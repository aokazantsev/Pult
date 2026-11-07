import json
import os
import queue
import sys
import threading
import time
import traceback

from .auth import Challenges, RateLimiter, ReplayWindow, login_string, parse_public, post_string, verify
from .codec import Invalid, JsonError, b64u, b64u_decode, is_token, is_uint, strict_json
from .config import PROTOCOL_VERSIONS

SIDES = ("pc", "phone")
CLOSE = object()
CLIENT_REASONS = ("user", "idle", "replaced", "pairing_rejected", "no_hello")
ALL = object()
ENVELOPE_FIELDS = {"v", "pair", "from", "id", "ts", "ttl", "ct"}
CLAIM_PC_FIELDS = {"pc_sign", "pc_agree"}
CLAIM_PHONE_FIELDS = {"phone_sign", "phone_agree", "tag"}
INTRUSION_GAP_SEC = 60
LIVE_BACKLOG_BYTES = 1048576
MESSAGE_OVERHEAD = 1024
SIGNAL_BACKLOG = 1000
BUDGET_LOCK = threading.Lock()


class RelayError(Exception):
    def __init__(self, status, code, **extra):
        super().__init__(code)
        self.status = status
        self.code = code
        self.extra = extra

    def payload(self):
        return {"error": self.code, **self.extra}


class Budget:
    def __init__(self, limit):
        self.limit = limit
        self.total = 0


class Stream:
    def __init__(self, side, stream_id, now, window, budgets=()):
        self.side = side
        self.id = stream_id
        self.outbox = queue.Queue()
        self.last_seen = now
        self.closed = False
        self.window = ReplayWindow(window)
        self.backlog = 0
        self.budgets = tuple(budgets)

    def reserve(self, size, limit):
        with BUDGET_LOCK:
            if self.backlog + size > limit or any(b.total + size > b.limit for b in self.budgets):
                return False
            self._add(size)
            return True

    def release(self, size):
        with BUDGET_LOCK:
            self._add(-size)

    def drop(self):
        with BUDGET_LOCK:
            self._add(-self.backlog)

    def _add(self, size):
        self.backlog += size
        for b in self.budgets:
            b.total += size


class Address:
    def __init__(self, limit):
        self.active = 0
        self.budget = Budget(limit)


def other(side):
    return "phone" if side == "pc" else "pc"


def _queued_size(message):
    return message["size"]


def message_size(env):
    return len(env["ct"]) + MESSAGE_OVERHEAD


def _parse(raw, duplicate_code):
    try:
        return strict_json(raw)
    except JsonError as e:
        raise RelayError(400, "bad_json" if e.kind == "syntax" else duplicate_code)


def _check_key(text):
    if parse_public(text) is None:
        raise RelayError(400, "bad_key")


def _form(data, fields, code):
    if not isinstance(data, dict) or set(data) != fields:
        raise RelayError(400, code)


