import base64
import hashlib
import hmac
import http.client
import json
import os
import queue
import re
import secrets
import threading
import time
import urllib.parse

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

PROTOCOL = [2]
B64U_RE = re.compile(r"[A-Za-z0-9_-]*")
TOKEN_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")


class ClientError(Exception):
    pass


def b64u(raw):
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode("ascii")


def b64u_decode(text):
    if not isinstance(text, str) or not B64U_RE.fullmatch(text) or len(text) % 4 == 1:
        raise ClientError("base64url")
    raw = base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))
    if b64u(raw) != text:
        raise ClientError("base64url")
    return raw


def sha256(data):
    return hashlib.sha256(data).digest()


def hmac_sha256(key, data):
    return hmac.new(key, data, hashlib.sha256).digest()


def public_text(key):
    raw = key.public_key().public_bytes(serialization.Encoding.X962, serialization.PublicFormat.UncompressedPoint)
    return b64u(raw)


def load_public(text):
    raw = b64u_decode(text)
    if len(raw) != 65 or raw[0] != 4:
        raise ClientError("public key")
    return ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), raw)


def sign_p1363(key, data):
    r, s = decode_dss_signature(key.sign(data, ec.ECDSA(hashes.SHA256())))
    return b64u(r.to_bytes(32, "big") + s.to_bytes(32, "big"))


def strict_json(text):
    def pairs(items):
        keys = [k for k, _ in items]
        if len(keys) != len(set(keys)):
            raise ClientError("duplicate JSON key")
        return dict(items)

    def constant(name):
        raise ClientError("JSON constant")

    return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)


def compact(obj):
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False)


def login_string(pair, side, challenge):
    return "\n".join(["pult-v2-login", pair, side, challenge]).encode("utf-8")


def post_string(op, pair, side, stream, n, body):
    return "\n".join(["pult-v2-post", op, pair, side, stream, str(n), b64u(sha256(body))]).encode("utf-8")


def transcript(pair, pc_sign, pc_agree, phone_sign, phone_agree):
    return "\n".join([pair, pc_sign, pc_agree, phone_sign, phone_agree])


def claim_tag(secret, text):
    return hmac_sha256(secret, b"pult-v2-claim\n" + text.encode("utf-8"))


def fingerprint(text):
    digest = sha256(b"pult-v2-fingerprint\n" + text.encode("utf-8"))
    digits = "%016d" % (int.from_bytes(digest[:8], "big") % 10**16)
    return " ".join(digits[i : i + 4] for i in range(0, 16, 4))


def derive_keys(own_agree, peer_agree_text, text):
    z = own_agree.exchange(ec.ECDH(), load_public(peer_agree_text))
    salt = sha256(b"pult-v2-salt\n" + text.encode("utf-8"))
    prk = hmac_sha256(salt, z)
    return {
        "pc": hmac_sha256(prk, b"pult-v2-key-pc-phone\x01"),
        "phone": hmac_sha256(prk, b"pult-v2-key-phone-pc\x01"),
    }


def envelope_aad(env):
    parts = ["pult-v2-env", str(env["v"]), env["pair"], env["from"], env["id"], str(env["ts"]), str(env["ttl"])]
    return "\n".join(parts).encode("utf-8")


def seal(key, pair, side, mid, ttl, body, ts=None, nonce=None):
    env = {"v": 2, "pair": pair, "from": side, "id": mid, "ts": int(time.time()) if ts is None else ts, "ttl": ttl}
    nonce = nonce or os.urandom(12)
    plain = compact(body).encode("utf-8")
    env["ct"] = b64u(nonce + AESGCM(key).encrypt(nonce, plain, envelope_aad(env)))
    return env


def open_envelope(keys, env, pair, receiver):
    if not isinstance(env, dict) or set(env) != {"v", "pair", "from", "id", "ts", "ttl", "ct"}:
        raise ClientError("envelope fields")
    for name in ("v", "ts", "ttl"):
        if not isinstance(env[name], int) or isinstance(env[name], bool) or env[name] < 0:
            raise ClientError(f"envelope {name}")
    if env["v"] != 2:
        raise ClientError("unsupported version")
    if env["pair"] != pair or env["from"] not in ("pc", "phone") or env["from"] == receiver:
        raise ClientError("envelope routing")
    if not isinstance(env["id"], str) or not TOKEN_RE.fullmatch(env["id"]):
        raise ClientError("envelope id")
    raw = b64u_decode(env["ct"])
    if len(raw) < 28:
        raise ClientError("ct too short")
    try:
        plain = AESGCM(keys[env["from"]]).decrypt(raw[:12], raw[12:], envelope_aad(env))
    except Exception as e:
        raise ClientError("authentication failed") from e
    return strict_json(plain.decode("utf-8"))


