import os
import threading
import unittest
from unittest import mock

from tests.test_flow import RelayCase
from tests.helpers import wait_until
from tools import pult_cli, pult_testclient
from tools.pult_testclient import ClientError, Phone


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


if __name__ == "__main__":
    unittest.main()
