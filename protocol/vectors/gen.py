import argparse
import base64
import hashlib
import hmac
import json
import re
import sys
from pathlib import Path

from cryptography.exceptions import InvalidSignature, InvalidTag
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature, encode_dss_signature
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

HERE = Path(__file__).resolve().parent
VERSION = 2
CURVE_N = 0xFFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551
TOKEN_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")
B64U_RE = re.compile(r"[A-Za-z0-9_-]*")
SIDES = ("pc", "phone")
OPS = ("messages", "acks", "ping", "delete", "state")
REPLAY_WINDOW = 128
MAX_SAFE_INT = 2**53 - 1


class Rejected(Exception):
    pass


def b64u(raw):
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode("ascii")


def b64u_decode(text):
    if not isinstance(text, str) or not B64U_RE.fullmatch(text) or len(text) % 4 == 1:
        raise Rejected("bad base64url")
    raw = base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))
    if b64u(raw) != text:
        raise Rejected("non-canonical base64url")
    return raw


def sha256(data):
    return hashlib.sha256(data).digest()


def hmac_sha256(key, data):
    return hmac.new(key, data, hashlib.sha256).digest()


def fixed_bytes(label, size=32):
    out = b""
    counter = 0
    while len(out) < size:
        out += sha256(f"pult-vectors {label} {counter}".encode())
        counter += 1
    return out[:size]


def fixed_scalar(label):
    return int.from_bytes(sha256(f"pult-vectors {label}".encode()), "big") % (CURVE_N - 1) + 1


def private_key(scalar):
    return ec.derive_private_key(scalar, ec.SECP256R1())


def public_raw(key):
    return key.public_key().public_bytes(serialization.Encoding.X962, serialization.PublicFormat.UncompressedPoint)


def parse_public(text):
    raw = b64u_decode(text)
    if len(raw) != 65 or raw[0] != 4:
        raise Rejected("public key is not an uncompressed P-256 point")
    try:
        return ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), raw)
    except ValueError as e:
        raise Rejected("public key is not on P-256") from e


def check_token(text, what):
    if not isinstance(text, str) or not TOKEN_RE.fullmatch(text):
        raise Rejected(f"bad {what}")
    return text


def check_int(value, what):
    if not isinstance(value, int) or isinstance(value, bool) or value < 0 or value > MAX_SAFE_INT:
        raise Rejected(f"bad {what}")
    return value


def der_to_p1363(der):
    r, s = decode_dss_signature(der)
    return r.to_bytes(32, "big") + s.to_bytes(32, "big")


def p1363_to_der(sig):
    return encode_dss_signature(int.from_bytes(sig[:32], "big"), int.from_bytes(sig[32:], "big"))


def sign(key, data):
    return der_to_p1363(key.sign(data, ec.ECDSA(hashes.SHA256(), deterministic_signing=True)))


def verify(public_text, data, sig_text):
    try:
        public = parse_public(public_text)
        sig = b64u_decode(sig_text)
    except Rejected:
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


def claim_transcript(pair, pc_sign, pc_agree, phone_sign, phone_agree):
    return "\n".join([pair, pc_sign, pc_agree, phone_sign, phone_agree])


def claim_tag(secret, transcript):
    return hmac_sha256(secret, ("pult-v2-claim\n" + transcript).encode())


def pair_salt(transcript):
    return sha256(("pult-v2-salt\n" + transcript).encode())


def fingerprint(transcript):
    digest = sha256(("pult-v2-fingerprint\n" + transcript).encode())
    digits = f"{int.from_bytes(digest[:8], 'big') % 10**16:016d}"
    return " ".join(digits[i:i + 4] for i in range(0, 16, 4))


def hkdf_extract(salt, ikm):
    return hmac_sha256(salt, ikm)


def hkdf_expand_32(prk, info):
    return hmac_sha256(prk, info.encode() + b"\x01")


