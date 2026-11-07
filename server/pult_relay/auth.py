import functools
import hashlib
import hmac
import os
import threading
from collections import deque

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import encode_dss_signature

from .codec import MAX_SAFE_INT, Invalid, b64u, b64u_decode

CURVE_N = 0xFFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551


@functools.lru_cache(maxsize=2048)
def _parse_public(text):
    try:
        raw = b64u_decode(text)
    except Invalid:
        return None
    if len(raw) != 65 or raw[0] != 4:
        return None
    try:
        return ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), raw)
    except (ValueError, TypeError):
        return None


def parse_public(text):
    if not isinstance(text, str):
        return None
    return _parse_public(text)


def verify(public_text, data, sig_text):
    public = parse_public(public_text)
    if public is None:
        return False
    try:
        sig = b64u_decode(sig_text)
    except Invalid:
        return False
    if len(sig) != 64:
        return False
    r = int.from_bytes(sig[:32], "big")
    s = int.from_bytes(sig[32:], "big")
    if not (1 <= r < CURVE_N and 1 <= s < CURVE_N):
        return False
    try:
        public.verify(encode_dss_signature(r, s), data, ec.ECDSA(hashes.SHA256()))
        return True
    except InvalidSignature:
        return False


def login_string(pair, side, challenge):
    return "\n".join(["pult-v2-login", pair, side, challenge]).encode("utf-8")


def post_string(op, pair, side, stream, n_text, body):
    digest = b64u(hashlib.sha256(body).digest())
    return "\n".join(["pult-v2-post", op, pair, side, stream, n_text, digest]).encode("utf-8")


class ReplayWindow:
    def __init__(self, size=128):
        self.size = size
        self.top = 0
        self.seen = set()

    def accept(self, n):
        if not isinstance(n, int) or isinstance(n, bool) or n < 1 or n > MAX_SAFE_INT:
            return False
        if n > self.top:
            self.top = n
            self.seen = {m for m in self.seen if m > n - self.size}
            self.seen.add(n)
            return True
        if n <= self.top - self.size or n in self.seen:
            return False
        self.seen.add(n)
        return True


class Challenges:
    RANDOM = 12
    MAC = 16

    def __init__(self, ttl, clock):
        self.ttl = ttl
        self.clock = clock
        self.key = os.urandom(32)
        self.used = {}
        self._swept = 0.0
        self._lock = threading.Lock()

    def _mac(self, pair, side, exp_bytes, rnd):
        data = b"\n".join([pair.encode("utf-8"), side.encode("utf-8"), exp_bytes, rnd])
        return hmac.new(self.key, data, hashlib.sha256).digest()[: self.MAC]

    def issue(self, pair, side):
        exp = int(self.clock()) + self.ttl
        exp_bytes = exp.to_bytes(4, "big")
        rnd = os.urandom(self.RANDOM)
        return b64u(exp_bytes + rnd + self._mac(pair, side, exp_bytes, rnd))

    def consume(self, pair, side, text):
        try:
            raw = b64u_decode(text)
        except Invalid:
            return False
        if len(raw) != 4 + self.RANDOM + self.MAC:
            return False
        exp_bytes, rnd, mac = raw[:4], raw[4 : 4 + self.RANDOM], raw[4 + self.RANDOM :]
        if not hmac.compare_digest(mac, self._mac(pair, side, exp_bytes, rnd)):
            return False
        now = self.clock()
        with self._lock:
            self._sweep(now)
            if int.from_bytes(exp_bytes, "big") < now or text in self.used:
                return False
            self.used[text] = int.from_bytes(exp_bytes, "big")
            return True

    def _sweep(self, now):
        if now - self._swept < 5 and len(self.used) < 100000:
            return
        self._swept = now
        self.used = {t: e for t, e in self.used.items() if e >= now}


class RateLimiter:
    def __init__(self, limit, window, clock):
        self.limit = limit
        self.window = window
        self.clock = clock
        self.hits = {}
        self._lock = threading.Lock()
        self._swept = 0.0

    def allow(self, key):
        now = self.clock()
        with self._lock:
            if now - self._swept > self.window:
                self._swept = now
                self.hits = {k: q for k, q in self.hits.items() if q and q[-1] > now - self.window}
            q = self.hits.setdefault(key, deque())
            while q and q[0] <= now - self.window:
                q.popleft()
            if len(q) >= self.limit:
                return False
            q.append(now)
            return True
