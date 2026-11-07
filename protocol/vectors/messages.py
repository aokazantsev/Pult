import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ID_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")
MAX_SAFE_INT = 2**53 - 1
MESSAGE_BYTES = 48000
ITEM_BYTES = 16384
TO_PHONE = {"thread", "feed", "permission_request", "choice_request", "text_request", "resolved", "mode", "snapshot", "hello"}
TO_PC = {"permission_response", "choice_response", "text_response", "hello"}
REQUEST_TYPES = ("permission_request", "choice_request", "text_request")
TAIL_KNOWN = 200
TAIL_NEW = 50
TAIL_BUDGET = 1024 * 1024
PHONE_KEEP = 500
BIDI = set(range(0x202A, 0x202F)) | set(range(0x2066, 0x206A)) | {0x200E, 0x200F}
WHITE_SPACE = "".join(chr(c) for c in [9, 10, 11, 12, 13, 32, 0x85, 0xA0, 0x1680, *range(0x2000, 0x200B), 0x2028, 0x2029, 0x202F, 0x205F, 0x3000])
BARE_URL = re.compile("https?://[^" + re.escape(WHITE_SPACE) + "<>\"'`]+")
MD_LINK = re.compile(r"(?<!!)\[([^\]]*)\]\(([^\s()]+)\)")
CODE = re.compile(r"```.*?```|`[^`\n]*`", re.S)


class Rejected(Exception):
    pass


def reject_duplicates(pairs):
    out = {}
    for key, value in pairs:
        if key in out:
            raise Rejected("duplicate key " + key)
        out[key] = value
    return out


def strict_load(text):
    return json.loads(text, object_pairs_hook=reject_duplicates)


def size(obj):
    return len(json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))


def no_nulls(value):
    if value is None:
        raise Rejected("null")
    if isinstance(value, dict):
        for item in value.values():
            no_nulls(item)
    if isinstance(value, list):
        for item in value:
            no_nulls(item)


def need(obj, key, required=True):
    if key not in obj:
        if required:
            raise Rejected("missing " + key)
        return None
    return obj[key]


def ident(obj, key, required=True):
    value = need(obj, key, required)
    if value is None:
        return None
    if not isinstance(value, str) or not ID_RE.fullmatch(value):
        raise Rejected("bad id " + key)
    return value


def text(obj, key, limit, required=True, nonempty=False):
    value = need(obj, key, required)
    if value is None:
        return None
    if not isinstance(value, str):
        raise Rejected("not a string " + key)
    if len(value) > limit:
        raise Rejected("too long " + key)
    if nonempty and not value.strip(WHITE_SPACE):
        raise Rejected("empty " + key)
    return value


def integer(obj, key, required=True, minimum=0):
    value = need(obj, key, required)
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum or value > MAX_SAFE_INT:
        raise Rejected("bad int " + key)
    return value


def boolean(obj, key, required=True):
    value = need(obj, key, required)
    if value is None:
        return None
    if not isinstance(value, bool):
        raise Rejected("not bool " + key)
    return value


def array(obj, key, required=True, minimum=0, maximum=None):
    value = need(obj, key, required)
    if value is None:
        return None
    if not isinstance(value, list) or len(value) < minimum or (maximum is not None and len(value) > maximum):
        raise Rejected("bad array " + key)
    return value


def obj_of(value, label):
    if not isinstance(value, dict):
        raise Rejected("not an object " + label)
    if size(value) > ITEM_BYTES:
        raise Rejected("item too large " + label)
    return value


def check_thread(desc, in_snapshot):
    obj_of(desc, "thread")
    ident(desc, "thread")
    text(desc, "title", 120, nonempty=True)
    text(desc, "folder", 120)
    text(desc, "status", 64)
    boolean(desc, "pc_dialog")
    text(desc, "error", 64, required=False)
    ident(desc, "parent", required=False)
    fork = need(desc, "fork_of", required=False)
    if fork is not None:
        obj_of(fork, "fork_of")
        text(fork, "title", 120)
        ident(fork, "thread", required=False)
    text(desc, "turn_note", 64, required=False)
    integer(desc, "last_seq", required=in_snapshot)


def check_input(value):
    if not isinstance(value, dict):
        raise Rejected("input not an object")
    for item in value.values():
        if isinstance(item, str):
            if len(item) > 1000:
                raise Rejected("input value too long")
        elif isinstance(item, (dict, list)) or item is None:
            raise Rejected("input value not flat")


