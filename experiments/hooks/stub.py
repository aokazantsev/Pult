import argparse
import json
import os
import sys
import threading
import time
from pathlib import Path


def now():
    return time.strftime("%H:%M:%S", time.localtime()) + f".{int(time.time() * 1000) % 1000:03d}"


def append(path, record):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


def heartbeat(path, label, stop):
    while not stop.is_set():
        append(path, {"t": now(), "pid": os.getpid(), "label": label, "beat": True})
        stop.wait(1)


def counter_next(path):
    n = int(path.read_text()) + 1 if path.exists() else 1
    path.write_text(str(n))
    return n


def wait_for_answer(path, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if path.exists():
            text = path.read_text(encoding="utf-8")
            path.unlink()
            return text
        time.sleep(0.2)
    return None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--label", default="hook")
    p.add_argument("--log", default=os.environ.get("PULT_STUB_LOG", str(Path(__file__).parent / "log" / "hooks.jsonl")))
    p.add_argument("--sleep", type=float, default=0)
    p.add_argument("--heartbeat", action="store_true")
    p.add_argument("--stdout", default=None)
    p.add_argument("--stdout-file", default=None)
    p.add_argument("--exit", type=int, default=0)
    p.add_argument("--stderr", default=None)
    p.add_argument("--answer-file", default=None)
    p.add_argument("--answer-timeout", type=float, default=60)
    p.add_argument("--block-times", type=int, default=0)
    p.add_argument("--counter", default=None)
    p.add_argument("--control", default=None)
    a = p.parse_args()
    if a.control and Path(a.control).exists():
        entry = json.loads(Path(a.control).read_text(encoding="utf-8")).get(a.label, {})
        for key, value in entry.items():
            setattr(a, key.replace("-", "_"), value)

    log = Path(a.log)
    raw = sys.stdin.buffer.read().decode("utf-8", errors="replace")
    try:
        data = json.loads(raw)
    except ValueError:
        data = {"raw": raw}
    env = {k: v for k, v in os.environ.items() if k.startswith(("CLAUDE", "PULT"))}
    append(log, {"t": now(), "pid": os.getpid(), "ppid": os.getppid(), "label": a.label, "input": data, "env": env})

    stop = threading.Event()
    if a.heartbeat:
        threading.Thread(target=heartbeat, args=(log, a.label, stop), daemon=True).start()

    out = a.stdout
    if a.stdout_file:
        out = Path(a.stdout_file).read_text(encoding="utf-8")
    if a.sleep:
        time.sleep(a.sleep)
    if a.answer_file:
        got = wait_for_answer(Path(a.answer_file), a.answer_timeout)
        if got is not None:
            out = got
    if a.block_times:
        n = counter_next(Path(a.counter or str(log) + ".counter"))
        if n <= a.block_times:
            out = json.dumps({"decision": "block", "reason": f"Continuation {n}: reply with the single word ok{n} and stop."})
    stop.set()
    append(log, {"t": now(), "pid": os.getpid(), "label": a.label, "done": True, "stdout": out, "exit": a.exit})
    if out:
        sys.stdout.buffer.write(out.encode("utf-8"))
        sys.stdout.buffer.flush()
    if a.stderr:
        sys.stderr.write(a.stderr)
    sys.exit(a.exit)


if __name__ == "__main__":
    main()