def direction_info(sender):
    return "pult-v2-key-pc-phone" if sender == "pc" else "pult-v2-key-phone-pc"


def pair_keys(own_agree, peer_agree_text, transcript):
    shared = own_agree.exchange(ec.ECDH(), parse_public(peer_agree_text))
    prk = hkdf_extract(pair_salt(transcript), shared)
    return shared, prk, {side: hkdf_expand_32(prk, direction_info(side)) for side in SIDES}


def envelope_aad(env):
    return "\n".join(["pult-v2-env", str(env["v"]), env["pair"], env["from"], env["id"], str(env["ts"]), str(env["ttl"])]).encode()


def validate_envelope(env):
    if not isinstance(env, dict):
        raise Rejected("envelope is not an object")
    if set(env) != {"v", "pair", "from", "id", "ts", "ttl", "ct"}:
        raise Rejected("envelope fields")
    if check_int(env["v"], "v") != VERSION:
        raise Rejected("unsupported version")
    check_token(env["pair"], "pair")
    if env["from"] not in SIDES:
        raise Rejected("bad from")
    check_token(env["id"], "id")
    check_int(env["ts"], "ts")
    check_int(env["ttl"], "ttl")
    if len(b64u_decode(env["ct"])) < 12 + 16:
        raise Rejected("ct too short")


def seal(key, env_open, plaintext, nonce):
    env = dict(env_open)
    env["ct"] = b64u(nonce + AESGCM(key).encrypt(nonce, plaintext, envelope_aad(env)))
    return env


def open_envelope(keys, env, expected_pair, receiver):
    validate_envelope(env)
    if env["pair"] != expected_pair:
        raise Rejected("foreign pair")
    if env["from"] == receiver:
        raise Rejected("own direction reflected")
    raw = b64u_decode(env["ct"])
    try:
        return AESGCM(keys[env["from"]]).decrypt(raw[:12], raw[12:], envelope_aad(env))
    except InvalidTag as e:
        raise Rejected("authentication failed") from e


def login_string(pair, side, challenge):
    return "\n".join(["pult-v2-login", pair, side, challenge])


def post_string(op, pair, side, stream, n, body):
    return "\n".join(["pult-v2-post", op, pair, side, stream, str(n), b64u(sha256(body))])


class ReplayWindow:
    def __init__(self):
        self.top = 0
        self.seen = set()

    def accept(self, n):
        if not isinstance(n, int) or isinstance(n, bool) or n < 1 or n > MAX_SAFE_INT:
            return False
        if n > self.top:
            self.top = n
            self.seen = {m for m in self.seen if m > n - REPLAY_WINDOW}
            self.seen.add(n)
            return True
        if n <= self.top - REPLAY_WINDOW or n in self.seen:
            return False
        self.seen.add(n)
        return True


def non_canonical_twin(text):
    raw = base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))
    alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    for c in alphabet:
        twin = text[:-1] + c
        if twin != text and base64.urlsafe_b64decode(twin + "=" * (-len(twin) % 4)) == raw:
            return twin
    raise ValueError("no spare bits in the last character")


def strict_json(text):
    def pairs(items):
        keys = [k for k, _ in items]
        if len(keys) != len(set(keys)):
            raise Rejected("duplicate JSON key")
        return dict(items)

    def constant(name):
        raise Rejected(f"JSON constant {name}")

    try:
        return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)
    except ValueError as e:
        raise Rejected("bad JSON") from e


def parse_qr(text):
    qr = strict_json(text)
    if not isinstance(qr, dict) or set(qr) != {"v", "s", "c", "k", "ps", "pa", "n"}:
        raise Rejected("qr fields")
    if not isinstance(qr["v"], list) or not qr["v"] or any(check_int(v, "qr v") < 1 for v in qr["v"]):
        raise Rejected("qr versions")
    if not isinstance(qr["s"], str) or not qr["s"].startswith("https://") or qr["s"].endswith("/"):
        raise Rejected("qr server")
    check_token(qr["c"], "qr pair")
    if len(b64u_decode(qr["k"])) != 32:
        raise Rejected("qr secret")
    parse_public(qr["ps"])
    parse_public(qr["pa"])
    if not 900 <= check_int(qr["n"], "qr idle") <= 129600:
        raise Rejected("qr idle")
    return qr


