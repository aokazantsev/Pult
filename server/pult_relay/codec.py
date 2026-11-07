import base64
import json
import re

B64U_RE = re.compile(r"[A-Za-z0-9_-]*")
TOKEN_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")
COUNTER_RE = re.compile(r"[1-9][0-9]*")
MAX_SAFE_INT = 2**53 - 1


class Invalid(ValueError):
    pass


class JsonError(Invalid):
    def __init__(self, kind):
        super().__init__(kind)
        self.kind = kind


def b64u(raw):
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode("ascii")


def b64u_decode(text):
    if not isinstance(text, str) or not B64U_RE.fullmatch(text) or len(text) % 4 == 1:
        raise Invalid("base64url")
    raw = base64.urlsafe_b64decode(text + "=" * (-len(text) % 4))
    if b64u(raw) != text:
        raise Invalid("base64url")
    return raw


def strict_json(raw):
    def pairs(items):
        keys = [k for k, _ in items]
        if len(keys) != len(set(keys)):
            raise JsonError("duplicate")
        return dict(items)

    def constant(name):
        raise JsonError("constant")

    try:
        text = raw.decode("utf-8") if isinstance(raw, (bytes, bytearray)) else raw
        return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)
    except JsonError:
        raise
    except (ValueError, RecursionError) as e:
        raise JsonError("syntax") from e


def is_token(value):
    return isinstance(value, str) and TOKEN_RE.fullmatch(value) is not None


def is_uint(value):
    return isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= MAX_SAFE_INT
