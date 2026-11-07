import io
import os
import shutil
import tempfile
import threading
import unittest
from unittest import mock

from tests.test_flow import RelayCase
from pult_relay.config import Config
from pult_relay.core import Core
from pult_relay.store import Store
from tests.helpers import wait_server, wait_until
from tools import pult_cli, pult_testclient
from tools.pult_testclient import ClientError, KeyPair, Phone


class ToolsTest(RelayCase):
    clock_fake = False

    def test_pc_pings_while_waiting_for_the_phone(self):
        pc, qr = self.relay.pending()
        pings = []
        real = pc.ping
        pc.ping = lambda: pings.append(1) or real()
        with mock.patch.object(pult_testclient, "PING_INTERVAL", 0.2):
            with self.assertRaises(ClientError):
                pc.await_phone(0.9)
        self.assertGreaterEqual(len(pings), 3)

    def test_phone_pings_while_waiting_for_hello(self):
        pc, qr = self.relay.pending()
        phone = self.relay.phone()
        phone.scan(qr)
        pings = []
        real = phone.ping
        phone.ping = lambda: pings.append(1) or real()
        with mock.patch.object(pult_testclient, "PING_INTERVAL", 0.2):
            with self.assertRaises(ClientError):
                phone.await_hello(0.9)
        self.assertGreaterEqual(len(pings), 3)

    def test_stream_read_timeout_exceeds_keepalive(self):
        pc, phone = self.relay.paired()
        self.assertGreater(pult_testclient.STREAM_READ_TIMEOUT, 15 * 3)
        self.assertEqual(phone.sse.sock.gettimeout(), pult_testclient.STREAM_READ_TIMEOUT)

    def test_undecryptable_envelope_is_acked_and_dropped(self):
        pc, phone = self.relay.paired()
        phone.close()
        good = pc.dir_keys["pc"]
        pc.dir_keys["pc"] = os.urandom(32)
        status, data, env = pc.send({"type": "text_request", "request_id": "x"})
        pc.dir_keys["pc"] = good
        self.assertEqual(status, 202)
        self.assertEqual(pc.state()[1]["queued_to_phone"], 1)
        phone.login()
        kind, body = phone.receive(5)
        self.assertEqual(kind, "reject")
        wait_until(lambda: pc.state()[1]["queued_to_phone"] == 0)

    def test_hello_is_queued_for_120_seconds(self):
        pc, phone = self.relay.paired()
        phone.close()
        status, data, env = pc.hello()
        self.assertEqual(env["ttl"], 120)
        self.assertEqual(pc.state()[1]["queued_to_phone"], 1)

    def test_listen_reconnects_after_the_stream_is_closed(self):
        pc, phone = self.relay.paired()
        first = phone.stream_id
        thread = threading.Thread(target=pult_cli.listen, args=(phone, False, 4, 0.05), daemon=True)
        thread.start()
        rival = Phone.restore(phone.state_dump())
        self.relay.peers.append(rival)
        rival.login()
        wait_until(lambda: phone.stream_id not in (None, first), 5)
        status, data, env = pc.send({"type": "text_request", "request_id": "after-reconnect"})
        self.assertEqual(status, 202)
        thread.join(8)
        self.assertFalse(thread.is_alive())



def failing_disk():
    return mock.patch("pult_relay.store._atomic_write", side_effect=OSError("disk full"))


class FailedWriteTest(RelayCase):
    clock_fake = False

    def test_failed_state_write_answers_500(self):
        pc = self.relay.pc()
        keys = KeyPair()
        claim = {"pc_sign": keys.sign_public, "pc_agree": keys.agree_public}
        log = io.StringIO()
        with failing_disk(), mock.patch("sys.stderr", log):
            status, body = pc.http.request("POST", "/v2/claims", claim)
        self.assertEqual((status, body), (500, {"error": "internal"}))
        self.assertIn("StoreError", log.getvalue())
        self.assertIn("disk full", log.getvalue())
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim)[0], 201)

    def test_failed_write_still_delivers_to_the_live_peer(self):
        pc, phone = self.relay.paired()
        with failing_disk(), mock.patch("sys.stderr"):
            status, data, env = pc.send({"type": "text_request", "request_id": "r1"})
        self.assertEqual(status, 500)
        self.assertEqual(pult_cli.await_message(phone, 5)["request_id"], "r1")

    def test_failed_write_on_scan_still_signals_the_pc(self):
        pc, qr = self.relay.pending()
        phone = self.relay.phone()
        with failing_disk(), mock.patch("sys.stderr"):
            with self.assertRaises(ClientError):
                phone.scan(qr)
        self.assertEqual(wait_server(pc, "pair")["type"], "pair")


class FailedReapTest(unittest.TestCase):
    def test_watchdog_keeps_reaping_when_the_write_fails(self):
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        core = Core(Store(tmp), Config(data_dir=tmp, watchdog_sec=0.02))
        core.store.pairs["p"] = {"status": "pending", "created": 0, "queue": [], "seen": [], "seq": 0}
        calls = []
        real = core.reap
        core.reap = lambda: calls.append(1) or real()
        with failing_disk(), mock.patch("sys.stderr"):
            core.start_watchdog()
            try:
                wait_until(lambda: "p" not in core.store.pairs and len(calls) >= 3)
            finally:
                core.stop()


class DemoTest(RelayCase):
    clock_fake = False

    def test_demo_runs_to_the_end(self):
        with mock.patch.object(pult_cli, "say") as said:
            self.assertEqual(pult_cli.main(["--server", self.relay.url, "demo"]), 0)
        got = {c.args[0]: c.args[1] for c in said.call_args_list if c.args[0] in ("phone got:", "pc got:")}
        self.assertEqual(got["phone got:"]["type"], "permission_request")
        self.assertEqual(got["pc got:"]["type"], "permission_response")


class AwaitMessageTest(RelayCase):
    def test_times_out_without_a_message(self):
        pc, phone = self.relay.paired()
        with self.assertRaisesRegex(ClientError, "no message"):
            pult_cli.await_message(phone, 0.5)

    def test_closed_stream_is_an_error(self):
        pc, phone = self.relay.paired()
        rival = Phone.restore(phone.state_dump())
        self.relay.peers.append(rival)
        rival.login()
        with self.assertRaisesRegex(ClientError, "stream closed"):
            pult_cli.await_message(phone, 5)

    def test_undecryptable_message_is_an_error(self):
        pc, phone = self.relay.paired()
        pc.dir_keys["pc"] = os.urandom(32)
        pc.send({"type": "text_request", "request_id": "x"})
        with self.assertRaises(ClientError) as failed:
            pult_cli.await_message(phone, 5)
        self.assertNotIn(str(failed.exception), ("no message", "stream closed"))

if __name__ == "__main__":
    unittest.main()
