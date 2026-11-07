import hmac
import json
import queue
import threading
import time

from .config import PROTOCOL_VERSIONS

SIDES = ("pc", "phone")
CLOSE = object()


class RelayError(Exception):
    def __init__(self, status, code):
        super().__init__(code)
        self.status = status
        self.code = code


class Stream:
    def __init__(self, side, now):
        self.side = side
        self.outbox = queue.Queue()
        self.last_seen = now
        self.closed = False


def _queued_size(message):
    size = message.get("size")
    if isinstance(size, int):
        return size
    return len(json.dumps(message["env"], ensure_ascii=False).encode("utf-8"))


def other(side):
    return "phone" if side == "pc" else "pc"


class Core:
    def __init__(self, store, config, clock=time.time):
        self.store = store
        self.config = config
        self.clock = clock
        self.lock = threading.RLock()
        self.streams = {}
        self._stop = threading.Event()

    def authenticate(self, pair_id, token):
        tokens = self.store.pairs.get(pair_id)
        if not tokens or not token:
            raise RelayError(401, "unauthorized")
        for side in SIDES:
            if hmac.compare_digest(tokens[side].encode("utf-8"), token.encode("utf-8")):
                return side
        raise RelayError(401, "unauthorized")

    def _purge(self, ps):
        now = self.clock()
        ps["queue"] = [m for m in ps["queue"] if m["expires"] > now]

    def validate_envelope(self, pair_id, side, env):
        if not isinstance(env, dict):
            raise RelayError(400, "bad_envelope")
        if env.get("v") not in PROTOCOL_VERSIONS:
            raise RelayError(400, "unsupported_version")
        if env.get("pair") != pair_id or env.get("from") != side:
            raise RelayError(400, "bad_envelope")
        mid = env.get("id")
        if not isinstance(mid, str) or not mid or len(mid) > 64:
            raise RelayError(400, "bad_envelope")
        ttl = env.get("ttl")
        if not isinstance(ttl, int) or isinstance(ttl, bool) or ttl < 0:
            raise RelayError(400, "bad_envelope")
        if "inner" not in env and "ct" not in env:
            raise RelayError(400, "bad_envelope")

    def post(self, pair_id, side, env, raw_size):
        if raw_size > self.config.max_envelope_bytes:
            raise RelayError(413, "too_large")
        self.validate_envelope(pair_id, side, env)
        with self.lock:
            self.touch(pair_id, side)
            ps = self.store.pair_state(pair_id)
            if env["id"] in ps["seen"]:
                return {"seq": None, "dup": True}
            ttl = min(env["ttl"], self.config.max_ttl)
            target = other(side)
            if ttl > 0:
                self._purge(ps)
                queued = [m for m in ps["queue"] if m["to"] == target]
                if len(queued) >= self.config.max_queue_per_pair:
                    raise RelayError(429, "queue_full")
                if sum(_queued_size(m) for m in queued) + raw_size > self.config.max_queue_bytes_per_pair:
                    raise RelayError(507, "queue_bytes_full")
            ps["seq"] += 1
            seq = ps["seq"]
            ps["seen"].append(env["id"])
            del ps["seen"][: -self.config.seen_ids_per_pair]
            if ttl > 0:
                ps["queue"].append({"seq": seq, "to": target, "env": env, "size": raw_size, "expires": self.clock() + ttl})
            self.store.save_state()
            stream = self.streams.get((pair_id, target))
            if stream:
                stream.outbox.put(("message", seq, env))
            return {"seq": seq, "dup": False}

    def ack(self, pair_id, side, ids):
        if not isinstance(ids, list):
            raise RelayError(400, "bad_ack")
        with self.lock:
            self.touch(pair_id, side)
            ps = self.store.pair_state(pair_id)
            ids = set(ids)
            before = len(ps["queue"])
            ps["queue"] = [m for m in ps["queue"] if not (m["to"] == side and m["env"]["id"] in ids)]
            if len(ps["queue"]) != before:
                self.store.save_state()
            return {"removed": before - len(ps["queue"])}

    def touch(self, pair_id, side):
        stream = self.streams.get((pair_id, side))
        if stream:
            stream.last_seen = self.clock()

    def phone_state(self, pair_id):
        return "open" if (pair_id, "phone") in self.streams else "closed"

    def open_stream(self, pair_id, side):
        with self.lock:
            now = self.clock()
            ps = self.store.pair_state(pair_id)
            self._purge(ps)
            stream = Stream(side, now)
            previous = self.streams.get((pair_id, side))
            if previous:
                previous.outbox.put(CLOSE)
                previous.closed = True
            if side == "pc":
                stream.outbox.put(("server", None, {"type": "phone_stream", "state": self.phone_state(pair_id)}))
            for m in ps["queue"]:
                if m["to"] == side:
                    stream.outbox.put(("message", m["seq"], m["env"]))
            self.streams[(pair_id, side)] = stream
            if side == "phone" and not previous:
                self._signal_pc(pair_id, "open")
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
            if stream.side == "phone":
                self._signal_pc(pair_id, "closed")

    def _signal_pc(self, pair_id, state):
        pc = self.streams.get((pair_id, "pc"))
        if pc:
            pc.outbox.put(("server", None, {"type": "phone_stream", "state": state}))

    def reap_dead(self):
        with self.lock:
            now = self.clock()
            for (pair_id, _), stream in list(self.streams.items()):
                if now - stream.last_seen > self.config.liveness_sec:
                    self.close_stream(pair_id, stream)

    def state(self, pair_id):
        with self.lock:
            ps = self.store.pair_state(pair_id)
            self._purge(ps)
            return {
                "pc_stream": "open" if (pair_id, "pc") in self.streams else "closed",
                "phone_stream": self.phone_state(pair_id),
                "queued_to_pc": sum(1 for m in ps["queue"] if m["to"] == "pc"),
                "queued_to_phone": sum(1 for m in ps["queue"] if m["to"] == "phone"),
            }

    def start_watchdog(self):
        def loop():
            while not self._stop.wait(self.config.watchdog_sec):
                self.reap_dead()

        threading.Thread(target=loop, daemon=True).start()

    def stop(self):
        self._stop.set()


def format_sse(kind, seq, payload):
    head = f"id: {seq}\n" if seq is not None else ""
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    return f"{head}event: {kind}\ndata: {body}\n\n".encode("utf-8")