class KeyPair:
    def __init__(self, sign=None, agree=None):
        self.sign = sign or ec.generate_private_key(ec.SECP256R1())
        self.agree = agree or ec.generate_private_key(ec.SECP256R1())

    @property
    def sign_public(self):
        return public_text(self.sign)

    @property
    def agree_public(self):
        return public_text(self.agree)

    def dump(self):
        def d(key):
            return "%064x" % key.private_numbers().private_value

        return {"sign": d(self.sign), "agree": d(self.agree)}

    @staticmethod
    def load(data):
        return KeyPair(
            ec.derive_private_key(int(data["sign"], 16), ec.SECP256R1()),
            ec.derive_private_key(int(data["agree"], 16), ec.SECP256R1()),
        )


class Http:
    def __init__(self, base_url, timeout=10):
        parts = urllib.parse.urlsplit(base_url)
        self.scheme = parts.scheme
        self.host = parts.hostname
        self.port = parts.port or (443 if parts.scheme == "https" else 80)
        self.prefix = parts.path.rstrip("/")
        self.timeout = timeout

    def connection(self):
        cls = http.client.HTTPSConnection if self.scheme == "https" else http.client.HTTPConnection
        return cls(self.host, self.port, timeout=self.timeout)

    def request(self, method, path, body=None, headers=None):
        if isinstance(body, (dict, list)):
            body = compact(body).encode("utf-8")
        headers = dict(headers or {})
        if body is not None:
            headers.setdefault("Content-Type", "application/json")
        conn = self.connection()
        try:
            conn.request(method, self.prefix + path, body=body, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            try:
                parsed = json.loads(data) if data else None
            except ValueError:
                parsed = None
            return resp.status, parsed
        finally:
            conn.close()


class Sse:
    def __init__(self, http_client, path, headers):
        self.events = queue.Queue()
        self.conn = http_client.connection()
        self.conn.request("GET", http_client.prefix + path, headers=headers)
        self.sock = self.conn.sock
        self.response = self.conn.getresponse()
        self.status = self.response.status
        self.error = None
        if self.status != 200:
            data = self.response.read()
            self.conn.close()
            try:
                self.error = json.loads(data)
            except ValueError:
                self.error = {"error": "http_%d" % self.status}
            return
        self.thread = threading.Thread(target=self._read, daemon=True)
        self.thread.start()

    def _read(self):
        kind = data = None
        try:
            while True:
                line = self.response.fp.readline()
                if not line:
                    break
                line = line.decode("utf-8").rstrip("\r\n")
                if line.startswith("event: "):
                    kind = line[7:]
                elif line.startswith("data: "):
                    data = json.loads(line[6:])
                elif line == "" and kind:
                    self.events.put((kind, data))
                    kind = data = None
        except (OSError, ValueError, http.client.HTTPException):
            pass
        self.events.put(("eof", None))

    def close(self):
        try:
            if self.sock:
                self.sock.shutdown(2)
            self.response.close()
            self.conn.close()
        except OSError:
            pass


class LoginRefused(ClientError):
    def __init__(self, status, body):
        super().__init__(f"{status} {body}")
        self.status = status
        self.body = body


class Peer:
    def __init__(self, base_url, side):
        self.base_url = base_url.rstrip("/")
        self.side = side
        self.http = Http(self.base_url)
        self.keys = None
        self.pair = None
        self.peer_sign = None
        self.peer_agree = None
        self.text = None
        self.dir_keys = None
        self.stream_id = None
        self.n = 0
        self.sse = None
        self.counter = 0
        self.seen = set()
        self.token = secrets.token_hex(3)
        self.pending_server = []

    @property
    def peer_side(self):
        return "phone" if self.side == "pc" else "pc"

    def info(self):
        status, body = self.http.request("GET", "/info")
        if status != 200:
            raise ClientError(f"/info {status}")
        return body

    def challenge(self):
        status, body = self.http.request("POST", f"/v2/pairs/{self.pair}/challenge", {"from": self.side})
        if status != 200:
            raise LoginRefused(status, body)
        return body["challenge"]

    def login(self, timeout=10):
        if self.sse:
            self.sse.close()
        challenge = self.challenge()
        sig = sign_p1363(self.keys.sign, login_string(self.pair, self.side, challenge))
        headers = {"Pult-From": self.side, "Pult-Challenge": challenge, "Pult-Sig": sig}
        sse = Sse(self.http, f"/v2/pairs/{self.pair}/stream", headers)
        if sse.status != 200:
            raise LoginRefused(sse.status, sse.error)
        self.sse = sse
        self.n = 0
        self.stream_id = None
        deadline = time.time() + timeout
        while time.time() < deadline:
            kind, data = self.sse.events.get(timeout=max(0.01, deadline - time.time()))
            if kind == "server" and data.get("type") == "session":
                self.stream_id = data["stream"]
                return self.stream_id
            if kind == "eof":
                break
            self.pending_server.append((kind, data))
        raise ClientError("no session event")

    def signed(self, op, body=b"", method="POST"):
        if isinstance(body, (dict, list)):
            body = compact(body).encode("utf-8")
        for attempt in (0, 1):
            self.n += 1
            sig = sign_p1363(self.keys.sign, post_string(op, self.pair, self.side, self.stream_id, self.n, body))
            headers = {
                "Pult-From": self.side,
                "Pult-Stream": self.stream_id,
                "Pult-N": str(self.n),
                "Pult-Sig": sig,
            }
            status, data = self.http.request(method, f"/v2/pairs/{self.pair}/{op}", body if method == "POST" else None, headers)
            if status == 401 and data and data.get("error") == "stale_stream" and attempt == 0:
                self.login()
                continue
            return status, data

    def ping(self):
        return self.signed("ping")

    def state(self):
        return self.signed("state", method="GET")

    def ack(self, ids):
        return self.signed("acks", {"ids": list(ids)})

    def delete(self, reason):
        return self.signed("delete", {"reason": reason})

    def send(self, body, ttl=600, mid=None):
        self.counter += 1
        mid = mid or f"{self.side}-{self.token}-{self.counter:04d}"
        env = seal(self.dir_keys[self.side], self.pair, self.side, mid, ttl, body)
        status, data = self.signed("messages", compact(env).encode("utf-8"))
        return status, data, env

    def next_event(self, timeout=5):
        if self.pending_server:
            return self.pending_server.pop(0)
        return self.sse.events.get(timeout=timeout)

    def receive(self, timeout=5, ack=True):
        deadline = time.time() + timeout
        while True:
            left = deadline - time.time()
            if left <= 0:
                raise queue.Empty
            kind, data = self.next_event(left)
            if kind == "server":
                return "server", data
            if kind == "eof":
                return "eof", None
            if kind != "message":
                continue
            try:
                body = open_envelope(self.dir_keys, data, self.pair, self.side)
            except ClientError as e:
                return "reject", {"error": str(e), "envelope": data}
            fresh = data["id"] not in self.seen
            self.seen.add(data["id"])
            if ack:
                self.ack([data["id"]])
            if fresh:
                return "message", body

    def hello(self):
        return self.send({"type": "hello", "protocol": PROTOCOL, "capabilities": []}, ttl=0)

    def close(self):
        if self.sse:
            self.sse.close()
            self.sse = None


class Pc(Peer):
    def __init__(self, base_url):
        super().__init__(base_url, "pc")
        self.secret = None

    def claim(self, idle_seconds=14400, advertised_url=None):
        info = self.info()
        if 2 not in info.get("protocol", []):
            raise ClientError("server is incompatible")
        self.keys = KeyPair()
        self.secret = os.urandom(32)
        status, body = self.http.request(
            "POST", "/v2/claims", {"pc_sign": self.keys.sign_public, "pc_agree": self.keys.agree_public}
        )
        if status != 201:
            raise ClientError(f"claim {status} {body}")
        self.pair = body["pair"]
        qr = {
            "v": PROTOCOL,
            "s": advertised_url or self.base_url,
            "c": self.pair,
            "k": b64u(self.secret),
            "ps": self.keys.sign_public,
            "pa": self.keys.agree_public,
            "n": idle_seconds,
        }
        return compact(qr)

    def await_phone(self, timeout=60):
        if not self.stream_id:
            self.login()
        deadline = time.time() + timeout
        while True:
            left = deadline - time.time()
            if left <= 0:
                raise ClientError("no phone")
            kind, data = self.next_event(left)
            if kind == "eof":
                raise ClientError("stream closed")
            if kind == "server" and data.get("type") == "pair":
                return self.accept_phone(data)

    def accept_phone(self, signal):
        text = transcript(self.pair, self.keys.sign_public, self.keys.agree_public, signal["phone_sign"], signal["phone_agree"])
        if not hmac.compare_digest(claim_tag(self.secret, text), b64u_decode(signal["tag"])):
            self.delete("pairing_rejected")
            raise ClientError("tag mismatch")
        self.peer_sign = signal["phone_sign"]
        self.peer_agree = signal["phone_agree"]
        self.text = text
        self.dir_keys = derive_keys(self.keys.agree, self.peer_agree, text)
        self.secret = None
        return fingerprint(text)

    def state_dump(self):
        return {
            "side": "pc",
            "pair": self.pair,
            "keys": self.keys.dump(),
            "peer_sign": self.peer_sign,
            "peer_agree": self.peer_agree,
        }

    @staticmethod
    def restore(base_url, dump):
        pc = Pc(base_url)
        pc.keys = KeyPair.load(dump["keys"])
        pc.pair = dump["pair"]
        pc.peer_sign, pc.peer_agree = dump["peer_sign"], dump["peer_agree"]
        pc.text = transcript(pc.pair, pc.keys.sign_public, pc.keys.agree_public, pc.peer_sign, pc.peer_agree)
        pc.dir_keys = derive_keys(pc.keys.agree, pc.peer_agree, pc.text)
        return pc


class Phone(Peer):
    def __init__(self, base_url=None):
        super().__init__(base_url or "http://invalid", "phone")

    @staticmethod
    def parse_qr(text, allow_http=True):
        qr = strict_json(text)
        if not isinstance(qr, dict) or set(qr) != {"v", "s", "c", "k", "ps", "pa", "n"}:
            raise ClientError("qr fields")
        if not isinstance(qr["v"], list) or not qr["v"] or not all(isinstance(v, int) and not isinstance(v, bool) and v >= 1 for v in qr["v"]):
            raise ClientError("qr v")
        if not isinstance(qr["s"], str) or qr["s"].endswith("/"):
            raise ClientError("qr s")
        if not (qr["s"].startswith("https://") or (allow_http and qr["s"].startswith("http://"))):
            raise ClientError("qr s scheme")
        if not isinstance(qr["c"], str) or not TOKEN_RE.fullmatch(qr["c"]):
            raise ClientError("qr c")
        if len(b64u_decode(qr["k"])) != 32:
            raise ClientError("qr k")
        load_public(qr["ps"])
        load_public(qr["pa"])
        n = qr["n"]
        if not isinstance(n, int) or isinstance(n, bool) or not 900 <= n <= 129600:
            raise ClientError("qr n")
        return qr

    def scan(self, qr_text, allow_http=True):
        qr = self.parse_qr(qr_text, allow_http)
        if 2 not in qr["v"]:
            raise ClientError("pc version is incompatible")
        self.base_url = qr["s"]
        self.http = Http(self.base_url)
        if 2 not in self.info().get("protocol", []):
            raise ClientError("server is incompatible")
        self.keys = KeyPair()
        self.pair = qr["c"]
        self.peer_sign, self.peer_agree = qr["ps"], qr["pa"]
        self.text = transcript(self.pair, self.peer_sign, self.peer_agree, self.keys.sign_public, self.keys.agree_public)
        tag = claim_tag(b64u_decode(qr["k"]), self.text)
        status, body = self.http.request(
            "POST",
            f"/v2/claims/{self.pair}",
            {"phone_sign": self.keys.sign_public, "phone_agree": self.keys.agree_public, "tag": b64u(tag)},
        )
        if status != 200:
            raise ClientError(f"claim {status} {body}")
        self.dir_keys = derive_keys(self.keys.agree, self.peer_agree, self.text)
        self.login()
        return fingerprint(self.text), qr["n"]

    def await_hello(self, timeout=120):
        deadline = time.time() + timeout
        while True:
            left = deadline - time.time()
            if left <= 0:
                self.delete("no_hello")
                raise ClientError("no hello")
            kind, body = self.receive(left)
            if kind == "message" and body.get("type") == "hello":
                return body
            if kind == "eof":
                raise ClientError("stream closed")

    def state_dump(self):
        return {
            "side": "phone",
            "base_url": self.base_url,
            "pair": self.pair,
            "keys": self.keys.dump(),
            "peer_sign": self.peer_sign,
            "peer_agree": self.peer_agree,
        }

    @staticmethod
    def restore(dump):
        phone = Phone(dump["base_url"])
        phone.keys = KeyPair.load(dump["keys"])
        phone.pair = dump["pair"]
        phone.peer_sign, phone.peer_agree = dump["peer_sign"], dump["peer_agree"]
        phone.text = transcript(phone.pair, phone.peer_sign, phone.peer_agree, phone.keys.sign_public, phone.keys.agree_public)
        phone.dir_keys = derive_keys(phone.keys.agree, phone.peer_agree, phone.text)
        return phone
