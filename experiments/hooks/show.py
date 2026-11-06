import json
import sys
from pathlib import Path

FIELDS = ("tool_name", "source", "reason", "error", "stop_hook_active", "notification_type", "message", "prompt", "agent_id", "agent_type")


def main():
    log = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent / "lab" / "hooks.jsonl")
    skip = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    lines = log.read_text(encoding="utf-8").splitlines()[skip:]
    beats = {}
    for line in lines:
        r = json.loads(line)
        if r.get("beat"):
            beats.setdefault(r["pid"], []).append(r["t"])
            continue
        if r.get("done"):
            print(f'{r["t"]} {r["label"]} pid={r["pid"]} done stdout={(r.get("stdout") or "")[:80]}')
            continue
        i = r.get("input", {})
        extra = {k: (str(i[k])[:80]) for k in FIELDS if k in i}
        print(f'{r["t"]} {r["label"]} pid={r["pid"]} sid={i.get("session_id", "")[:8]} mode={i.get("permission_mode", "")} cwd={i.get("cwd", "")} {extra}')
    for pid, ts in beats.items():
        print(f"beats pid={pid}: {len(ts)} from {ts[0]} to {ts[-1]}")
    print(f"lines={len(lines) + skip}")


if __name__ == "__main__":
    main()
