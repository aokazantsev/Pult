import http.client
import json
import queue
import socket
import threading


def envelope(pair, sender, mid, ttl=60, inner=None):
    return {"v": 1, "pair": pair, "from": sender, "id": mid, "ts": 0, "ttl": ttl, "inner": inner or {"type": "text_request"}}


def drain(stream):
    items = []
    while True:
        try:
            items.append(stream.outbox.get_nowait())
        except queue.Empty:
            return items


class Clock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


class SseClient:
    def __init__(self, port, pair, token):
        self.events = queue.Queue()
        self.conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
        self.conn.request("GET", f"/v1/pairs/{pair}/stream", headers={"Authorization": f"Bearer {token}"})
        self.sock = self.conn.sock
        self.response = self.conn.getresponse()
        self.status = self.response.status
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
        except (OSError, ValueError):
            pass
        self.events.put(("eof", None))

    def next(self, timeout=5):
        return self.events.get(timeout=timeout)

    def close(self):
        try:
            self.sock.shutdown(socket.SHUT_RDWR)
            self.conn.close()
        except OSError:
            pass


def request(port, method, path, token=None, body=None):
    conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    payload = None
    if body is not None:
        payload = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    conn.request(method, path, body=payload, headers=headers)
    resp = conn.getresponse()
    data = resp.read()
    conn.close()
    return resp.status, (json.loads(data) if data else None)
