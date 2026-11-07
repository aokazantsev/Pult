import json
import os
import queue
import dataclasses
import time

from pult_relay.app import create_server, serve_in_thread
from pult_relay.config import Config
from pult_relay.core import CLOSE, Core
from pult_relay.store import Store
from tools.pult_testclient import Pc, Phone, post_string, sign_p1363

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VECTORS = os.path.join(ROOT, "protocol", "vectors")


def vector(name):
    with open(os.path.join(VECTORS, name + ".json"), "r", encoding="utf-8") as f:
        return json.load(f)


class Clock:
    def __init__(self, now=1000.0):
        self.now = now

    def __call__(self):
        return self.now

    def advance(self, seconds):
        self.now += seconds


class Relay:
    def __init__(self, data_dir, clock=None, **config):
        self.data_dir = data_dir
        self.clock = clock or time.time
        self.config = Config(data_dir=data_dir, port=0, keepalive_sec=0.2, **config)
        self.peers = []
        self.start()

    def start(self):
        if getattr(self, "port", None):
            self.config = dataclasses.replace(self.config, port=self.port)
        self.store = Store(self.data_dir)
        self.core = Core(self.store, self.config, self.clock)
        self.server = create_server(self.core)
        self.port = self.server.server_address[1]
        serve_in_thread(self.server)

    @property
    def url(self):
        return f"http://127.0.0.1:{self.port}"

    def stop(self):
        for stream in list(self.core.streams.values()):
            stream.outbox.put(CLOSE)
        self.server.shutdown()
        self.server.server_close()

    def close(self):
        for peer in self.peers:
            peer.close()
        self.stop()

    def pc(self):
        peer = Pc(self.url)
        self.peers.append(peer)
        return peer

    def phone(self):
        peer = Phone()
        self.peers.append(peer)
        return peer

    def pending(self):
        pc = self.pc()
        qr = pc.claim()
        pc.login()
        return pc, qr

    def paired(self):
        pc, qr = self.pending()
        phone = self.phone()
        phone.scan(qr)
        self.peers.append(phone)
        pc.await_phone(5)
        pc.hello()
        phone.await_hello(5)
        drain(pc)
        return pc, phone


def drain(peer, timeout=0.1):
    items = []
    while True:
        try:
            items.append(peer.next_event(timeout))
        except queue.Empty:
            return items


def wait_server(peer, kind, timeout=5):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            event, data = peer.next_event(max(0.01, deadline - time.time()))
        except queue.Empty:
            break
        if event == "server" and data.get("type") == kind:
            return data
        if event == "eof":
            break
    raise AssertionError(f"no server event {kind}")


def wait_eof(peer, timeout=5):
    deadline = time.time() + timeout
    while time.time() < deadline:
        event, _ = peer.next_event(max(0.01, deadline - time.time()))
        if event == "eof":
            return
    raise AssertionError("stream not closed")


def raw_signed(peer, op, body=b"", n=None, stream=None, sign_key=None, sign_op=None, method="POST", extra=None, headers=None):
    n = peer.n + 1 if n is None else n
    stream = stream or peer.stream_id
    if isinstance(body, (dict, list)):
        body = json.dumps(body, separators=(",", ":")).encode("utf-8")
    key = sign_key or peer.keys.sign
    sig = sign_p1363(key, post_string(sign_op or op, peer.pair, peer.side, stream, n, body))
    hdrs = {"Pult-From": peer.side, "Pult-Stream": stream, "Pult-N": str(n), "Pult-Sig": sig}
    hdrs.update(headers or {})
    for name in extra or []:
        hdrs.pop(name, None)
    status, data = peer.http.request(method, f"/v2/pairs/{peer.pair}/{op}", body if method == "POST" else None, hdrs)
    if status != 401 or (data or {}).get("error") != "stale_stream":
        peer.n = max(peer.n, n)
    return status, data


def wait_until(condition, timeout=5):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if condition():
            return
        time.sleep(0.02)
    raise AssertionError("condition not met")
