import http.client
import json
import os
import socket
import subprocess
import sys
import tempfile
import time
import unittest

from pult_relay.app import create_server, serve_in_thread
from pult_relay.config import Config
from pult_relay.core import Core
from pult_relay.store import Store
from tests.helpers import SseClient, envelope, request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class HttpTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.config = Config(data_dir=self.tmp.name, port=0, keepalive_sec=0.2)
        self.store = Store(self.tmp.name)
        self.pair, self.tokens = self.store.add_pair("p1")
        self.core = Core(self.store, self.config)
        self.server = create_server(self.core)
        self.port = self.server.server_address[1]
        serve_in_thread(self.server)
        self.clients = []

    def tearDown(self):
        for c in self.clients:
            c.close()
        self.server.shutdown()
        self.server.server_close()

    def sse(self, side):
        c = SseClient(self.port, "p1", self.tokens[side])
        self.clients.append(c)
        return c

    def send(self, side, mid, ttl=60):
        return request(self.port, "POST", "/v1/pairs/p1/messages", self.tokens[side], envelope("p1", side, mid, ttl))

    def test_info(self):
        status, body = request(self.port, "GET", "/info")
        self.assertEqual((status, body["protocol"]), (200, [1]))

    def test_non_ascii_token_is_401(self):
        self.assertEqual(request(self.port, "GET", "/v1/pairs/p1/state", "café")[0], 401)
        self.assertEqual(request(self.port, "POST", "/v1/pairs/p1/messages", "café", {})[0], 401)

    def chunked(self, payload, token=None, split=7):
        raw = payload if isinstance(payload, bytes) else json.dumps(payload).encode("utf-8")
        parts = [raw[i:i + split] for i in range(0, len(raw), split)]
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=10)
        conn.request(
            "POST",
            "/v1/pairs/p1/messages",
            body=iter(parts),
            headers={"Authorization": f"Bearer {token or self.tokens['pc']}", "Transfer-Encoding": "chunked"},
            encode_chunked=True,
        )
        resp = conn.getresponse()
        data = resp.read()
        conn.close()
        return resp.status, json.loads(data)

    def test_chunked_post_accepted(self):
        status, body = self.chunked(envelope("p1", "pc", "ch-1"))
        self.assertEqual((status, body["dup"]), (202, False))
        self.assertEqual(request(self.port, "GET", "/v1/pairs/p1/state", self.tokens["pc"])[1]["queued_to_phone"], 1)

    def test_chunked_post_over_limit_is_413(self):
        self.core.config = Config(data_dir=self.tmp.name, port=0, max_envelope_bytes=200)
        auth = f"Authorization: Bearer {self.tokens['pc']}"
        data = self.raw_request(
            f"POST /v1/pairs/p1/messages HTTP/1.1\r\nHost: x\r\n{auth}\r\nTransfer-Encoding: chunked\r\n\r\n1f4\r\n"
        )
        self.assertTrue(data.startswith(b"HTTP/1.1 413"))

    def raw_request(self, head):
        sock = socket.create_connection(("127.0.0.1", self.port), timeout=10)
        sock.sendall(head.encode("ascii"))
        data = sock.recv(4096)
        sock.close()
        return data

    def test_chunked_post_bad_chunk_is_400(self):
        auth = f"Authorization: Bearer {self.tokens['pc']}"
        data = self.raw_request(
            f"POST /v1/pairs/p1/messages HTTP/1.1\r\nHost: x\r\n{auth}\r\nTransfer-Encoding: chunked\r\n\r\nZZ\r\n"
        )
        self.assertTrue(data.startswith(b"HTTP/1.1 400"))

    def test_post_without_length_is_411(self):
        auth = f"Authorization: Bearer {self.tokens['pc']}"
        data = self.raw_request(f"POST /v1/pairs/p1/messages HTTP/1.1\r\nHost: x\r\n{auth}\r\n\r\n")
        self.assertTrue(data.startswith(b"HTTP/1.1 411"))

    def test_stream_requires_auth(self):
        self.assertEqual(request(self.port, "GET", "/v1/pairs/p1/state")[0], 401)
        self.assertEqual(request(self.port, "POST", "/v1/pairs/p1/messages", "bad", {})[0], 401)

    def test_roundtrip_pc_to_phone_and_back(self):
        pc = self.sse("pc")
        self.assertEqual(pc.next(), ("server", {"type": "phone_stream", "state": "closed"}))
        phone = self.sse("phone")
        self.assertEqual(pc.next(), ("server", {"type": "phone_stream", "state": "open"}))
        self.assertEqual(self.send("pc", "req1")[0], 202)
        kind, env = phone.next()
        self.assertEqual((kind, env["id"]), ("message", "req1"))
        self.assertEqual(request(self.port, "POST", "/v1/pairs/p1/acks", self.tokens["phone"], {"ids": ["req1"]})[1]["removed"], 1)
        self.assertEqual(self.send("phone", "resp1")[0], 202)
        kind, env = pc.next()
        self.assertEqual((kind, env["id"]), ("message", "resp1"))

    def test_phone_close_signals_pc(self):
        pc = self.sse("pc")
        pc.next()
        phone = self.sse("phone")
        pc.next()
        phone.close()
        self.assertEqual(pc.next(), ("server", {"type": "phone_stream", "state": "closed"}))

    def test_message_queued_while_phone_away_delivered_on_return(self):
        self.assertEqual(self.send("pc", "req1")[0], 202)
        phone = self.sse("phone")
        self.assertEqual(phone.next()[1]["id"], "req1")

    def test_duplicate_post_is_acknowledged(self):
        self.assertFalse(self.send("pc", "x")[1]["dup"])
        status, body = self.send("pc", "x")
        self.assertEqual((status, body["dup"]), (202, True))

    def test_bad_envelope_400(self):
        status, body = request(self.port, "POST", "/v1/pairs/p1/messages", self.tokens["pc"], {"v": 1})
        self.assertEqual((status, body["error"]), (400, "bad_envelope"))

    def test_new_stream_closes_previous(self):
        first = self.sse("phone")
        second = self.sse("phone")
        self.assertEqual(first.next()[0], "eof")
        self.send("pc", "m")
        self.assertEqual(second.next()[1]["id"], "m")

    def test_state_endpoint(self):
        self.send("pc", "q")
        status, body = request(self.port, "GET", "/v1/pairs/p1/state", self.tokens["pc"])
        self.assertEqual(body["queued_to_phone"], 1)
        self.assertEqual(body["phone_stream"], "closed")