class Core:
    def __init__(self, store, config, clock=time.time):
        self.store = store
        self.config = config
        self.clock = clock
        self.lock = threading.RLock()
        self.streams = {}
        self.challenges = Challenges(config.challenge_ttl, clock)
        self.claim_rate = RateLimiter(config.rate_claims_per_min, 60, clock)
        self.challenge_rate = RateLimiter(config.rate_challenges_per_min, 60, clock)
        self.write_rate = RateLimiter(config.rate_writes_per_min, 60, clock)
        self.last_intrusion = {}
        self.pc_down = {}
        self.claim_addr = {}
        self.backlog_limit = config.max_queue_bytes_per_pair + LIVE_BACKLOG_BYTES
        self.backlog_budget = Budget(config.max_total_queue_bytes)
        self.addresses = {}
        self._addresses_lock = threading.Lock()
        self._stop = threading.Event()
        now = clock()
        for pair_id, rec in store.pairs.items():
            if rec["status"] == "active":
                self.pc_down[pair_id] = now

    def enter(self, addr):
        with self._addresses_lock:
            entry = self.addresses.get(addr)
            if entry is None:
                entry = self.addresses[addr] = Address(self.config.max_total_queue_bytes // 2)
            if entry.active >= self.config.max_connections_per_addr:
                raise RelayError(429, "rate_limited")
            entry.active += 1

    def leave(self, addr):
        with self._addresses_lock:
            entry = self.addresses.get(addr)
            if entry is None:
                return
            entry.active -= 1
            if entry.active <= 0:
                del self.addresses[addr]

    def check_rate(self, kind, addr):
        limiter = self.claim_rate if kind == "claim" else self.challenge_rate
        if not limiter.allow(addr):
            raise RelayError(429, "rate_limited")

    def _pair(self, pair_id, save=True):
        rec = self.store.pairs.get(pair_id)
        if rec is None:
            raise RelayError(404, "not_found")
        now = self.clock()
        expired = (rec["status"] == "pending" and rec["created"] + self.config.claim_ttl <= now) or (
            rec["status"] == "deleted" and rec["deleted_at"] + self.config.deleted_keep_sec <= now
        )
        if expired:
            self._remove_pair(pair_id, save)
            raise RelayError(404, "not_found")
        return rec

    def _remove_pair(self, pair_id, save=True):
        self._close_streams(pair_id, None)
        self.store.pairs.pop(pair_id, None)
        self.pc_down.pop(pair_id, None)
        self._forget_signals(pair_id)
        self.claim_addr.pop(pair_id, None)
        if save:
            self.store.save_state()

    def _sweep_expired(self):
        removed = False
        for pair_id in [p for p, r in self.store.pairs.items() if r["status"] != "active"]:
            try:
                self._pair(pair_id, save=False)
            except RelayError:
                removed = True
        return removed

    def _deliver(self, pair_id, stream, seq, env):
        if stream.closed:
            return
        if stream.reserve(message_size(env), self.backlog_limit):
            stream.outbox.put(("message", seq, env))
        else:
            self.close_stream(pair_id, stream)

    def _close_streams(self, pair_id, signal):
        for side in SIDES:
            stream = self.streams.pop((pair_id, side), None)
            if stream:
                stream.closed = True
                if signal:
                    stream.outbox.put(("server", None, signal))
                stream.outbox.put(CLOSE)

    def _send(self, pair_id, side, payload):
        stream = self.streams.get((pair_id, side))
        if not stream:
            return
        if stream.outbox.qsize() >= SIGNAL_BACKLOG:
            self.close_stream(pair_id, stream)
            return
        stream.outbox.put(("server", None, payload))

    def _throttled(self, kind, pair_id):
        now = self.clock()
        key = (kind, pair_id)
        last = self.last_intrusion.get(key)
        if last is not None and now - last < INTRUSION_GAP_SEC:
            return True
        self.last_intrusion[key] = now
        return False

    def _forget_signals(self, pair_id):
        for kind in ("intrusion", "phone_incompatible"):
            self.last_intrusion.pop((kind, pair_id), None)

    def _intrusion(self, pair_id):
        if self._throttled("intrusion", pair_id):
            return
        for side in SIDES:
            self._send(pair_id, side, {"type": "intrusion"})

    def create_claim(self, raw, addr):
        data = _parse(raw, "bad_key")
        _form(data, CLAIM_PC_FIELDS, "bad_key")
        _check_key(data["pc_sign"])
        _check_key(data["pc_agree"])
        with self.lock:
            now = self.clock()
            if self._sweep_expired():
                self.store.save_state()
            pending = sum(1 for r in self.store.pairs.values() if r["status"] == "pending")
            mine = sum(1 for a in self.claim_addr.values() if a == addr)
            if pending >= self.config.max_pending or mine >= self.config.max_pairs_per_addr:
                raise RelayError(429, "rate_limited")
            if len(self.store.pairs) >= self.config.max_pairs:
                deleted = [p for p, r in self.store.pairs.items() if r["status"] == "deleted"]
                if not deleted:
                    raise RelayError(429, "rate_limited")
                self._remove_pair(min(deleted, key=lambda p: self.store.pairs[p]["deleted_at"]), save=False)
            pair_id = b64u(os.urandom(16))
            self.claim_addr[pair_id] = addr
            self.store.pairs[pair_id] = {
                "status": "pending",
                "created": now,
                "pc_sign": data["pc_sign"],
                "pc_agree": data["pc_agree"],
                "phone_sign": None,
                "phone_agree": None,
                "tag": None,
                "seq": 0,
                "queue": [],
                "seen": [],
            }
            self.store.save_state()
            return {"pair": pair_id, "ttl": self.config.claim_ttl}

    def submit_claim(self, pair_id, raw):
        with self.lock:
            rec = self._pair(pair_id)
            if rec["status"] != "pending":
                if rec["status"] == "active":
                    self._intrusion(pair_id)
                raise RelayError(409, "claim_used")
            data = _parse(raw, "bad_key")
            _form(data, CLAIM_PHONE_FIELDS, "bad_key")
            _check_key(data["phone_sign"])
            _check_key(data["phone_agree"])
            try:
                tag = b64u_decode(data["tag"])
            except Invalid:
                raise RelayError(400, "bad_tag")
            if len(tag) != 32:
                raise RelayError(400, "bad_tag")
            rec["status"] = "active"
            rec["phone_sign"] = data["phone_sign"]
            rec["phone_agree"] = data["phone_agree"]
            rec["tag"] = data["tag"]
            if (pair_id, "pc") not in self.streams:
                self.pc_down[pair_id] = self.clock()
            self._send(pair_id, "pc", self._pair_signal(rec))
            self.store.save_state()
            return {"pair": pair_id}

    @staticmethod
    def _pair_signal(rec):
        return {"type": "pair", "phone_sign": rec["phone_sign"], "phone_agree": rec["phone_agree"], "tag": rec["tag"]}

    def issue_challenge(self, pair_id, raw):
        data = _parse(raw, "bad_json")
        if not isinstance(data, dict) or set(data) != {"from"} or data["from"] not in SIDES:
            raise RelayError(400, "bad_json")
        side = data["from"]
        with self.lock:
            rec = self._pair(pair_id)
            if not rec.get(f"{side}_sign"):
                raise RelayError(404, "not_found")
            return {"challenge": self.challenges.issue(pair_id, side), "ttl": self.config.challenge_ttl}

    def login(self, pair_id, side, challenge, sig_text, addr=None):
        with self.lock:
            rec = self._pair(pair_id)
            public = rec.get(f"{side}_sign")
            if not public:
                raise RelayError(404, "not_found")
            fresh = self.challenges.consume(pair_id, side, challenge)
            if not (fresh and verify(public, login_string(pair_id, side, challenge), sig_text)):
                self._intrusion(pair_id)
                raise RelayError(401, "bad_signature")
            if rec["status"] == "deleted":
                raise RelayError(410, "pair_deleted", reason=rec["reason"], by=rec["by"])
            return self._open_stream(pair_id, side, rec, addr)

    def _open_stream(self, pair_id, side, rec, addr=None):
        now = self.clock()
        budgets = [self.backlog_budget]
        with self._addresses_lock:
            entry = self.addresses.get(addr)
        if entry is not None:
            budgets.append(entry.budget)
        stream = Stream(side, b64u(os.urandom(16)), now, self.config.replay_window, budgets)
        previous = self.streams.get((pair_id, side))
        if previous:
            previous.closed = True
            previous.outbox.put(CLOSE)
        stream.outbox.put(("server", None, {"type": "session", "stream": stream.id}))
        if side == "pc":
            self.pc_down.pop(pair_id, None)
            if addr is not None:
                self.claim_addr.setdefault(pair_id, addr)
            if rec["status"] == "active":
                stream.outbox.put(("server", None, self._pair_signal(rec)))
        if rec["status"] == "active":
            self._purge(rec)
            for m in rec["queue"]:
                if m["to"] != side:
                    continue
                if not stream.reserve(message_size(m["env"]), self.backlog_limit):
                    stream.outbox.put(CLOSE)
                    break
                stream.outbox.put(("message", m["seq"], m["env"]))
        if side == "pc":
            stream.outbox.put(("server", None, {"type": "phone_stream", "state": self.phone_state(pair_id)}))
        self.streams[(pair_id, side)] = stream
        if side == "phone" and not previous:
            self._send(pair_id, "pc", {"type": "phone_stream", "state": "open"})
        return stream

    def close_stream(self, pair_id, stream):
        with self.lock:
            if stream.closed:
                return
            stream.closed = True
            if self.streams.get((pair_id, stream.side)) is not stream:
                return
            del self.streams[(pair_id, stream.side)]
            stream.outbox.put(CLOSE)
            if stream.side == "pc":
                rec = self.store.pairs.get(pair_id)
                if rec and rec["status"] == "active":
                    self.pc_down[pair_id] = self.clock()
            else:
                self._send(pair_id, "pc", {"type": "phone_stream", "state": "closed"})

    def phone_state(self, pair_id):
        return "open" if (pair_id, "phone") in self.streams else "closed"

    def precheck(self, pair_id, side, stream_id):
        with self.lock:
            self._current_stream(pair_id, side, stream_id)

    def _current_stream(self, pair_id, side, stream_id):
        rec = self._pair(pair_id)
        stream = self.streams.get((pair_id, side))
        if stream is None or stream.closed or stream.id != stream_id:
            raise RelayError(401, "stale_stream")
        return rec, stream

    def signed(self, pair_id, side, stream_id, n_text, op, body, sig_text, addr=None):
        with self.lock:
            rec, stream = self._current_stream(pair_id, side, stream_id)
            data = post_string(op, pair_id, side, stream_id, n_text, body)
            if not verify(rec[f"{side}_sign"], data, sig_text):
                self._intrusion(pair_id)
                raise RelayError(401, "bad_signature")
            if not stream.window.accept(int(n_text)):
                raise RelayError(409, "replay")
            stream.last_seen = self.clock()
            if op == "ping":
                return 200, {"ok": True}
            if op == "state":
                return 200, self._state(pair_id)
            if op == "delete":
                return 200, self._delete(pair_id, side, body)
            if rec["status"] != "active":
                raise RelayError(409, "not_paired")
            if op == "messages":
                return 202, self._post(pair_id, side, rec, body, addr)
            return 200, self._ack(side, rec, body, addr)

    def _check_write(self, addr):
        if not self.write_rate.allow(addr):
            raise RelayError(429, "rate_limited")

    def _purge(self, rec):
        now = self.clock()
        rec["queue"] = [m for m in rec["queue"] if m["expires"] > now]

    def _validate_envelope(self, pair_id, side, env):
        if not isinstance(env, dict):
            raise RelayError(400, "bad_envelope")
        version = env.get("v")
        if is_uint(version) and version not in PROTOCOL_VERSIONS:
            raise RelayError(400, "unsupported_version", protocol=PROTOCOL_VERSIONS)
        if set(env) != ENVELOPE_FIELDS or not is_uint(version):
            raise RelayError(400, "bad_envelope")
        if env["pair"] != pair_id or env["from"] != side:
            raise RelayError(400, "bad_envelope")
        if not is_token(env["id"]) or not is_uint(env["ts"]) or not is_uint(env["ttl"]):
            raise RelayError(400, "bad_envelope")
        try:
            ct = b64u_decode(env["ct"])
        except Invalid:
            raise RelayError(400, "bad_envelope")
        if len(ct) < 12 + 16:
            raise RelayError(400, "bad_envelope")

    def _post(self, pair_id, side, rec, raw, addr=None):
        if len(raw) > self.config.max_envelope_bytes:
            raise RelayError(413, "too_large")
        env = _parse(raw, "bad_envelope")
        try:
            self._validate_envelope(pair_id, side, env)
        except RelayError as e:
            if e.code == "unsupported_version" and side == "phone" and not self._throttled("phone_incompatible", pair_id):
                self._send(pair_id, "pc", {"type": "phone_incompatible", "v": env["v"]})
            raise
        if env["id"] in rec["seen"] or any(m["env"]["id"] == env["id"] for m in rec["queue"]):
            return {"seq": None, "dup": True}
        raw_size = len(raw)
        ttl = min(env["ttl"], self.config.max_ttl)
        target = other(side)
        if ttl > 0:
            self._purge(rec)
            queued = [m for m in rec["queue"] if m["to"] == target]
            if len(queued) >= self.config.max_queue_per_pair:
                raise RelayError(429, "queue_full")
            if sum(_queued_size(m) for m in queued) + raw_size > self.config.max_queue_bytes_per_pair:
                raise RelayError(507, "queue_bytes_full")
            cost = raw_size + MESSAGE_OVERHEAD
            if self._queued() + cost > self.config.max_total_queue_bytes:
                raise RelayError(507, "queue_bytes_full")
            owner = self.claim_addr.setdefault(pair_id, addr) if addr is not None else self.claim_addr.get(pair_id)
            if self._queued(owner) + cost > self.config.max_total_queue_bytes // 3:
                raise RelayError(507, "queue_bytes_full")
            self._check_write(addr)
        rec["seq"] += 1
        seq = rec["seq"]
        rec["seen"].append(env["id"])
        del rec["seen"][: -max(self.config.seen_ids_per_pair, 2 * self.config.max_queue_per_pair)]
        if ttl > 0:
            rec["queue"].append({"seq": seq, "to": target, "env": env, "size": raw_size, "expires": self.clock() + ttl})
        stream = self.streams.get((pair_id, target))
        if stream:
            self._deliver(pair_id, stream, seq, env)
        if ttl > 0:
            self.store.save_state()
        return {"seq": seq, "dup": False}

    def _queued(self, owner=ALL):
        now = self.clock()
        return sum(
            _queued_size(m) + MESSAGE_OVERHEAD
            for p, r in self.store.pairs.items()
            if owner is ALL or self.claim_addr.get(p) == owner
            for m in r["queue"]
            if m["expires"] > now
        )

    def _ack(self, side, rec, raw, addr=None):
        data = _parse(raw, "bad_ack")
        ids = data.get("ids") if isinstance(data, dict) and set(data) == {"ids"} else None
        if not isinstance(ids, list) or not all(is_token(i) for i in ids):
            raise RelayError(400, "bad_ack")
        ids = set(ids)
        before = len(rec["queue"])
        if any(m["to"] == side and m["env"]["id"] in ids for m in rec["queue"]):
            self._check_write(addr)
        rec["queue"] = [m for m in rec["queue"] if not (m["to"] == side and m["env"]["id"] in ids)]
        if len(rec["queue"]) != before:
            self.store.save_state()
        return {"removed": before - len(rec["queue"])}

    def _delete(self, pair_id, side, raw):
        data = _parse(raw, "bad_json")
        if not isinstance(data, dict) or set(data) != {"reason"} or data["reason"] not in CLIENT_REASONS:
            raise RelayError(400, "bad_reason")
        self.delete_pair(pair_id, data["reason"], side)
        return {"ok": True}

    def delete_pair(self, pair_id, reason, by, save=True):
        rec = self.store.pairs[pair_id]
        signal = {"type": "pair_deleted", "reason": reason, "by": by}
        self._close_streams(pair_id, signal)
        self.pc_down.pop(pair_id, None)
        self.claim_addr.pop(pair_id, None)
        if rec["status"] == "pending":
            self.store.pairs.pop(pair_id, None)
            self._forget_signals(pair_id)
        else:
            rec.update(status="deleted", reason=reason, by=by, deleted_at=self.clock(), queue=[], seen=[])
        if save:
            self.store.save_state()

    def _state(self, pair_id):
        rec = self.store.pairs[pair_id]
        self._purge(rec)
        return {
            "pc_stream": "open" if (pair_id, "pc") in self.streams else "closed",
            "phone_stream": self.phone_state(pair_id),
            "queued_to_pc": sum(1 for m in rec["queue"] if m["to"] == "pc"),
            "queued_to_phone": sum(1 for m in rec["queue"] if m["to"] == "phone"),
        }

    def reap(self):
        with self.lock:
            now = self.clock()
            for (pair_id, _), stream in list(self.streams.items()):
                if now - stream.last_seen > self.config.liveness_sec:
                    self.close_stream(pair_id, stream)
            changed = self._sweep_expired()
            for rec in self.store.pairs.values():
                before = len(rec["queue"])
                self._purge(rec)
                changed = changed or len(rec["queue"]) != before
            for pair_id in list(self.store.pairs):
                rec = self.store.pairs[pair_id]
                since = self.pc_down.get(pair_id)
                if rec["status"] == "active" and since is not None and now - since > self.config.pc_offline_sec:
                    self.delete_pair(pair_id, "pc_offline", "server", save=False)
                    changed = True
            if changed:
                self.store.save_state()

    def start_watchdog(self):
        def loop():
            while not self._stop.wait(self.config.watchdog_sec):
                try:
                    self.reap()
                except Exception:
                    traceback.print_exc(file=sys.stderr)

        threading.Thread(target=loop, daemon=True).start()

    def stop(self):
        self._stop.set()


def format_sse(kind, seq, payload):
    head = f"id: {seq}\n" if seq is not None else ""
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    return f"{head}event: {kind}\ndata: {body}\n\n".encode("utf-8")
