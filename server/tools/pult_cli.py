import argparse
import json
import os
import queue
import sys
import threading
import time

from tools.pult_testclient import ClientError, Pc, Phone

AUTO_REPLIES = {
    "permission_request": lambda b: {"type": "permission_response", "request_id": b["request_id"], "decision": "allow", "reason": ""},
    "choice_request": lambda b: {"type": "choice_response", "request_id": b["request_id"], "selected": [b["options"][0]["id"]] if b.get("options") else []},
    "text_request": lambda b: {"type": "text_response", "request_id": b["request_id"], "text": "ok"},
}


def say(*parts):
    print(*parts, flush=True)


def load_state(path):
    if path and os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    return None


def save_state(path, peer):
    if path:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(peer.state_dump(), f)


def stdin_sender(peer):
    def run():
        for line in sys.stdin:
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                status, body, env = peer.send(json.loads(line))
                say(f"sent {env['id']}: {status} {body}")
            except (ValueError, ClientError, OSError) as e:
                say(f"send failed: {e}")

    threading.Thread(target=run, daemon=True).start()


def listen(peer, auto, seconds):
    deadline = time.time() + seconds if seconds else None
    pinger = threading.Thread(target=lambda: _ping_loop(peer), daemon=True)
    pinger.start()
    while deadline is None or time.time() < deadline:
        try:
            kind, body = peer.receive(1)
        except queue.Empty:
            continue
        if kind == "eof":
            say("stream closed")
            return
        say(f"{kind}: {json.dumps(body, ensure_ascii=False)}")
        if kind == "message" and auto and body.get("type") in AUTO_REPLIES:
            status, data, env = peer.send(AUTO_REPLIES[body["type"]](body))
            say(f"auto-reply {env['id']}: {status} {data}")


def _ping_loop(peer):
    while peer.sse:
        time.sleep(15)
        try:
            peer.ping()
        except (OSError, ClientError):
            return


def cmd_pc(args):
    dump = load_state(args.state)
    if dump and dump["side"] == "pc":
        pc = Pc.restore(args.server, dump)
        pc.login()
        say(f"restored pair {pc.pair}")
    else:
        qr = pc_claim_and_wait(args)
        pc = qr
    pc.hello()
    stdin_sender(pc)
    listen(pc, args.auto, args.seconds)
    return 0


def pc_claim_and_wait(args):
    pc = Pc(args.server)
    qr = pc.claim(args.idle)
    say("QR:")
    say(qr)
    if args.qr_file:
        with open(args.qr_file, "w", encoding="utf-8") as f:
            f.write(qr)
    pc.login()
    say(f"pair {pc.pair}, waiting for the phone")
    fp = pc.await_phone(args.timeout)
    say(f"phone paired, fingerprint {fp}")
    save_state(args.state, pc)
    return pc


def cmd_phone(args):
    dump = load_state(args.state)
    if dump and dump["side"] == "phone":
        phone = Phone.restore(dump)
        phone.login()
        say(f"restored pair {phone.pair}")
    else:
        qr = args.qr
        if args.qr_file:
            with open(args.qr_file, "r", encoding="utf-8") as f:
                qr = f.read().strip()
        if not qr:
            say("need --qr or --qr-file")
            return 2
        phone = Phone()
        fp, idle = phone.scan(qr)
        say(f"scanned, fingerprint {fp}, idle {idle}")
        phone.await_hello(args.timeout)
        say("hello received, pair active")
        save_state(args.state, phone)
    phone.hello()
    stdin_sender(phone)
    listen(phone, args.auto, args.seconds)
    return 0


def cmd_demo(args):
    pc = Pc(args.server)
    qr = pc.claim(args.idle)
    say("QR:", qr)
    pc.login()
    phone = Phone()
    fp, _ = phone.scan(qr)
    say("phone fingerprint:", fp)
    say("pc fingerprint:   ", pc.await_phone(10))
    pc.hello()
    say("phone got:", phone.await_hello(10))
    phone.hello()
    say("pc got:", pc.receive(5)[1])
    status, data, env = pc.send({"type": "permission_request", "request_id": "r1", "thread": "t1", "title": "demo", "tool": "Bash", "summary": "ls", "input": {"command": "ls"}})
    say("pc sent", status, data)
    kind, body = phone.receive(5)
    say("phone got:", body)
    status, data, env = phone.send(AUTO_REPLIES["permission_request"](body))
    say("phone sent", status, data)
    say("pc got:", pc.receive(5)[1])
    say("state:", pc.state())
    pc.delete("user")
    say("phone event:", phone.receive(5))
    pc.close()
    phone.close()
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(prog="pult_cli")
    parser.add_argument("--server", default="http://127.0.0.1:8082")
    sub = parser.add_subparsers(dest="cmd", required=True)
    pc = sub.add_parser("pc")
    pc.add_argument("--state")
    pc.add_argument("--qr-file")
    pc.add_argument("--idle", type=int, default=14400)
    pc.add_argument("--timeout", type=int, default=600)
    pc.add_argument("--seconds", type=int, default=0)
    pc.add_argument("--auto", action="store_true")
    phone = sub.add_parser("phone")
    phone.add_argument("--state")
    phone.add_argument("--qr")
    phone.add_argument("--qr-file")
    phone.add_argument("--timeout", type=int, default=120)
    phone.add_argument("--seconds", type=int, default=0)
    phone.add_argument("--auto", action="store_true")
    demo = sub.add_parser("demo")
    demo.add_argument("--idle", type=int, default=14400)
    args = parser.parse_args(argv)
    try:
        return {"pc": cmd_pc, "phone": cmd_phone, "demo": cmd_demo}[args.cmd](args)
    except KeyboardInterrupt:
        return 0
    except ClientError as e:
        say(f"error: {e}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