ENTRY_FIELDS = {
    "user": lambda e: (text(e, "text", 8000), text(e, "via", 64, required=False), ident(e, "request_id", required=False)),
    "assistant": lambda e: text(e, "text", 8000),
    "tool_call": lambda e: (ident(e, "call"), text(e, "tool", 64), text(e, "summary", 300), "input" in e and check_input(e["input"])),
    "tool_result": lambda e: (ident(e, "call"), text(e, "output", 2000), boolean(e, "error")),
    "peer": lambda e: (text(e, "direction", 64), text(e, "peer_title", 120), text(e, "text", 8000), ident(e, "peer", required=False)),
    "notice": lambda e: text(e, "text", 300),
    "unrecognized": lambda e: integer(e, "count", minimum=1),
}


def check_entry(entry):
    obj_of(entry, "entry")
    integer(entry, "seq", minimum=1)
    kind = text(entry, "kind", 64)
    integer(entry, "at", required=False)
    boolean(entry, "truncated", required=False)
    check = ENTRY_FIELDS.get(kind)
    if check:
        check(entry)


def check_request(msg):
    obj_of(msg, "request")
    ident(msg, "request_id")
    ident(msg, "thread")
    text(msg, "title", 120)
    integer(msg, "after_seq")
    kind = msg.get("type")
    if kind == "permission_request":
        text(msg, "tool", 64)
        text(msg, "summary", 300)
        if "input" in msg:
            check_input(msg["input"])
    elif kind == "choice_request":
        boolean(msg, "answerable")
        questions = array(msg, "questions", minimum=1, maximum=4)
        seen = set()
        for q in questions:
            obj_of(q, "question")
            qid = ident(q, "id")
            if qid in seen:
                raise Rejected("duplicate question id")
            seen.add(qid)
            text(q, "header", 50, required=False)
            text(q, "question", 500)
            boolean(q, "multi")
            boolean(q, "other")
            options = array(q, "options", minimum=1, maximum=8)
            oids = set()
            for o in options:
                obj_of(o, "option")
                oid = ident(o, "id")
                if oid in oids:
                    raise Rejected("duplicate option id")
                oids.add(oid)
                text(o, "label", 100)
                text(o, "description", 300, required=False)
    elif kind == "text_request":
        text(msg, "last_message", 4000)
    else:
        raise Rejected("not a request")


def check_outcome(item):
    obj_of(item, "outcome")
    ident(item, "request_id")
    text(item, "kind", 64)
    ident(item, "thread")
    integer(item, "after_seq")
    text(item, "summary", 120)
    text(item, "outcome", 64)


def check_hello(msg, to):
    protocol = array(msg, "protocol", minimum=1)
    for v in protocol:
        if isinstance(v, bool) or not isinstance(v, int):
            raise Rejected("bad protocol")
    for cap in array(msg, "capabilities"):
        if not isinstance(cap, str):
            raise Rejected("bad capability")
    feeds = array(msg, "feeds", required=False)
    if feeds is not None:
        if to != "pc":
            raise Rejected("feeds only from phone")
        threads = set()
        for f in feeds:
            obj_of(f, "feeds")
            t = ident(f, "thread")
            if t in threads:
                raise Rejected("duplicate feeds thread")
            threads.add(t)
            integer(f, "seq")