def other_side(side):
    return "phone" if side == "pc" else "pc"


def dumps_compact(obj):
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def build():
    labels = ["pc_sign", "pc_agree", "phone_sign", "phone_agree"]
    priv = {label: private_key(fixed_scalar(label)) for label in labels}
    pub = {label: b64u(public_raw(priv[label])) for label in labels}

    keys = {
        "description": "Fixed P-256 key pairs. d — private scalar, public — SEC1 uncompressed point (0x04||X||Y), base64url without padding.",
        "keys": {
            label: {
                "d": f"{priv[label].private_numbers().private_value:064x}",
                "x": f"{priv[label].public_key().public_numbers().x:064x}",
                "y": f"{priv[label].public_key().public_numbers().y:064x}",
                "public": pub[label],
            }
            for label in labels
        },
    }

    pair = b64u(fixed_bytes("pair", 16))
    secret = fixed_bytes("claim secret")
    server = "https://example.com/pult"
    idle = 14400
    qr = dumps_compact({"v": [VERSION], "s": server, "c": pair, "k": b64u(secret), "ps": pub["pc_sign"], "pa": pub["pc_agree"], "n": idle})
    transcript = claim_transcript(pair, pub["pc_sign"], pub["pc_agree"], pub["phone_sign"], pub["phone_agree"])
    shared_pc, prk_pc, keys_pc = pair_keys(priv["pc_agree"], pub["phone_agree"], transcript)
    shared_phone, prk_phone, keys_phone = pair_keys(priv["phone_agree"], pub["pc_agree"], transcript)
    assert shared_pc == shared_phone and prk_pc == prk_phone and keys_pc == keys_phone
    forged_transcript = claim_transcript(pair, pub["pc_sign"], pub["pc_agree"], pub["phone_sign"], pub["pc_agree"])

    pairing = {
        "description": "Pairing: QR payload, claim transcript, attestation tag, fingerprint, ECDH and HKDF.",
        "pair": pair,
        "secret": b64u(secret),
        "server": server,
        "idle_seconds": idle,
        "qr": qr,
        "pc_sign": pub["pc_sign"],
        "pc_agree": pub["pc_agree"],
        "phone_sign": pub["phone_sign"],
        "phone_agree": pub["phone_agree"],
        "transcript": transcript,
        "tag": b64u(claim_tag(secret, transcript)),
        "fingerprint": fingerprint(transcript),
        "shared_secret_hex": shared_pc.hex(),
        "salt_hex": pair_salt(transcript).hex(),
        "prk_hex": prk_pc.hex(),
        "key_pc_to_phone_hex": keys_pc["pc"].hex(),
        "key_phone_to_pc_hex": keys_pc["phone"].hex(),
        "qr_reject": [
            {"case": case, "qr": text}
            for case, text in [
                ("duplicate k", qr.replace('"k":', '"k":"' + b64u(fixed_bytes("other secret")) + '","k":', 1)),
                ("idle below 15 minutes", qr.replace('"n":14400', '"n":60')),
                ("missing pa", qr.replace(',"pa":"' + pub["pc_agree"] + '"', "")),
                ("plain http server", qr.replace("https://", "http://")),
                ("extra key", qr[:-1] + ',"x":1}'),
                ("secret of 16 bytes", qr.replace(b64u(secret), b64u(secret[:16]))),
            ]
        ],
        "reject": [
            {
                "case": "phone_agree replaced by the server",
                "transcript": forged_transcript,
                "tag": b64u(claim_tag(secret, transcript)),
            },
            {
                "case": "tag computed with a wrong secret",
                "transcript": transcript,
                "tag": b64u(claim_tag(fixed_bytes("wrong secret"), transcript)),
            },
        ],
    }

    cases = [
        ("pc", "pc-0001", 1793989146, 600, {"type": "permission_request", "request_id": "r1", "thread": "t1", "title": "Pult", "tool": "Bash", "summary": "mkdir t1", "input": {"command": "mkdir t1"}}),
        ("phone", "ph-0001", 1793989150, 600, {"type": "permission_response", "request_id": "r1", "decision": "allow", "reason": ""}),
        ("pc", "pc-0002", 1793989151, 0, {"type": "status", "thread": "t1", "status": "working", "title": "Тред"}),
        ("phone", "ph-0002", 1793989152, 120, {"type": "hello", "protocol": [VERSION], "capabilities": []}),
    ]
    sealed = []
    for side, mid, ts, ttl, inner in cases:
        env_open = {"v": VERSION, "pair": pair, "from": side, "id": mid, "ts": ts, "ttl": ttl}
        plaintext = dumps_compact(inner).encode()
        nonce = fixed_bytes(f"nonce {mid}", 12)
        env = seal(keys_pc[side], env_open, plaintext, nonce)
        sealed.append({
            "from": side,
            "to": other_side(side),
            "key_hex": keys_pc[side].hex(),
            "nonce_hex": nonce.hex(),
            "plaintext": plaintext.decode(),
            "aad": envelope_aad(env).decode(),
            "envelope": env,
        })

    base = sealed[0]["envelope"]
    raw_ct = bytearray(b64u_decode(base["ct"]))
    raw_ct[20] ^= 1
    non_canonical = non_canonical_twin(base["ct"])
    reject = [
        ("ttl raised by the server", {**base, "ttl": 900}),
        ("ts changed", {**base, "ts": base["ts"] + 1}),
        ("id changed", {**base, "id": "pc-0009"}),
        ("from flipped", {**base, "from": "phone"}),
        ("own direction reflected back to the sender", base),
        ("v is a float", {**base, "v": 2.0}),
        ("ciphertext bit flipped", {**base, "ct": b64u(bytes(raw_ct))}),
        ("foreign pair", {**base, "pair": b64u(fixed_bytes("other pair", 16))}),
        ("plaintext inner instead of ct", {k: v for k, v in base.items() if k != "ct"} | {"inner": {"type": "status"}}),
        ("extra field", {**base, "sig": "AA"}),
        ("ttl is a float", {**base, "ttl": 600.0}),
        ("version 1", {**base, "v": 1}),
        ("ct is not canonical base64url", {**base, "ct": non_canonical}),
        ("ct with = padding", {**base, "ct": base["ct"] + "=" * (-len(base["ct"]) % 4 or 4)}),
        ("ct in standard base64 alphabet", {**base, "ct": base["ct"].replace("-", "+").replace("_", "/")}),
        ("id with a newline", {**base, "id": "pc-0001\n"}),
    ]
    receivers = {"from flipped": "pc", "own direction reflected back to the sender": "pc"}
    reject = [(case, env, receivers.get(case, "phone")) for case, env in reject if case in receivers or dumps_compact(env) != dumps_compact(base)]
    server_visible = {
        "plaintext inner instead of ct", "extra field", "ttl is a float", "version 1", "v is a float",
        "ct is not canonical base64url", "ct with = padding", "ct in standard base64 alphabet", "id with a newline",
    }
    base_text = dumps_compact(base)
    reject_text = [
        ("duplicate ttl, last value is the original", "phone", base_text.replace('"ttl":600,', '"ttl":900,"ttl":600,')),
        ("duplicate from, last value is the original", "phone", base_text.replace('"from":"pc",', '"from":"phone","from":"pc",')),
        ("NaN in ts", "phone", base_text.replace('"ts":1793989146,', '"ts":NaN,')),
    ]
    envelope = {
        "description": "Envelope v2: AES-256-GCM, ct = base64url(nonce12 || ciphertext || tag16), AAD = canonical open fields. Keys: key_pc_to_phone for from=pc, key_phone_to_pc for from=phone.",
        "pair": pair,
        "key_pc_to_phone_hex": keys_pc["pc"].hex(),
        "key_phone_to_pc_hex": keys_pc["phone"].hex(),
        "valid": sealed,
        "reject": [{"case": case, "to": to, "server": case in server_visible, "envelope": env} for case, env, to in reject],
        "reject_text": [{"case": case, "to": to, "server": True, "text": text} for case, to, text in reject_text],
    }

    challenge = b64u(fixed_bytes("challenge", 32))
    stream = b64u(fixed_bytes("stream", 16))
    logins = []
    for side in SIDES:
        text = login_string(pair, side, challenge)
        sig = sign(priv[f"{side}_sign"], text.encode())
        logins.append({"from": side, "public": pub[f"{side}_sign"], "challenge": challenge, "string": text, "sig": b64u(sig), "der_hex": p1363_to_der(sig).hex()})
    posts = []
    post_cases = [
        ("messages", "pc", 1, dumps_compact(sealed[0]["envelope"]).encode()),
        ("acks", "phone", 7, b'{"ids":["pc-0001"]}'),
        ("ping", "phone", 8, b""),
        ("delete", "pc", 2, b'{"reason":"user"}'),
    ]
    for op, side, n, body in post_cases:
        text = post_string(op, pair, side, stream, n, body)
        sig = sign(priv[f"{side}_sign"], text.encode())
        posts.append({"op": op, "from": side, "public": pub[f"{side}_sign"], "stream": stream, "n": n, "body": body.decode(), "body_sha256": b64u(sha256(body)), "string": text, "sig": b64u(sig), "der_hex": p1363_to_der(sig).hex()})

    pc_login = logins[0]
    sig_raw = b64u_decode(pc_login["sig"])
    high_s = sig_raw[:32] + (CURVE_N - int.from_bytes(sig_raw[32:], "big")).to_bytes(32, "big")
    zero_r = bytes(32) + sig_raw[32:]
    n_as_s = sig_raw[:32] + CURVE_N.to_bytes(32, "big")
    first_post = posts[0]
    verify_cases = [
        ("high-S form of a valid signature is accepted", pub["pc_sign"], pc_login["string"], b64u(high_s), True),
        ("login signature checked with the phone key", pub["phone_sign"], pc_login["string"], pc_login["sig"], False),
        ("login signature for a different challenge", pub["pc_sign"], login_string(pair, "pc", b64u(fixed_bytes("challenge 2", 32))), pc_login["sig"], False),
        ("r = 0", pub["pc_sign"], pc_login["string"], b64u(zero_r), False),
        ("s = n", pub["pc_sign"], pc_login["string"], b64u(n_as_s), False),
        ("63-byte signature", pub["pc_sign"], pc_login["string"], b64u(sig_raw[:63]), False),
        ("DER instead of P1363", pub["pc_sign"], pc_login["string"], b64u(p1363_to_der(sig_raw)), False),
        ("post signature with another op", pub["pc_sign"], post_string("acks", pair, "pc", stream, 1, first_post["body"].encode()), first_post["sig"], False),
        ("post signature with another n", pub["pc_sign"], post_string("messages", pair, "pc", stream, 2, first_post["body"].encode()), first_post["sig"], False),
        ("post signature with a changed body", pub["pc_sign"], post_string("messages", pair, "pc", stream, 1, first_post["body"].encode() + b" "), first_post["sig"], False),
        ("post signature with another stream", pub["pc_sign"], post_string("messages", pair, "pc", b64u(fixed_bytes("stream 2", 16)), 1, first_post["body"].encode()), first_post["sig"], False),
    ]
    window = ReplayWindow()
    replay_sequence = [1, 2, 2, 5, 3, 4, 3, 200, 72, 73, 73, 200, 199, 0, -1]
    replay = [{"n": n, "accept": window.accept(n)} for n in replay_sequence]

    auth = {
        "description": "Signatures: ECDSA P-256 SHA-256, P1363 r||s (64 bytes) in base64url. String is signed as UTF-8 bytes. der_hex — same signature in DER for platforms that produce DER.",
        "pair": pair,
        "login": logins,
        "post": posts,
        "verify": [{"case": case, "public": public, "string": text, "sig": sig, "valid": valid} for case, public, text, sig, valid in verify_cases],
        "replay_window": {"size": REPLAY_WINDOW, "sequence": replay},
    }

    return {"keys.json": keys, "pairing.json": pairing, "envelope.json": envelope, "auth.json": auth}


