import socket
import threading
import time
import unittest

from tests.helpers import drain, wait_until
from tests.test_flow import RelayCase

TOTAL_BYTES = 1048576
BODY_BYTES = 40000


class GatedReader:
    def __init__(self, fp, gate):
        self.fp = fp
        self.gate = gate

    def readline(self, *args):
        self.gate.wait()
        return self.fp.readline(*args)

    def __getattr__(self, name):
        return getattr(self.fp, name)


class StreamVolumeTest(RelayCase):
    clock_fake = False

    def test_one_mebibyte_of_live_messages_arrives_in_order_without_loss(self):
        pc, phone = self.relay.paired()
        stream = self.relay.core.streams[(phone.pair, "phone")]
        gate = threading.Event()
        phone.sse.sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4096)
        phone.sse.response.fp = GatedReader(phone.sse.response.fp, gate)
        count = TOTAL_BYTES // BODY_BYTES + 1
        started = time.time()
        for i in range(count):
            status, _, _ = pc.send({"type": "t", "n": i, "pad": "x" * BODY_BYTES}, ttl=0)
            self.assertEqual(status, 202)
        self.assertLess(time.time() - started, self.relay.config.liveness_sec)
        self.assertGreaterEqual(count * BODY_BYTES, TOTAL_BYTES)
        self.assertGreater(stream.backlog, 0)
        gate.set()
        received = []
        while len(received) < count:
            kind, body = phone.receive(5)
            self.assertEqual(kind, "message")
            received.append(body["n"])
        self.assertEqual(received, list(range(count)))
        self.assertEqual(self.relay.core.streams[(phone.pair, "phone")].closed, False)
        wait_until(lambda: self.relay.core.streams[(phone.pair, "phone")].backlog == 0)
        self.assertEqual(self.relay.core.store.pairs[pc.pair]["queue"], [])


class PcLoginOrderTest(RelayCase):
    def test_phone_queue_arrives_before_phone_stream_signal(self):
        pc, phone = self.relay.paired()
        pc.close()
        wait_until(lambda: (pc.pair, "pc") not in self.relay.core.streams)
        for i in range(3):
            self.assertEqual(phone.send({"type": "t", "n": i}, ttl=600)[0], 202)
        pc.login()
        events = drain(pc, 0.3)
        kinds = [data["type"] if kind == "server" else kind for kind, data in events]
        self.assertEqual(kinds, ["pair", "message", "message", "message", "phone_stream"])
        self.assertEqual(events[-1][1]["state"], "open")

    def test_signal_is_last_when_phone_stream_is_closed(self):
        pc, phone = self.relay.paired()
        pc.close()
        wait_until(lambda: (pc.pair, "pc") not in self.relay.core.streams)
        phone.send({"type": "t"}, ttl=600)
        phone.close()
        wait_until(lambda: (pc.pair, "phone") not in self.relay.core.streams)
        pc.login()
        events = drain(pc, 0.3)
        kinds = [data["type"] if kind == "server" else kind for kind, data in events]
        self.assertEqual(kinds, ["pair", "message", "phone_stream"])
        self.assertEqual(events[-1][1]["state"], "closed")


if __name__ == "__main__":
    unittest.main()
