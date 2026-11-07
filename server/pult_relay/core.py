import json
import os
import queue
import threading
import time

from .auth import Challenges, RateLimiter, ReplayWindow, login_string, parse_public, post_string, verify
from .codec import Invalid, JsonError, b64u, b64u_decode, is_token, is_uint, strict_json
from .config import PROTOCOL_VERSIONS

SIDES = ("pc", "phone")
CLOSE = object()
CLIENT_REASONS = ("user", "idle", "replaced", "pairing_rejected", "no_hello")
ENVELOPE_FIELDS = {"v", "pair", "from", "id", "ts", "ttl", "ct"}
CLAIM_PC_FIELDS = {"pc_sign", "pc_agree"}
CLAIM_PHONE_FIELDS = {"phone_sign", "phone_agree", "tag"}
INTRUSION_GAP_SEC = 60


class RelayError(Exception):
    def __init__(self, status, code, **extra):
        super().__init__(code)
        self.status = status
        self.code = code
        self.extra = extra

    def payload(self):
        return {"error": self.code, **self.extra}


class Stream:
    def __init__(self, side, stream_id, now, window):
        self.side = side
        self.id = stream_id
        self.outbox = queue.Queue()
        self.last_seen = now
        self.closed = False
        self.window = ReplayWindow(window)


def other(side):
    return "phone" if side == "pc" else "pc"


def _queued_size(message):
    size = message.get("size")
    if isinstance(size, int):
        return size
    return len(json.dumps(message["env"], ensure_ascii=False).encode("utf-8"))


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
        self.last_intrusion = {}
        self.pc_down = {}
        self._stop = threading.Event()
        now = clock()
        for pair_id, rec in store.pairs.items():
            if rec["status"] == "active":
                self.pc_down[pair_id] = now

    def check_rate(self, kind, addr):
        limiter = self.claim_rate if kind == "claim" else self.challenge_rate
        if not limiter.allow(addr):
            raise RelayError(429, "rate_limited")

    def _pair(self, pair_id):
        rec = self.store.pairs.get(pair_id)
        if rec is None:
            raise RelayError(404, "not_found")
        now = self.clock()
        expired = (rec["status"] == "pending" and rec["created"] + self.config.claim_ttl <= now) or (
            rec["status"] == "deleted" and rec["deleted_at"] + self.config.deleted_keep_sec <= now
        )
        if expired:
            self._remove_pair(pair_id)
            raise RelayError(404, "not_found")
        return rec

    def _remove_pair(self, pair_id):
        self._close_streams(pair_id, None)
        self.store.pairs.pop(pair_id, None)
        self.pc_down.pop(pair_id, None)
        self.last_intrusion.pop(pair_id, None)
        self.store.save_state()

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
        if stream:
            stream.outbox.put(("server", None, payload))

    def _intrusion(self, pair_id):
        now = self.clock()
        last = self.last_intrusion.get(pair_id)
        if last is not None and now - last < INTRUSION_GAP_SEC:
            return
        self.last_intrusion[pair_id] = now
        for side in SIDES:
            self._send(pair_id, side, {"type": "intrusion"})

    def create_claim(self, raw):
        data = _parse(raw, "bad_key")
        _form(data, CLAIM_PC_FIELDS, "bad_key")
        _check_key(data["pc_sign"])
        _check_key(data["pc_agree"])
        with self.lock:
            now = self.clock()
            for pair_id in [p for p, r in self.store.pairs.items() if r["status"] == "pending"]:
                try:
                    self._pair(pair_id)
                except RelayError:
                    pass
            pending = sum(1 for r in self.store.pairs.values() if r["status"] == "pending")
            if pending >= self.config.max_pending:
                raise RelayError(429, "rate_limited")
            pair_id = b64u(os.urandom(16))
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
            self.store.save_state()
            self._send(pair_id, "pc", self._pair_signal(rec))
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

    def login(self, pair_id, side, challenge, sig_text):
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
            return self._open_stream(pair_id, side, rec)

    def _open_stream(self, pair_id, side, rec):
        now = self.clock()
        stream = Stream(side, b64u(os.urandom(16)), now, self.config.replay_window)
        previous = self.streams.get((pair_id, side))
        if previous:
            previous.closed = True
            previous.outbox.put(CLOSE)
        stream.outbox.put(("server", None, {"type": "session", "stream": stream.id}))
        if side == "pc":
            self.pc_down.pop(pair_id, None)
            if rec["status"] == "active":
                stream.outbox.put(("server", None, self._pair_signal(rec)))
            stream.outbox.put(("server", None, {"type": "phone_stream", "state": self.phone_state(pair_id)}))
        if rec["status"] == "active":
            self._purge(rec)
            for m in rec["queue"]:
                if m["to"] == side:
                    stream.outbox.put(("message", m["seq"], m["env"]))
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

    def signed(self, pair_id, side, stream_id, n_text, op, body, sig_text):
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
                return 202, self._post(pair_id, side, rec, body)
            return 200, self._ack(pair_id, side, rec, body)

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

    def _post(self, pair_id, side, rec, raw):
        if len(raw) > self.config.max_envelope_bytes:
            raise RelayError(413, "too_large")
        env = _parse(raw, "bad_envelope")
        try:
            self._validate_envelope(pair_id, side, env)
        except RelayError as e:
            if e.code == "unsupported_version" and side == "phone":
                self._send(pair_id, "pc", {"type": "phone_incompatible", "v": env["v"]})
            raise
        if env["id"] in rec["seen"]:
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
        rec["seq"] += 1
        seq = rec["seq"]
        rec["seen"].append(env["id"])
        del rec["seen"][: -self.config.seen_ids_per_pair]
        if ttl > 0:
            rec["queue"].append({"seq": seq, "to": target, "env": env, "size": raw_size, "expires": self.clock() + ttl})
        self.store.save_state()
        stream = self.streams.get((pair_id, target))
        if stream:
            stream.outbox.put(("message", seq, env))
        return {"seq": seq, "dup": False}

    def _ack(self, pair_id, side, rec, raw):
        data = _parse(raw, "bad_ack")
        ids = data.get("ids") if isinstance(data, dict) and set(data) == {"ids"} else None
        if not isinstance(ids, list) or not all(is_token(i) for i in ids):
            raise RelayError(400, "bad_ack")
        ids = set(ids)
        before = len(rec["queue"])
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

    def delete_pair(self, pair_id, reason, by):
        rec = self.store.pairs[pair_id]
        signal = {"type": "pair_deleted", "reason": reason, "by": by}
        self._close_streams(pair_id, signal)
        self.pc_down.pop(pair_id, None)
        if rec["status"] == "pending":
            self.store.pairs.pop(pair_id, None)
            self.last_intrusion.pop(pair_id, None)
        else:
            rec.update(status="deleted", reason=reason, by=by, deleted_at=self.clock(), queue=[], seen=[])
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
            for pair_id in list(self.store.pairs):
                try:
                    rec = self._pair(pair_id)
                except RelayError:
                    continue
                since = self.pc_down.get(pair_id)
                if rec["status"] == "active" and since is not None and now - since > self.config.pc_offline_sec:
                    self.delete_pair(pair_id, "pc_offline", "server")

    def start_watchdog(self):
        def loop():
            while not self._stop.wait(self.config.watchdog_sec):
                self.reap()

        threading.Thread(target=loop, daemon=True).start()

    def stop(self):
        self._stop.set()


def format_sse(kind, seq, payload):
    head = f"id: {seq}\n" if seq is not None else ""
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    return f"{head}event: {kind}\ndata: {body}\n\n".encode("utf-8")
