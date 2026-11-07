import json
import os
import queue
import re
import select
import socket
import sys
import threading
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .codec import COUNTER_RE, MAX_SAFE_INT
from .config import PROTOCOL_VERSIONS
from .core import CLOSE, SIDES, RelayError, format_sse

PEER_POLL_SEC = 0.25
SMALL_BODY = 4096
ID = r"([A-Za-z0-9_-]{1,64})"
CLAIM_ROUTE = re.compile(rf"^/v2/claims/{ID}$")
PAIR_ROUTE = re.compile(rf"^/v2/pairs/{ID}/(challenge|stream|messages|acks|ping|delete|state)$")
POST_OPS = ("challenge", "messages", "acks", "ping", "delete")
BODY_REQUIRED = ("messages", "acks", "delete")


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
            self._json(error.status, error.payload())

        def _addr(self):
            if core.config.trust_proxy:
                forwarded = self.headers.get("X-Forwarded-For", "")
                if forwarded:
                    return forwarded.split(",")[-1].strip()
            return self.client_address[0]

        def _read_body(self, limit, required):
            if "chunked" in self.headers.get("Transfer-Encoding", "").lower():
                return self._read_chunked(limit)
            header = self.headers.get("Content-Length")
            if header is None:
                if required:
                    raise RelayError(411, "length_required")
                return b""
            try:
                length = int(header)
            except ValueError:
                raise RelayError(411, "length_required")
            if length < 0:
                raise RelayError(400, "bad_length")
            if length > limit:
                self.rfile.read(min(length, limit * 4))
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

        def _guard(self, action):
            try:
                action()
            except RelayError as e:
                self._fail(e)
            except (OSError, ValueError):
                self.close_connection = True
            except Exception:
                traceback.print_exc(file=sys.stderr)
                self._fail(RelayError(500, "internal"))

        def do_GET(self):
            self._guard(self._get)

        def do_POST(self):
            self._guard(self._post)

        def _get(self):
            if self.path == "/info":
                return self._json(200, {"name": "pult-relay", "protocol": PROTOCOL_VERSIONS})
            m = PAIR_ROUTE.match(self.path)
            if not m:
                raise RelayError(404, "not_found")
            pair_id, action = m.groups()
            if action == "stream":
                return self._login(pair_id)
            if action == "state":
                return self._signed(pair_id, action)
            raise RelayError(405, "method_not_allowed")

        def _post(self):
            if self.path == "/v2/claims":
                core.check_rate("claim", self._addr())
                raw = self._read_body(SMALL_BODY, True)
                return self._json(201, core.create_claim(raw))
            m = CLAIM_ROUTE.match(self.path)
            if m:
                core.check_rate("claim", self._addr())
                raw = self._read_body(SMALL_BODY, True)
                return self._json(200, core.submit_claim(m.group(1), raw))
            m = PAIR_ROUTE.match(self.path)
            if not m:
                raise RelayError(404, "not_found")
            pair_id, action = m.groups()
            if action not in POST_OPS:
                raise RelayError(405, "method_not_allowed")
            if action == "challenge":
                core.check_rate("challenge", self._addr())
                raw = self._read_body(SMALL_BODY, True)
                return self._json(200, core.issue_challenge(pair_id, raw))
            return self._signed(pair_id, action)

        def _signed(self, pair_id, op):
            side = self.headers.get("Pult-From")
            stream_id = self.headers.get("Pult-Stream")
            n_text = self.headers.get("Pult-N")
            sig = self.headers.get("Pult-Sig")
            if not (side and stream_id and n_text and sig) or side not in SIDES:
                raise RelayError(401, "unauthorized")
            if not COUNTER_RE.fullmatch(n_text) or len(n_text) > 16 or int(n_text) > MAX_SAFE_INT:
                raise RelayError(400, "bad_header")
            core.precheck(pair_id, side, stream_id)
            body = self._read_body(core.config.max_envelope_bytes, op in BODY_REQUIRED)
            status, payload = core.signed(pair_id, side, stream_id, n_text, op, body, sig)
            self._json(status, payload)

        def _login(self, pair_id):
            side = self.headers.get("Pult-From")
            challenge = self.headers.get("Pult-Challenge")
            sig = self.headers.get("Pult-Sig")
            if not (challenge and sig) or side not in SIDES:
                raise RelayError(401, "unauthorized")
            stream = core.login(pair_id, side, challenge, sig)
            self._stream(pair_id, stream)

        def _peer_gone(self):
            try:
                ready, _, _ = select.select([self.connection], [], [], 0)
                if not ready:
                    return False
                return self.connection.recv(1, socket.MSG_PEEK) == b""
            except (OSError, ValueError):
                return True

        def _stream(self, pair_id, stream):
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
    allow_reuse_address = os.name != "nt"

    def server_bind(self):
        if os.name == "nt":
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()

    def handle_error(self, request, client_address):
        if not isinstance(sys.exc_info()[1], OSError):
            super().handle_error(request, client_address)


def create_server(core):
    return Server((core.config.host, core.config.port), make_handler(core))


def serve_in_thread(server):
    t = threading.Thread(target=lambda: server.serve_forever(poll_interval=0.02), daemon=True)
    t.start()
    return t