class RestartTest(unittest.TestCase):
    def start(self, port, data):
        env = dict(os.environ, PULT_DATA=data, PULT_PORT=str(port), PULT_HOST="127.0.0.1")
        flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        proc = subprocess.Popen(
            [sys.executable, "-m", "pult_relay", "run"],
            cwd=ROOT, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=flags,
        )
        self.addCleanup(self.stop, proc)
        deadline = time.time() + 10
        while time.time() < deadline:
            try:
                if request(port, "GET", "/info")[0] == 200:
                    return proc
            except OSError:
                time.sleep(0.1)
        self.fail("server did not start")

    @staticmethod
    def stop(proc):
        if proc.poll() is None:
            proc.kill()
            proc.wait(timeout=10)

    def test_queue_survives_process_restart(self):
        with tempfile.TemporaryDirectory() as data:
            store = Store(data)
            _, tokens = store.add_pair("p1")
            port = free_port()
            proc = self.start(port, data)
            env = envelope("p1", "pc", "req-1", ttl=600)
            self.assertEqual(request(port, "POST", "/v1/pairs/p1/messages", tokens["pc"], env)[0], 202)
            proc.kill()
            proc.wait(timeout=10)
            proc = self.start(port, data)
            phone = SseClient(port, "p1", tokens["phone"])
            try:
                kind, got = phone.next()
                self.assertEqual((kind, got["id"]), ("message", "req-1"))
            finally:
                phone.close()
            status, body = request(port, "POST", "/v1/pairs/p1/messages", tokens["pc"], env)
            self.assertEqual(body["dup"], True)
            deadline = time.time() + 5
            while request(port, "GET", "/v1/pairs/p1/state", tokens["pc"])[1]["phone_stream"] != "closed":
                self.assertLess(time.time(), deadline)
                time.sleep(0.1)
            pc = SseClient(port, "p1", tokens["pc"])
            try:
                self.assertEqual(pc.next(), ("server", {"type": "phone_stream", "state": "closed"}))
            finally:
                pc.close()


if __name__ == "__main__":
    unittest.main()
