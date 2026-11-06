import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def make_handler(log, delay, reply):
    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            length = int(self.headers.get("Content-Length") or 0)
            body = self.rfile.read(length).decode("utf-8", errors="replace")
            record = {
                "t": time.strftime("%H:%M:%S"),
                "path": self.path,
                "client": self.client_address[0],
                "headers": dict(self.headers),
                "body": json.loads(body) if body.startswith("{") else body,
            }
            with log.open("a", encoding="utf-8") as f:
                f.write(json.dumps(record, ensure_ascii=False) + "\n")
            if delay:
                time.sleep(delay)
            payload = reply.encode("utf-8")
            try:
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
            except OSError as e:
                with log.open("a", encoding="utf-8") as f:
                    f.write(json.dumps({"t": time.strftime("%H:%M:%S"), "write_failed": str(e)}) + "\n")

        def log_message(self, *args):
            pass

    return Handler


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=47801)
    p.add_argument("--log", default=str(Path(__file__).parent / "log" / "http.jsonl"))
    p.add_argument("--delay", type=float, default=0)
    p.add_argument("--reply", default="{}")
    a = p.parse_args()
    log = Path(a.log)
    log.parent.mkdir(parents=True, exist_ok=True)
    ThreadingHTTPServer(("127.0.0.1", a.port), make_handler(log, a.delay, a.reply)).serve_forever()


if __name__ == "__main__":
    main()