def check_message(msg, to):
    if not isinstance(msg, dict):
        raise Rejected("not an object")
    no_nulls(msg)
    if size(msg) > MESSAGE_BYTES:
        raise Rejected("message too large")
    kind = need(msg, "type")
    if not isinstance(kind, str):
        raise Rejected("bad type")
    allowed = TO_PHONE if to == "phone" else TO_PC
    if kind not in TO_PHONE | TO_PC:
        return
    if kind not in allowed:
        raise Rejected("type not for " + to)
    if kind == "thread":
        check_thread(msg, False)
    elif kind == "feed":
        ident(msg, "thread")
        entries = array(msg, "entries", minimum=1)
        previous = None
        for entry in entries:
            check_entry(entry)
            if previous is not None and entry["seq"] != previous + 1:
                raise Rejected("seq not contiguous")
            previous = entry["seq"]
    elif kind in REQUEST_TYPES:
        check_request(msg)
    elif kind == "resolved":
        ident(msg, "request_id")
        text(msg, "outcome", 64)
    elif kind == "mode":
        text(msg, "mode", 64)
    elif kind == "snapshot":
        ident(msg, "snapshot")
        part = integer(msg, "part", minimum=1)
        boolean(msg, "last")
        text(msg, "mode", 64, required=part == 1)
        for desc in array(msg, "threads", required=False) or []:
            check_thread(desc, True)
        for req in array(msg, "requests", required=False) or []:
            check_request(req)
        for item in array(msg, "outcomes", required=False) or []:
            check_outcome(item)
    elif kind == "hello":
        check_hello(msg, to)
    elif kind == "permission_response":
        ident(msg, "request_id")
        if need(msg, "decision") not in ("allow", "deny"):
            raise Rejected("bad decision")
        text(msg, "reason", 1000, required=False)
    elif kind == "choice_response":
        ident(msg, "request_id")
        for a in array(msg, "answers", minimum=1):
            obj_of(a, "answer")
            ident(a, "question")
            for s in array(a, "selected"):
                if not isinstance(s, str) or not ID_RE.fullmatch(s):
                    raise Rejected("bad selected")
            text(a, "other", 2000, required=False)
    elif kind == "text_response":
        ident(msg, "request_id")
        text(msg, "text", 8000, nonempty=True)


def check_choice_answer(request, response):
    if not request["answerable"]:
        raise Rejected("not answerable")
    questions = {q["id"]: q for q in request["questions"]}
    answered = set()
    for a in response["answers"]:
        q = questions.get(a["question"])
        if q is None or a["question"] in answered:
            raise Rejected("unknown or repeated question")
        answered.add(a["question"])
        selected = a["selected"]
        options = {o["id"] for o in q["options"]}
        if len(set(selected)) != len(selected) or not set(selected) <= options:
            raise Rejected("bad selection")
        other = a.get("other")
        if other is not None and (not q["other"] or not other.strip()):
            raise Rejected("bad other")
        if q["multi"]:
            if not selected and other is None:
                raise Rejected("empty multi answer")
        elif not ((len(selected) == 1 and other is None) or (not selected and other is not None)):
            raise Rejected("single answer must be one option or other")
    if answered != set(questions):
        raise Rejected("not every question answered")


def apply_snapshot(state, parts):
    collected = []
    for part in parts:
        if part["part"] == 1:
            collected = [part]
        elif not collected or part["part"] != len(collected) + 1 or part["snapshot"] != collected[0]["snapshot"]:
            return None
        else:
            collected.append(part)
        if part["last"]:
            break
    if not collected or not collected[-1]["last"]:
        return None
    threads = [t for p in collected for t in p.get("threads", [])]
    requests = [r for p in collected for r in p.get("requests", [])]
    outcomes = [o for p in collected for o in p.get("outcomes", [])]
    after = {"mode": collected[0]["mode"], "threads": {}, "cards": {}}
    for desc in threads:
        after["threads"][desc["thread"]] = desc["last_seq"]
    for rid, card in state["cards"].items():
        if card["thread"] in after["threads"]:
            after["cards"][rid] = dict(card)
    open_ids = set()
    for req in requests:
        open_ids.add(req["request_id"])
        card = after["cards"].get(req["request_id"])
        if card is None or card["state"] == "missing":
            after["cards"][req["request_id"]] = {"thread": req["thread"], "state": "open"}
    closed_ids = set()
    for item in outcomes:
        if item["thread"] not in after["threads"]:
            continue
        closed_ids.add(item["request_id"])
        card = after["cards"].get(item["request_id"])
        if card is None:
            after["cards"][item["request_id"]] = {"thread": item["thread"], "state": "closed", "outcome": item["outcome"]}
        elif card["state"] != "closed":
            card["state"] = "closed"
            card["outcome"] = item["outcome"]
    for rid, card in after["cards"].items():
        if card["state"] in ("open", "sending", "sent") and rid not in open_ids and rid not in closed_ids:
            card["state"] = "missing"
    return after


def feed_markers(stored, incoming, last_seq):
    seqs = set(stored)
    for batch in incoming:
        seqs.update(batch)
    ordered = sorted(seqs)[-PHONE_KEEP:]
    markers = []
    if ordered and ordered[0] > 1:
        markers.append({"where": "start"})
    for a, b in zip(ordered, ordered[1:]):
        if b - a > 1:
            markers.append({"where": "between", "after": a, "missing": b - a - 1})
    if last_seq is not None and (not ordered or last_seq > ordered[-1]):
        markers.append({"where": "end"})
    return ordered, markers


