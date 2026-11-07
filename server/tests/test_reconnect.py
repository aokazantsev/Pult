import queue
import time
import unittest
from unittest import mock

from tests.test_flow import RelayCase
from tools import pult_cli, pult_testclient
from tools.pult_testclient import ClientError, LoginRefused, refusal


class RefusalTest(unittest.TestCase):
    def test_final_and_transient_statuses(self):
        for status in (401, 404, 410):
            self.assertIsInstance(refusal(status, {}), LoginRefused)
        for status in (429, 502, 503):
            error = refusal(status, {})
            self.assertIsInstance(error, ClientError)
            self.assertNotIsInstance(error, LoginRefused)


class ReconnectTest(RelayCase):
    clock_fake = False

    def test_reconnect_retries_after_a_transient_refusal(self):
        pc, phone = self.relay.paired()
        first = phone.stream_id
        real = phone.challenge
        calls = []

        def flaky():
            calls.append(1)
            if len(calls) == 1:
                raise refusal(502, None)
            return real()

        phone.challenge = flaky
        self.assertTrue(pult_cli.reconnect(phone, time.time() + 5, 0.01))
        self.assertNotEqual(phone.stream_id, first)
        self.assertEqual(len(calls), 2)

    def test_reconnect_gives_up_on_a_deleted_pair(self):
        pc, phone = self.relay.paired()
        self.assertEqual(pc.delete("user")[0], 200)
        started = time.time()
        self.assertFalse(pult_cli.reconnect(phone, time.time() + 10, 0.01))
        self.assertLess(time.time() - started, 5)

    def test_missing_session_event_is_a_transient_error(self):
        pc, phone = self.relay.paired()

        class Silent:
            status = 200
            error = None

            def __init__(self, *args):
                self.events = queue.Queue()

            def close(self):
                pass

        with mock.patch.object(pult_testclient, "Sse", Silent):
            with self.assertRaises(ClientError) as raised:
                phone.login(timeout=0.1)
        self.assertNotIsInstance(raised.exception, LoginRefused)


class AckFailureTest(RelayCase):
    clock_fake = False

    def test_message_whose_ack_failed_is_delivered_again(self):
        pc, phone = self.relay.paired()
        phone.close()
        self.assertEqual(pc.send({"type": "text_request", "request_id": "r"}, ttl=60)[0], 202)
        phone.login()
        real = phone.ack
        phone.ack = lambda ids: (_ for _ in ()).throw(OSError("network"))
        with self.assertRaises(OSError):
            phone.receive(5)
        phone.ack = real
        phone.login()
        kind, body = phone.receive(5)
        self.assertEqual((kind, body["request_id"]), ("message", "r"))


class PlaintextTest(RelayCase):
    clock_fake = False

    def test_non_object_plaintext_is_rejected_and_acked(self):
        from tests.helpers import wait_until
        pc, phone = self.relay.paired()
        phone.close()
        for body in ([1], "x"):
            self.assertEqual(pc.send(body, ttl=60)[0], 202)
        phone.login()
        kinds = [phone.receive(5)[0] for _ in range(2)]
        self.assertEqual(kinds, ["reject", "reject"])
        wait_until(lambda: pc.state()[1]["queued_to_phone"] == 0)

    def test_undecodable_plaintext_is_rejected_and_acked(self):
        import os
        from cryptography.hazmat.primitives.ciphers.aead import AESGCM
        from tests.helpers import wait_until
        from tools.pult_testclient import b64u, compact, envelope_aad
        pc, phone = self.relay.paired()
        phone.close()
        for i, plain in enumerate((bytes([255, 254]), b"not json")):
            env = {"v": 2, "pair": pc.pair, "from": "pc", "id": f"raw{i}", "ts": 0, "ttl": 60}
            nonce = os.urandom(12)
            env["ct"] = b64u(nonce + AESGCM(pc.dir_keys["pc"]).encrypt(nonce, plain, envelope_aad(env)))
            self.assertEqual(pc.signed("messages", compact(env).encode("utf-8"))[0], 202)
        phone.login()
        self.assertEqual([phone.receive(5)[0] for _ in range(2)], ["reject", "reject"])
        wait_until(lambda: pc.state()[1]["queued_to_phone"] == 0)


if __name__ == "__main__":
    unittest.main()