def check(files):
    failures = []

    def expect(cond, what):
        if not cond:
            failures.append(what)

    keys = files["keys.json"]["keys"]
    priv = {label: private_key(int(k["d"], 16)) for label, k in keys.items()}
    for label, k in keys.items():
        expect(b64u(public_raw(priv[label])) == k["public"], f"keys: public {label}")
        point = parse_public(k["public"]).public_numbers()
        expect(f"{point.x:064x}" == k["x"] and f"{point.y:064x}" == k["y"], f"keys: x/y {label}")

    p = files["pairing.json"]
    qr = parse_qr(p["qr"])
    for case in p["qr_reject"]:
        try:
            parse_qr(case["qr"])
            expect(False, f"pairing: qr accepted: {case['case']}")
        except Rejected:
            pass
    expect(qr == {"v": [VERSION], "s": p["server"], "c": p["pair"], "k": p["secret"], "ps": p["pc_sign"], "pa": p["pc_agree"], "n": p["idle_seconds"]}, "pairing: qr")
    transcript = claim_transcript(p["pair"], p["pc_sign"], p["pc_agree"], p["phone_sign"], p["phone_agree"])
    expect(transcript == p["transcript"], "pairing: transcript")
    secret = b64u_decode(p["secret"])
    expect(hmac.compare_digest(claim_tag(secret, transcript), b64u_decode(p["tag"])), "pairing: tag")
    for case in p["reject"]:
        expect(not hmac.compare_digest(claim_tag(secret, case["transcript"]), b64u_decode(case["tag"])), f"pairing: reject {case['case']}")
    expect(fingerprint(transcript) == p["fingerprint"], "pairing: fingerprint")
    shared_pc, prk_pc, keys_pc = pair_keys(priv["pc_agree"], p["phone_agree"], transcript)
    shared_phone, prk_phone, keys_phone = pair_keys(priv["phone_agree"], p["pc_agree"], transcript)
    expect(shared_pc == shared_phone and shared_pc.hex() == p["shared_secret_hex"], "pairing: shared secret")
    expect(pair_salt(transcript).hex() == p["salt_hex"], "pairing: salt")
    expect(prk_pc == prk_phone and prk_pc.hex() == p["prk_hex"], "pairing: prk")
    expect(keys_pc == keys_phone, "pairing: both sides derive the same keys")
    expect(keys_pc["pc"].hex() == p["key_pc_to_phone_hex"] and keys_pc["phone"].hex() == p["key_phone_to_pc_hex"], "pairing: direction keys")

    e = files["envelope.json"]
    expect(e["key_pc_to_phone_hex"] == keys_pc["pc"].hex() and e["key_phone_to_pc_hex"] == keys_pc["phone"].hex(), "envelope: keys")
    for case in e["valid"]:
        env = case["envelope"]
        expect(envelope_aad(env).decode() == case["aad"], f"envelope: aad {env['id']}")
        try:
            expect(case["to"] == other_side(env["from"]), f"envelope: to {env['id']}")
            plaintext = open_envelope(keys_pc, strict_json(dumps_compact(env)), e["pair"], case["to"])
            expect(plaintext.decode() == case["plaintext"], f"envelope: plaintext {env['id']}")
        except Rejected as err:
            expect(False, f"envelope: valid {env['id']} rejected: {err}")
        env_open = {k: v for k, v in env.items() if k != "ct"}
        resealed = seal(bytes.fromhex(case["key_hex"]), env_open, case["plaintext"].encode(), bytes.fromhex(case["nonce_hex"]))
        expect(resealed["ct"] == env["ct"], f"envelope: seal {env['id']}")
    for case in e["reject"]:
        try:
            open_envelope(keys_pc, case["envelope"], e["pair"], case["to"])
            expect(False, f"envelope: reject accepted: {case['case']}")
        except Rejected:
            pass
    for case in e["reject"] + e["reject_text"]:
        if case["server"]:
            try:
                validate_envelope(strict_json(case["text"]) if "text" in case else case["envelope"])
                expect(False, f"envelope: server accepts without key: {case['case']}")
            except Rejected:
                pass
    for case in e["reject_text"]:
        try:
            open_envelope(keys_pc, strict_json(case["text"]), e["pair"], case["to"])
            expect(False, f"envelope: reject_text accepted: {case['case']}")
        except Rejected:
            pass
        try:
            open_envelope(keys_pc, json.loads(case["text"]), e["pair"], case["to"])
            lenient = True
        except (Rejected, ValueError):
            lenient = False
        if case["case"].startswith("duplicate"):
            expect(lenient, f"envelope: reject_text {case['case']} must pass a lenient parser")

    a = files["auth.json"]
    for login in a["login"]:
        text = login_string(a["pair"], login["from"], login["challenge"])
        expect(text == login["string"], f"auth: login string {login['from']}")
        expect(verify(login["public"], text.encode(), login["sig"]), f"auth: login sig {login['from']}")
        expect(b64u(der_to_p1363(bytes.fromhex(login["der_hex"]))) == login["sig"], f"auth: login der {login['from']}")
        expect(b64u(sign(priv[f"{login['from']}_sign"], text.encode())) == login["sig"], f"auth: login deterministic {login['from']}")
    for post in a["post"]:
        if post["op"] not in OPS:
            expect(False, f"auth: unknown op {post['op']}")
        body = post["body"].encode()
        expect(b64u(sha256(body)) == post["body_sha256"], f"auth: body hash {post['op']}")
        text = post_string(post["op"], a["pair"], post["from"], post["stream"], post["n"], body)
        expect(text == post["string"], f"auth: post string {post['op']}")
        expect(verify(post["public"], text.encode(), post["sig"]), f"auth: post sig {post['op']}")
        expect(b64u(der_to_p1363(bytes.fromhex(post["der_hex"]))) == post["sig"], f"auth: post der {post['op']}")
    for case in a["verify"]:
        expect(verify(case["public"], case["string"].encode(), case["sig"]) == case["valid"], f"auth: verify {case['case']}")
    window = ReplayWindow()
    expect(a["replay_window"]["size"] == REPLAY_WINDOW, "auth: replay window size")
    for step in a["replay_window"]["sequence"]:
        expect(window.accept(step["n"]) == step["accept"], f"auth: replay n={step['n']}")

    return failures


def main():
    parser = argparse.ArgumentParser(description="Pult protocol v2 test vectors: check (default) or regenerate.")
    parser.add_argument("--write", action="store_true", help="regenerate vector files")
    args = parser.parse_args()
    built = build()
    if args.write:
        for name, data in built.items():
            (HERE / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print("written:", ", ".join(built))
    files = {name: json.loads((HERE / name).read_text(encoding="utf-8")) for name in built}
    failures = [f"{name}: differs from the generator" for name in built if files[name] != built[name]]
    failures += check(files)
    for failure in failures:
        print("FAIL", failure)
    if failures:
        sys.exit(1)
    print("ok: vectors match the generator and pass all checks")


if __name__ == "__main__":
    main()
