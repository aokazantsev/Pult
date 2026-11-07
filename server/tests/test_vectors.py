import tempfile
import unittest

from pult_relay.auth import ReplayWindow, login_string, post_string, verify
from pult_relay.codec import Invalid, JsonError, b64u, b64u_decode, strict_json
from pult_relay.config import Config
from pult_relay.core import Core, RelayError
from pult_relay.store import Store
from tests.helpers import vector
from tools import pult_testclient as client




def make_core():
    tmp = tempfile.TemporaryDirectory()
    return Core(Store(tmp.name), Config(data_dir=tmp.name)), tmp


class AuthVectorsTest(unittest.TestCase):
    def setUp(self):
        self.vec = vector("auth")

    def test_login_strings_and_signatures(self):
        for case in self.vec["login"]:
            string = login_string(self.vec["pair"], case["from"], case["challenge"])
            self.assertEqual(string.decode(), case["string"])
            self.assertTrue(verify(case["public"], string, case["sig"]))

    def test_post_strings_and_signatures(self):
        for case in self.vec["post"]:
            body = case["body"].encode("utf-8")
            self.assertEqual(b64u(__import__("hashlib").sha256(body).digest()), case["body_sha256"])
            string = post_string(case["op"], self.vec["pair"], case["from"], case["stream"], str(case["n"]), body)
            self.assertEqual(string.decode(), case["string"])
            self.assertTrue(verify(case["public"], string, case["sig"]))

    def test_verify_cases(self):
        for case in self.vec["verify"]:
            self.assertEqual(verify(case["public"], case["string"].encode("utf-8"), case["sig"]), case["valid"], case["case"])

    def test_replay_window(self):
        window = ReplayWindow(self.vec["replay_window"]["size"])
        for step in self.vec["replay_window"]["sequence"]:
            self.assertEqual(window.accept(step["n"]), step["accept"], step)


class EnvelopeVectorsTest(unittest.TestCase):
    def setUp(self):
        self.vec = vector("envelope")
        self.core, self.tmp = make_core()
        self.addCleanup(self.tmp.cleanup)

    def validate(self, env):
        side = env["from"] if isinstance(env, dict) and env.get("from") in ("pc", "phone") else "pc"
        self.core._validate_envelope(self.vec["pair"], side, env)

    def test_valid_envelopes_pass(self):
        for case in self.vec["valid"]:
            self.core._validate_envelope(self.vec["pair"], case["from"], case["envelope"])

    def test_server_visible_rejects(self):
        for case in self.vec["reject"]:
            env = case["envelope"]
            if case["server"]:
                with self.assertRaises(RelayError, msg=case["case"]) as ctx:
                    self.validate(env)
                self.assertEqual(ctx.exception.status, 400, case["case"])
            elif env["from"] in ("pc", "phone") and env["pair"] == self.vec["pair"]:
                self.core._validate_envelope(self.vec["pair"], env["from"], env)

    def test_text_rejects(self):
        for case in self.vec["reject_text"]:
            with self.assertRaises(JsonError, msg=case["case"]):
                strict_json(case["text"])

    def test_foreign_pair_is_not_this_pairs_envelope(self):
        case = next(c for c in self.vec["reject"] if c["case"] == "foreign pair")
        with self.assertRaises(RelayError):
            self.core._validate_envelope(self.vec["pair"], case["envelope"]["from"], case["envelope"])


class ClientVectorsTest(unittest.TestCase):
    def test_pairing_derivation(self):
        keys = vector("keys")["keys"]
        vec = vector("pairing")
        own = client.ec.derive_private_key(int(keys["pc_agree"]["d"], 16), client.ec.SECP256R1())
        text = vec["transcript"]
        self.assertEqual(client.claim_tag(b64u_decode(vec["secret"]), text), b64u_decode(vec["tag"]))
        self.assertEqual(client.fingerprint(text).replace(" ", ""), vec["fingerprint"].replace(" ", ""))
        derived = client.derive_keys(own, keys["phone_agree"]["public"], text)
        self.assertEqual(derived["pc"].hex(), vec["key_pc_to_phone_hex"])
        self.assertEqual(derived["phone"].hex(), vec["key_phone_to_pc_hex"])

    def test_envelope_vectors(self):
        vec = vector("envelope")
        keys = {"pc": bytes.fromhex(vec["key_pc_to_phone_hex"]), "phone": bytes.fromhex(vec["key_phone_to_pc_hex"])}
        for case in vec["valid"]:
            env = case["envelope"]
            sealed = client.seal(
                bytes.fromhex(case["key_hex"]), env["pair"], env["from"], env["id"], env["ttl"],
                client.strict_json(case["plaintext"]), ts=env["ts"], nonce=bytes.fromhex(case["nonce_hex"]),
            )
            self.assertEqual(client.envelope_aad(env).decode(), case["aad"])
            opened = client.open_envelope(keys, env, vec["pair"], case["to"])
            self.assertEqual(opened, client.strict_json(case["plaintext"]))
            self.assertEqual(sealed, env)
        for case in vec["reject"]:
            with self.assertRaises(client.ClientError, msg=case["case"]):
                client.open_envelope(keys, case["envelope"], vec["pair"], case["to"])


class CodecTest(unittest.TestCase):
    def test_b64u_rejects_noncanonical(self):
        for text in ["AB==", "A+B", "A/B", "A", "AAAAA", "AB"]:
            with self.assertRaises(Invalid, msg=text):
                b64u_decode(text)
        self.assertEqual(b64u_decode("AAAA"), b"\x00\x00\x00")


if __name__ == "__main__":
    unittest.main()