def tails(known, threads):
    order = sorted(threads, key=lambda t: (not t["open_request"], -t["last_event"]))
    left = TAIL_BUDGET
    out = {}
    for t in order:
        last = t["last_seq"]
        if t["thread"] in known:
            first = max(known[t["thread"]] + 1, last - TAIL_KNOWN + 1)
        else:
            first = max(1, last - TAIL_NEW + 1)
        if first > last:
            out[t["thread"]] = None
            continue
        cost = (last - first + 1) * t["entry_bytes"]
        if cost > left:
            out[t["thread"]] = None
            continue
        left -= cost
        out[t["thread"]] = [first, last]
    return out


def sanitize(value):
    value = value.replace(chr(13) + chr(10), chr(10)).replace(chr(13), chr(10))
    out = []
    for ch in value:
        code = ord(ch)
        if code in BIDI:
            continue
        if (code < 0x20 and ch not in "\n\t") or 0x7F <= code < 0xA0:
            out.append("�")
            continue
        out.append(ch)
    return "".join(out)


def links(value, field):
    if field != "markdown":
        return []
    masked = CODE.sub(lambda m: " " * len(m.group(0)), value)
    found = []
    taken = []
    for m in MD_LINK.finditer(masked):
        found.append((m.start(), m.group(2)))
        taken.append((m.start(), m.end()))
    for m in BARE_URL.finditer(masked):
        if any(a <= m.start() < b for a, b in taken):
            continue
        url = m.group(0).rstrip(".,;:!?)")
        found.append((m.start(), url))
    found.sort()
    return [{"url": url, "openable": url.startswith(("http://", "https://"))} for _, url in found]


def run(data):
    failures = []

    def expect(ok, label):
        if not ok:
            failures.append(label)

    for case in data["valid"]:
        try:
            check_message(case["message"], case["to"])
        except Rejected as e:
            failures.append("valid " + case["name"] + ": " + str(e))
    for case in data["reject"]:
        try:
            msg = strict_load(case["text"]) if "text" in case else case["message"]
            check_message(msg, case["to"])
            failures.append("reject accepted: " + case["name"])
        except (Rejected, ValueError):
            pass
    request = data["choice_answers"]["request"]
    check_message(request, "phone")
    for response in data["choice_answers"]["accept"]:
        try:
            check_message(response, "pc")
            check_choice_answer(request, response)
        except Rejected as e:
            failures.append("choice accept: " + str(e) + " " + json.dumps(response, ensure_ascii=False))
    for response in data["choice_answers"]["reject"]:
        try:
            check_message(response, "pc")
            check_choice_answer(request, response)
            failures.append("choice reject accepted: " + json.dumps(response, ensure_ascii=False))
        except Rejected:
            pass
    for case in data["snapshot_cases"]:
        for part in case["parts"]:
            try:
                check_message(part, "phone")
            except Rejected as e:
                failures.append("snapshot part " + case["name"] + ": " + str(e))
        after = apply_snapshot(case["before"], case["parts"])
        expect(after == case["after"], "snapshot " + case["name"] + ": got " + json.dumps(after, ensure_ascii=False))
    for case in data["feed_cases"]:
        ordered, markers = feed_markers(case["stored"], case["incoming"], case.get("last_seq"))
        expect(ordered == case["expected_stored"], "feed stored " + case["name"])
        expect(markers == case["markers"], "feed markers " + case["name"] + ": got " + json.dumps(markers))
    for case in data["tails"]:
        got = tails(case["known"], case["threads"])
        expect(got == case["expected"], "tails " + case["name"] + ": got " + json.dumps(got))
    for case in data["render"]:
        expect(sanitize(case["input"]) == case["sanitized"], "render sanitize " + case["name"])
        expect(links(sanitize(case["input"]), case["field"]) == case["links"], "render links " + case["name"] + ": got " + json.dumps(links(sanitize(case["input"]), case["field"])))
    return failures


def main():
    data = strict_load((HERE / "messages.json").read_text(encoding="utf-8"))
    failures = run(data)
    for failure in failures:
        print("FAIL", failure)
    print("ok" if not failures else str(len(failures)) + " failures")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
