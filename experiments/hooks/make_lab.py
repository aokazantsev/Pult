import argparse
import json
import sys
from pathlib import Path

EVENTS = [
    "SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PermissionDenied",
    "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop",
    "PreCompact", "CwdChanged",
]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--lab", default=str(Path(__file__).parent / "lab"))
    p.add_argument("--timeout", type=int, default=30)
    a = p.parse_args()
    lab = Path(a.lab).resolve()
    (lab / ".claude").mkdir(parents=True, exist_ok=True)
    stub = (Path(__file__).parent / "stub.py").resolve().as_posix()
    py = Path(sys.executable).as_posix()
    log = (lab / "hooks.jsonl").as_posix()
    control = (lab / "control.json").as_posix()
    hooks = {}
    for event in EVENTS:
        command = f'"{py}" "{stub}" --label {event} --log "{log}" --control "{control}"'
        timeout = {"PermissionRequest": 60, "Stop": 120}.get(event, a.timeout)
        hooks[event] = [{"matcher": "*", "hooks": [{"type": "command", "command": command, "timeout": timeout}]}]
    (lab / ".claude" / "settings.json").write_text(json.dumps({"hooks": hooks}, indent=2), encoding="utf-8")
    if not (lab / "control.json").exists():
        (lab / "control.json").write_text("{}", encoding="utf-8")
    print(lab)


if __name__ == "__main__":
    main()
