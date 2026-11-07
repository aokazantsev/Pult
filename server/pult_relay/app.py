import json
import queue
import re
import select
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .config import PROTOCOL_VERSIONS
from .core import CLOSE, RelayError, format_sse

PEER_POLL_SEC = 0.25
ROUTE = re.compile(r"^/v1/pairs/([A-Za-z0-9_-]{1,64})/(stream|messages|acks|ping|state)$")


def make_handler(core):
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"
        server_version = "pult-relay"

        def log_message(self, fmt, *args):
            pass

        def _json(self, status, payload):
            body = json.dumps(payload).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def _fail(self, error):
            self.close_connection = True
            self._json(error.status, {"error": error.code})

        def _auth(self, pair_id):
            header = self.headers.get("Authorization", "")
            token = header[7:] if header.startswith("Bearer ") else ""
            return core.authenticate(pair_id, token)

        def _read_body(self):
            limit = core.config.max_envelope_bytes
            if "chunked" in self.headers.get("Transfer-Encoding", "").lower():
                return self._read_chunked(limit)
            try:
                length = int(self.headers.get("Content-Length", ""))
            except ValueError:
                raise RelayError(411, "length_required")
            if length < 0:
                raise RelayError(400, "bad_length")
            if length > limit:
                raise RelayError(413, "too_large")
            return self.rfile.read(length)

        def _read_chunked(self, limit):
            body = bytearray()
            while True:
                line = self.rfile.readline(66)
                try:
                    size = int(line.split(b";", 1)[0].strip(), 16)
                except ValueError:
                    raise RelayError(400, "bad_chunk")
                if size < 0:
                    raise RelayError(400, "bad_chunk")
                if size == 0:
                    while True:
                        trailer = self.rfile.readline(65537)
                        if not trailer.strip():
                            break
                    return bytes(body)
                if len(body) + size > limit:
                    raise RelayError(413, "too_large")
                chunk = self.rfile.read(size)
                if len(chunk) != size:
                    raise RelayError(400, "bad_chunk")
                body += chunk
                self.rfile.readline(3)

        def do_GET(self):
            try:
                if self.path == "/info":
                    return self._json(200, {"name": "pult-relay", "protocol": PROTOCOL_VERSIONS})
                m = ROUTE.match(self.path)
                if not m:
                    raise RelayError(404, "not_found")
                pair_id, action = m.groups()
                side = self._auth(pair_id)
                if action == "stream":
                    return self._stream(pair_id, side)
                if action == "state":
                    return self._json(200, core.state(pair_id))
                raise RelayError(405, "method_not_allowed")
            except RelayError as e:
                self._fail(e)

        def do_POST(self):
            try:
                m = ROUTE.match(self.path)
                if not m:
                    raise RelayError(404, "not_found")
                pair_id, action = m.groups()
                side = self._auth(pair_id)
                raw = self._read_body()
                if action == "ping":
                    with core.lock:
                        core.touch(pair_id, side)
                    return self._json(200, {"ok": True})
                try:
                    data = json.loads(raw)
                except ValueError:
                    raise RelayError(400, "bad_json")
                if action == "messages":
                    return self._json(202, core.post(pair_id, side, data, len(raw)))
                if action == "acks":
                    ids = data.get("ids") if isinstance(data, dict) else None
                    return self._json(200, core.ack(pair_id, side, ids))
                raise RelayError(405, "method_not_allowed")
            except RelayError as e:
                self._fail(e)

        def _peer_gone(self):
            try:
                ready, _, _ = select.select([self.connection], [], [], 0)
                if not ready:
                    return False
                return self.connection.recv(1, socket.MSG_PEEK) == b""
            except (OSError, ValueError):
                return True

        def _stream(self, pair_id, side):
            stream = core.open_stream(pair_id, side)
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("X-Accel-Buffering", "no")
            self.send_header("Connection", "close")
            self.end_headers()
            self.close_connection = True
            try:
                self.wfile.write(b": open\n\n")
                self.wfile.flush()
                idle = 0.0
                while True:
                    try:
                        item = stream.outbox.get(timeout=PEER_POLL_SEC)
                    except queue.Empty:
                        if self._peer_gone():
                            break
                        idle += PEER_POLL_SEC
                        if idle >= core.config.keepalive_sec:
                            idle = 0.0
                            self.wfile.write(b": keepalive\n\n")
                            self.wfile.flush()
                        continue
                    idle = 0.0
                    if item is CLOSE:
                        break
                    kind, seq, payload = item
                    self.wfile.write(format_sse(kind, seq, payload))
                    self.wfile.flush()
            except (OSError, ValueError):
                pass
            finally:
                core.close_stream(pair_id, stream)

    return Handler


class Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def create_server(core):
    return Server((core.config.host, core.config.port), make_handler(core))


def serve_in_thread(server):
    t = threading.Thread(target=server.serve_forever, daemon=True)
    t.start()
    return t
