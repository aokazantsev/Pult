import http.client
import json
import os
import tempfile
import unittest

from pult_relay.codec import b64u
from tests.helpers import Clock, Relay, drain, raw_signed, wait_eof, wait_server, wait_until
from tools.pult_testclient import ClientError, KeyPair, LoginRefused, compact, fingerprint, sign_p1363, login_string


class RelayCase(unittest.TestCase):
    clock_fake = True
    config = {}

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.clock = Clock() if self.clock_fake else None
        self.relay = Relay(self.tmp.name, self.clock, **self.config)
        self.addCleanup(self.relay.close)


class PairingTest(RelayCase):
    def test_info_and_v1_gone(self):
        pc = self.relay.pc()
        self.assertEqual(pc.http.request("GET", "/info")[1], {"name": "pult-relay", "protocol": [2]})
        self.assertEqual(pc.http.request("GET", "/v1/pairs/x/stream")[0], 404)
        self.assertEqual(pc.http.request("POST", "/v1/pairs/x/messages", {})[0], 404)

    def test_full_pairing_and_messages(self):
        pc, phone = self.relay.paired()
        self.assertEqual(pc.text, phone.text)
        status, data, _ = pc.send({"type": "status", "thread": "t", "status": "working"}, ttl=0)
        self.assertEqual(status, 202)
        kind, body = phone.receive(5)
        self.assertEqual(body["status"], "working")
        status, data, _ = phone.send({"type": "permission_response", "request_id": "r", "decision": "allow", "reason": ""})
        self.assertEqual(status, 202)
        self.assertEqual(pc.receive(5)[1]["decision"], "allow")
        self.assertEqual(pc.state()[1]["phone_stream"], "open")

    def test_pc_gets_pair_signal_with_phone_keys_and_tag(self):
        pc, qr = self.relay.pending()
        phone = self.relay.phone()
        phone.scan(qr)
        signal = wait_server(pc, "pair")
        self.assertEqual(signal["phone_sign"], phone.keys.sign_public)
        self.assertEqual(signal["phone_agree"], phone.keys.agree_public)
        self.assertEqual(fingerprint(phone.text), pc.accept_phone(signal))

    def test_pair_signal_repeats_on_each_pc_login(self):
        pc, phone = self.relay.paired()
        pc.login()
        self.assertEqual(wait_server(pc, "pair")["phone_sign"], phone.keys.sign_public)

    def test_second_scan_is_claim_used_and_pc_sees_intrusion(self):
        pc, qr = self.relay.pending()
        first = self.relay.phone()
        first.scan(qr)
        second = self.relay.phone()
        with self.assertRaises(ClientError) as ctx:
            second.scan(qr)
        self.assertIn("409", str(ctx.exception))
        self.assertIn("claim_used", str(ctx.exception))
        wait_server(pc, "intrusion")

    def test_malformed_claim_does_not_burn(self):
        pc, qr = self.relay.pending()
        phone = self.relay.phone()
        keys = KeyPair()
        path = f"/v2/claims/{pc.pair}"
        good = {"phone_sign": keys.sign_public, "phone_agree": keys.agree_public, "tag": b64u(os.urandom(32))}
        http = pc.http
        off_curve = b64u(b"\x04" + os.urandom(64))
        bad = [
            ({**good, "phone_sign": off_curve}, "bad_key"),
            ({**good, "phone_agree": good["phone_agree"].replace("-", "+") + "="}, "bad_key"),
            ({**good, "extra": 1}, "bad_key"),
            ({"phone_sign": good["phone_sign"], "phone_agree": good["phone_agree"]}, "bad_key"),
            ({**good, "tag": b64u(os.urandom(31))}, "bad_tag"),
            ({**good, "tag": good["tag"] + "="}, "bad_tag"),
            ({**good, "tag": 5}, "bad_tag"),
        ]
        for body, code in bad:
            status, data = http.request("POST", path, body)
            self.assertEqual((status, data["error"]), (400, code), body)
        duplicate = '{"phone_sign":"%s","phone_sign":"%s","phone_agree":"%s","tag":"%s"}' % (
            good["phone_sign"], good["phone_sign"], good["phone_agree"], good["tag"]
        )
        self.assertEqual(http.request("POST", path, duplicate.encode())[0], 400)
        self.assertEqual(http.request("POST", path, b"not json")[1]["error"], "bad_json")
        phone.scan(qr)
        pc.await_phone(5)

    def test_claim_with_wrong_tag_burns_but_pc_rejects(self):
        pc, qr = self.relay.pending()
        keys = KeyPair()
        status, _ = pc.http.request(
            "POST", f"/v2/claims/{pc.pair}",
            {"phone_sign": keys.sign_public, "phone_agree": keys.agree_public, "tag": b64u(os.urandom(32))},
        )
        self.assertEqual(status, 200)
        with self.assertRaisesRegex(ClientError, "tag mismatch"):
            pc.await_phone(5)
        wait_eof(pc)
        status, data = self.relay.pc().http.request("POST", f"/v2/claims/{pc.pair}", {})
        self.assertEqual((status, data["error"]), (409, "claim_used"))

    def test_bad_pc_claims(self):
        http = self.relay.pc().http
        keys = KeyPair()
        off_curve = b64u(b"\x04" + os.urandom(64))
        for body in [{"pc_sign": keys.sign_public}, {"pc_sign": off_curve, "pc_agree": keys.agree_public}, {"pc_sign": 1, "pc_agree": 2}]:
            self.assertEqual(http.request("POST", "/v2/claims", body)[1]["error"], "bad_key")

    def test_claim_ttl_is_600_and_expired_claim_is_gone(self):
        pc, qr = self.relay.pending()
        status, data = pc.http.request("POST", "/v2/claims", {"pc_sign": pc.keys.sign_public, "pc_agree": pc.keys.agree_public})
        self.assertEqual((status, data["ttl"]), (201, 600))
        self.clock.advance(599)
        self.assertEqual(pc.state()[0], 200)
        self.clock.advance(2)
        phone = self.relay.phone()
        with self.assertRaises(ClientError) as ctx:
            phone.scan(qr)
        self.assertIn("404", str(ctx.exception))
        with self.assertRaises(LoginRefused) as ctx:
            pc.challenge()
        self.assertEqual(ctx.exception.status, 404)
        self.assertNotIn(pc.pair, self.relay.store.pairs)

    def test_unknown_pair_claim_is_404(self):
        self.assertEqual(self.relay.pc().http.request("POST", "/v2/claims/AAAAAAAAAAAAAAAAAAAAAA", {})[0], 404)

    def test_pending_pair_rejects_messages_and_phone_login(self):
        pc, qr = self.relay.pending()
        status, data = raw_signed(pc, "messages", b"{}")
        self.assertEqual((status, data["error"]), (409, "not_paired"))
        phone = self.relay.phone()
        phone.pair = pc.pair
        phone.http = pc.http
        status, data = pc.http.request("POST", f"/v2/pairs/{pc.pair}/challenge", {"from": "phone"})
        self.assertEqual(status, 404)

    def test_pending_pair_can_be_deleted_by_pc(self):
        pc, qr = self.relay.pending()
        self.assertEqual(pc.delete("user")[0], 200)
        wait_eof(pc)
        self.assertEqual(pc.http.request("POST", f"/v2/pairs/{pc.pair}/challenge", {"from": "pc"})[0], 404)
        self.assertEqual(pc.http.request("POST", f"/v2/claims/{pc.pair}", {})[0], 404)

    def test_claim_rate_limit(self):
        relay = Relay(tempfile.mkdtemp(), None, rate_claims_per_min=3)
        self.addCleanup(relay.close)
        pc = relay.pc()
        keys = KeyPair()
        body = {"pc_sign": keys.sign_public, "pc_agree": keys.agree_public}
        statuses = [pc.http.request("POST", "/v2/claims", body)[0] for _ in range(4)]
        self.assertEqual(statuses, [201, 201, 201, 429])

    def test_pending_cap(self):
        relay = Relay(tempfile.mkdtemp(), None, max_pending=2)
        self.addCleanup(relay.close)
        pc = relay.pc()
        keys = KeyPair()
        body = {"pc_sign": keys.sign_public, "pc_agree": keys.agree_public}
        self.assertEqual([pc.http.request("POST", "/v2/claims", body)[0] for _ in range(3)], [201, 201, 429])


class LoginTest(RelayCase):
    def challenge(self, peer):
        return peer.challenge()

    def login_raw(self, peer, challenge, key=None, side=None, sig=None):
        side = side or peer.side
        key = key or peer.keys.sign
        sig = sig or sign_p1363(key, login_string(peer.pair, side, challenge))
        conn = peer.http.connection()
        conn.request(
            "GET", f"/v2/pairs/{peer.pair}/stream",
            headers={"Pult-From": side, "Pult-Challenge": challenge, "Pult-Sig": sig},
        )
        resp = conn.getresponse()
        data = resp.read() if resp.status != 200 else None
        status = resp.status
        conn.close()
        return status, json.loads(data) if data else None

    def test_many_outstanding_challenges_all_valid_each_once(self):
        pc, phone = self.relay.paired()
        first, second = pc.challenge(), pc.challenge()
        self.assertNotEqual(first, second)
        self.assertEqual(self.login_raw(pc, second)[0], 200)
        self.assertEqual(self.login_raw(pc, first)[0], 200)
        status, data = self.login_raw(pc, second)
        self.assertEqual((status, data["error"]), (401, "bad_signature"))

    def test_challenge_is_one_time_even_after_bad_signature(self):
        pc, phone = self.relay.paired()
        challenge = pc.challenge()
        other = KeyPair()
        self.assertEqual(self.login_raw(pc, challenge, key=other.sign)[0], 401)
        self.assertEqual(self.login_raw(pc, challenge)[0], 401)

    def test_expired_challenge(self):
        pc, phone = self.relay.paired()
        challenge = pc.challenge()
        self.clock.advance(61)
        status, data = self.login_raw(pc, challenge)
        self.assertEqual((status, data["error"]), (401, "bad_signature"))

    def test_challenge_bound_to_side_and_pair(self):
        pc, phone = self.relay.paired()
        challenge = pc.challenge()
        status, _ = self.login_raw(phone, challenge)
        self.assertEqual(status, 401)
        other_pc, other_phone = self.relay.paired()
        status, _ = self.login_raw(other_pc, challenge)
        self.assertEqual(status, 401)
        forged = challenge[:-2] + ("AA" if challenge[-2:] != "AA" else "BB")
        self.assertEqual(self.login_raw(pc, forged)[0], 401)

    def test_foreign_signature_is_rejected_and_both_sides_see_intrusion(self):
        pc, phone = self.relay.paired()
        stranger = KeyPair()
        status, data = self.login_raw(pc, pc.challenge(), key=stranger.sign)
        self.assertEqual((status, data["error"]), (401, "bad_signature"))
        wait_server(pc, "intrusion")
        wait_server(phone, "intrusion")

    def test_phone_key_cannot_login_as_pc(self):
        pc, phone = self.relay.paired()
        status, _ = self.login_raw(pc, pc.challenge(), key=phone.keys.sign)
        self.assertEqual(status, 401)

    def test_intrusion_signal_at_most_once_a_minute(self):
        pc, phone = self.relay.paired()
        stranger = KeyPair()
        for _ in range(3):
            self.login_raw(pc, pc.challenge(), key=stranger.sign)
        wait_server(pc, "intrusion")
        self.assertEqual([e for e in drain(pc) if e[1] and e[1].get("type") == "intrusion"], [])
        self.clock.advance(61)
        self.login_raw(pc, pc.challenge(), key=stranger.sign)
        wait_server(pc, "intrusion")

    def test_missing_headers(self):
        pc, phone = self.relay.paired()
        conn = pc.http.connection()
        conn.request("GET", f"/v2/pairs/{pc.pair}/stream")
        resp = conn.getresponse()
        self.assertEqual((resp.status, json.loads(resp.read())["error"]), (401, "unauthorized"))
        conn.close()

    def test_unknown_pair_login_and_challenge(self):
        pc, phone = self.relay.paired()
        self.assertEqual(pc.http.request("POST", "/v2/pairs/AAAAAAAAAAAAAAAAAAAAAA/challenge", {"from": "pc"})[0], 404)
        self.assertEqual(pc.http.request("POST", f"/v2/pairs/{pc.pair}/challenge", {"from": "other"})[0], 400)
        self.assertEqual(pc.http.request("POST", f"/v2/pairs/{pc.pair}/challenge", {"from": "pc", "x": 1})[0], 400)

    def test_new_login_closes_previous_stream_and_invalidates_it(self):
        pc, phone = self.relay.paired()
        old = pc.stream_id
        pc.login()
        self.assertNotEqual(old, pc.stream_id)
        status, data = raw_signed(pc, "ping", stream=old)
        self.assertEqual((status, data["error"]), (401, "stale_stream"))
        self.assertEqual(pc.ping()[0], 200)

    def test_challenge_rate_limit(self):
        relay = Relay(tempfile.mkdtemp(), None, rate_challenges_per_min=2)
        self.addCleanup(relay.close)
        pc, qr = relay.pending()
        codes = [pc.http.request("POST", f"/v2/pairs/{pc.pair}/challenge", {"from": "pc"})[0] for _ in range(3)]
        self.assertEqual(codes, [200, 429, 429])


class SignedRequestTest(RelayCase):
    def test_replay_same_n_rejected(self):
        pc, phone = self.relay.paired()
        n = pc.n + 1
        self.assertEqual(raw_signed(pc, "ping", n=n)[0], 200)
        status, data = raw_signed(pc, "ping", n=n)
        self.assertEqual((status, data["error"]), (409, "replay"))

    def test_window_128(self):
        pc, phone = self.relay.paired()
        b = pc.n
        self.assertEqual(raw_signed(pc, "ping", n=b + 130)[0], 200)
        self.assertEqual(raw_signed(pc, "ping", n=b + 2)[0], 409)
        self.assertEqual(raw_signed(pc, "ping", n=b + 3)[0], 200)
        self.assertEqual(raw_signed(pc, "ping", n=b + 3)[0], 409)
        self.assertEqual(raw_signed(pc, "ping", n=b + 129)[0], 200)

    def test_replay_of_captured_request_on_new_stream_fails(self):
        pc, phone = self.relay.paired()
        old = pc.stream_id
        n = pc.n + 1
        self.assertEqual(raw_signed(pc, "ping", n=n)[0], 200)
        pc.login()
        status, data = raw_signed(pc, "ping", n=n, stream=old)
        self.assertEqual((status, data["error"]), (401, "stale_stream"))

    def test_bad_signature_does_not_consume_counter(self):
        pc, phone = self.relay.paired()
        stranger = KeyPair()
        status, data = raw_signed(pc, "ping", n=5, sign_key=stranger.sign)
        self.assertEqual((status, data["error"]), (401, "bad_signature"))
        self.assertEqual(raw_signed(pc, "ping", n=5)[0], 200)

    def test_signature_binds_op_pair_side_stream_n_and_body(self):
        pc, phone = self.relay.paired()
        self.assertEqual(raw_signed(pc, "ping", n=1, sign_op="state")[0], 401)
        self.assertEqual(raw_signed(pc, "acks", {"ids": ["a"]}, n=2, sign_op="delete")[0], 401)
        body = compact({"ids": ["a"]}).encode()
        other = compact({"ids": ["b"]}).encode()
        from tools.pult_testclient import post_string
        sig = sign_p1363(pc.keys.sign, post_string("acks", pc.pair, "pc", pc.stream_id, 3, body))
        status, _ = pc.http.request("POST", f"/v2/pairs/{pc.pair}/acks", other, {
            "Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": "3", "Pult-Sig": sig})
        self.assertEqual(status, 401)
        sig = sign_p1363(pc.keys.sign, post_string("ping", pc.pair, "pc", pc.stream_id, 4, b""))
        status, _ = pc.http.request("POST", f"/v2/pairs/{pc.pair}/ping", b"", {
            "Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": "5", "Pult-Sig": sig})
        self.assertEqual(status, 401)

    def test_phone_signature_with_pc_header_is_rejected(self):
        pc, phone = self.relay.paired()
        status, data = raw_signed(phone, "ping", headers={"Pult-From": "pc", "Pult-Stream": pc.stream_id})
        self.assertEqual(status, 401)

    def test_missing_headers_and_bad_counter(self):
        pc, phone = self.relay.paired()
        for name in ("Pult-From", "Pult-Stream", "Pult-N", "Pult-Sig"):
            status, data = raw_signed(pc, "ping", n=50, extra=[name])
            self.assertEqual((status, data["error"]), (401, "unauthorized"), name)
        for value in ("0", "01", "-1", "abc", "9007199254740992", "1.0"):
            status, data = raw_signed(pc, "ping", headers={"Pult-N": value})
            self.assertEqual((status, data["error"]), (400, "bad_header"), value)
        self.assertEqual(raw_signed(pc, "ping", n=9007199254740991)[0], 200)

    def test_unknown_stream_and_unknown_pair(self):
        pc, phone = self.relay.paired()
        status, data = raw_signed(pc, "ping", stream="AAAAAAAAAAAAAAAAAAAAAA")
        self.assertEqual((status, data["error"]), (401, "stale_stream"))
        pc.pair = "AAAAAAAAAAAAAAAAAAAAAA"
        self.assertEqual(raw_signed(pc, "ping")[0], 404)

    def test_state_get_is_signed(self):
        pc, phone = self.relay.paired()
        status, data = raw_signed(pc, "state", method="GET")
        self.assertEqual(status, 200)
        self.assertEqual(data["pc_stream"], "open")
        self.assertEqual(raw_signed(pc, "state", method="GET", sign_op="ping")[0], 401)

    def test_stale_stream_is_recoverable_by_client(self):
        pc, phone = self.relay.paired()
        other = self.relay.pc()
        other.__dict__.update({k: v for k, v in pc.__dict__.items() if k not in ("sse",)})
        other.login()
        status, data, env = pc.send({"type": "status", "thread": "t", "status": "done"}, ttl=0)
        self.assertEqual(status, 202)


class MessagesTest(RelayCase):
    def test_queue_replay_ack_and_dup(self):
        pc, phone = self.relay.paired()
        phone.close()
        status, data, env1 = pc.send({"type": "text_request", "request_id": "a"})
        self.assertEqual((status, data["dup"]), (202, False))
        status, data, env2 = pc.send({"type": "text_request", "request_id": "b"})
        self.assertEqual(pc.send({"type": "x"}, mid=env1["id"])[1], {"seq": None, "dup": True})
        phone.login()
        ids = []
        for _ in range(2):
            kind, body = phone.receive(5, ack=False)
            ids.append(body["request_id"])
        self.assertEqual(ids, ["a", "b"])
        self.assertEqual(pc.state()[1]["queued_to_phone"], 2)
        status, data = phone.ack([env1["id"]])
        self.assertEqual(data, {"removed": 1})
        self.assertEqual(pc.state()[1]["queued_to_phone"], 1)

    def test_ack_cannot_remove_other_sides_messages(self):
        pc, phone = self.relay.paired()
        phone.close()
        status, data, env = pc.send({"type": "text_request", "request_id": "a"})
        self.assertEqual(pc.ack([env["id"]])[1], {"removed": 0})
        self.assertEqual(pc.state()[1]["queued_to_phone"], 1)

    def test_bad_acks(self):
        pc, phone = self.relay.paired()
        for body in ({"ids": "a"}, {"ids": [1]}, {"ids": ["a b"]}, {"ids": [], "x": 1}, []):
            status, data = pc.signed("acks", body)
            self.assertEqual((status, data["error"]), (400, "bad_ack"), body)

    def test_zero_ttl_is_not_queued_and_dropped_without_stream(self):
        pc, phone = self.relay.paired()
        phone.close()
        pc.send({"type": "status", "thread": "t", "status": "working"}, ttl=0)
        self.assertEqual(pc.state()[1]["queued_to_phone"], 0)

    def test_order_by_seq_across_ttl_paths(self):
        pc, phone = self.relay.paired()
        seqs, ids = [], []
        for ttl in (60, 0, 60):
            status, data, env = pc.send({"type": "status", "thread": "t", "status": "working"}, ttl=ttl)
            seqs.append(data["seq"])
            ids.append(env["id"])
        self.assertEqual(seqs, sorted(seqs))
        got = []
        while len(got) < 3:
            event, data = phone.next_event(5)
            if event == "message":
                got.append(data["id"])
        self.assertEqual(got, ids)

    def test_ttl_is_clamped_for_queue_but_envelope_is_unchanged(self):
        pc, phone = self.relay.paired()
        phone.close()
        status, data, env = pc.send({"type": "text_request", "request_id": "a"}, ttl=100000)
        self.assertEqual(status, 202)
        self.clock.advance(901)
        self.assertEqual(pc.state()[1]["queued_to_phone"], 0)
        status, data, env = pc.send({"type": "text_request", "request_id": "b"}, ttl=100000)
        phone.login()
        event, delivered = phone.next_event(5)
        while event != "message":
            event, delivered = phone.next_event(5)
        self.assertEqual(delivered["ttl"], 100000)
        self.assertEqual(delivered, env)

    def test_unsupported_version_signals_pc_only_from_phone(self):
        pc, phone = self.relay.paired()
        env = {"v": 1, "pair": pc.pair, "from": "phone", "id": "x1", "ts": 0, "ttl": 0, "inner": {"type": "t"}}
        status, data = phone.signed("messages", compact(env).encode())
        self.assertEqual((status, data), (400, {"error": "unsupported_version", "protocol": [2]}))
        self.assertEqual(wait_server(pc, "phone_incompatible"), {"type": "phone_incompatible", "v": 1})
        env = {**env, "from": "pc", "id": "x2"}
        status, data = pc.signed("messages", compact(env).encode())
        self.assertEqual(data["error"], "unsupported_version")
        self.assertEqual([e for e in drain(pc) if e[1] and e[1].get("type") == "phone_incompatible"], [])

    def test_envelope_mismatch_and_garbage(self):
        pc, phone = self.relay.paired()
        status, data, env = pc.send({"type": "status", "thread": "t", "status": "x"}, ttl=0)
        for change in ({"from": "phone"}, {"pair": "other"}, {"extra": 1}, {"ttl": 1.0}, {"ct": "AAAA"}, {"id": "a\nb"}):
            bad = {**env, "id": "n-" + str(len(str(change))), **change}
            status, data = pc.signed("messages", compact(bad).encode())
            self.assertEqual((status, data["error"]), (400, "bad_envelope"), change)
        status, data = pc.signed("messages", b"{nope")
        self.assertEqual((status, data["error"]), (400, "bad_json"))
        status, data = pc.signed("messages", b'{"ttl":1,"ttl":2}')
        self.assertEqual(data["error"], "bad_envelope")
        status, data = pc.signed("messages", b"[]")
        self.assertEqual(data["error"], "bad_envelope")

    def test_signature_checked_before_body_parsing(self):
        pc, phone = self.relay.paired()
        stranger = KeyPair()
        status, data = raw_signed(pc, "messages", b"{garbage", sign_key=stranger.sign)
        self.assertEqual((status, data["error"]), (401, "bad_signature"))

    def test_length_required_too_large_and_chunked(self):
        pc, phone = self.relay.paired()
        status, data, env = pc.send({"type": "status", "thread": "t", "status": "x"}, ttl=0)
        conn = pc.http.connection()
        pc.n += 1
        from tools.pult_testclient import post_string
        body = compact({**env, "id": "chunked-1"}).encode()
        sig = sign_p1363(pc.keys.sign, post_string("messages", pc.pair, "pc", pc.stream_id, pc.n, body))
        headers = {"Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": str(pc.n), "Pult-Sig": sig}
        conn.request("POST", f"/v2/pairs/{pc.pair}/messages", body=iter([body[:20], body[20:]]), headers=headers, encode_chunked=True)
        resp = conn.getresponse()
        conn.close()
        self.assertIn(resp.status, (202, 400))
        big = b"x" * 70000
        pc.n += 1
        sig = sign_p1363(pc.keys.sign, post_string("messages", pc.pair, "pc", pc.stream_id, pc.n, big))
        headers = {"Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": str(pc.n), "Pult-Sig": sig}
        conn = pc.http.connection()
        conn.request("POST", f"/v2/pairs/{pc.pair}/messages", body=iter([big[:40000], big[40000:]]), headers=headers, encode_chunked=True)
        resp = conn.getresponse()
        self.assertEqual((resp.status, json.loads(resp.read())["error"]), (413, "too_large"))
        conn.close()
        pc.n += 1
        conn = pc.http.connection()
        conn.putrequest("POST", f"/v2/pairs/{pc.pair}/messages")
        for k, v in {"Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": str(pc.n), "Pult-Sig": "x"}.items():
            conn.putheader(k, v)
        conn.endheaders()
        resp = conn.getresponse()
        self.assertEqual((resp.status, json.loads(resp.read())["error"]), (411, "length_required"))
        conn.close()
        pc.n += 1
        status, data = pc.signed("messages", big)
        self.assertEqual((status, data["error"]), (413, "too_large"))

    def test_chunked_valid_message_accepted(self):
        pc, phone = self.relay.paired()
        from tools.pult_testclient import post_string, seal
        env = seal(pc.dir_keys["pc"], pc.pair, "pc", "chunk-ok", 0, {"type": "status", "thread": "t", "status": "x"})
        body = compact(env).encode()
        pc.n += 1
        sig = sign_p1363(pc.keys.sign, post_string("messages", pc.pair, "pc", pc.stream_id, pc.n, body))
        headers = {"Pult-From": "pc", "Pult-Stream": pc.stream_id, "Pult-N": str(pc.n), "Pult-Sig": sig}
        conn = pc.http.connection()
        conn.request("POST", f"/v2/pairs/{pc.pair}/messages", body=iter([body[:10], body[10:50], body[50:]]), headers=headers, encode_chunked=True)
        resp = conn.getresponse()
        self.assertEqual(resp.status, 202)
        conn.close()
        self.assertEqual(phone.receive(5)[1]["status"], "x")


class LimitsTest(RelayCase):
    config = {"max_queue_per_pair": 3, "max_queue_bytes_per_pair": 3000}

    def test_queue_full_and_bytes_full(self):
        pc, phone = self.relay.paired()
        phone.close()
        for i in range(3):
            self.assertEqual(pc.send({"type": "t", "i": i}, ttl=60)[0], 202)
        status, data, env = pc.send({"type": "t", "i": 9}, ttl=60)
        self.assertEqual((status, data["error"]), (429, "queue_full"))
        self.assertEqual(pc.send({"type": "t", "i": 9}, ttl=0)[0], 202)

    def test_queue_bytes(self):
        pc, phone = self.relay.paired()
        phone.close()
        big = {"type": "t", "pad": "x" * 900}
        self.assertEqual(pc.send(big, ttl=60)[0], 202)
        self.assertEqual(pc.send(big, ttl=60)[0], 202)
        status, data, env = pc.send(big, ttl=60)
        self.assertEqual((status, data["error"]), (507, "queue_bytes_full"))


class DeleteTest(RelayCase):
    def test_delete_notifies_both_and_login_gets_410(self):
        pc, phone = self.relay.paired()
        self.assertEqual(phone.delete("user")[0], 200)
        self.assertEqual(wait_server(pc, "pair_deleted"), {"type": "pair_deleted", "reason": "user", "by": "phone"})
        wait_eof(pc)
        wait_eof(phone)
        challenge = pc.challenge()
        conn = pc.http.connection()
        sig = sign_p1363(pc.keys.sign, login_string(pc.pair, "pc", challenge))
        conn.request("GET", f"/v2/pairs/{pc.pair}/stream", headers={"Pult-From": "pc", "Pult-Challenge": challenge, "Pult-Sig": sig})
        resp = conn.getresponse()
        self.assertEqual((resp.status, json.loads(resp.read())), (410, {"error": "pair_deleted", "reason": "user", "by": "phone"}))
        conn.close()
        stranger = KeyPair()
        challenge = pc.challenge()
        conn = pc.http.connection()
        sig = sign_p1363(stranger.sign, login_string(pc.pair, "pc", challenge))
        conn.request("GET", f"/v2/pairs/{pc.pair}/stream", headers={"Pult-From": "pc", "Pult-Challenge": challenge, "Pult-Sig": sig})
        resp = conn.getresponse()
        body = json.loads(resp.read())
        self.assertEqual((resp.status, body), (401, {"error": "bad_signature"}))
        conn.close()

    def test_reasons(self):
        pc, phone = self.relay.paired()
        for reason in ("pc_offline", "other", "", None, 3):
            status, data = pc.signed("delete", {"reason": reason})
            self.assertEqual((status, data["error"]), (400, "bad_reason"), reason)
        status, data = pc.signed("delete", {"reason": "user", "x": 1})
        self.assertEqual(data["error"], "bad_reason")

    def test_deleted_record_expires(self):
        pc, phone = self.relay.paired()
        pc.delete("idle")
        self.clock.advance(604801)
        with self.assertRaises(LoginRefused) as ctx:
            pc.challenge()
        self.assertEqual(ctx.exception.status, 404)

    def test_requests_to_deleted_pair_need_new_login(self):
        pc, phone = self.relay.paired()
        phone.delete("user")
        status, data = raw_signed(pc, "ping")
        self.assertEqual((status, data["error"]), (401, "stale_stream"))

    def test_pc_offline_five_minutes(self):
        pc, phone = self.relay.paired()
        pc.close()
        wait_until(lambda: (pc.pair, "pc") not in self.relay.core.streams)
        self.clock.advance(100)
        phone.ping()
        self.relay.core.reap()
        self.assertEqual(self.relay.store.pairs[pc.pair]["status"], "active")
        self.clock.advance(250)
        phone.ping()
        self.relay.core.reap()
        self.assertEqual(wait_server(phone, "pair_deleted"), {"type": "pair_deleted", "reason": "pc_offline", "by": "server"})

    def test_pc_reconnect_resets_offline_timer(self):
        pc, phone = self.relay.paired()
        pc.close()
        wait_until(lambda: (pc.pair, "pc") not in self.relay.core.streams)
        self.clock.advance(290)
        self.relay.core.reap()
        pc.login()
        self.clock.advance(290)
        self.relay.core.reap()
        self.assertEqual(self.relay.store.pairs[pc.pair]["status"], "active")


class LivenessTest(RelayCase):
    def test_phone_stream_signal_and_liveness(self):
        pc, phone = self.relay.paired()
        self.assertEqual(pc.state()[1]["phone_stream"], "open")
        self.clock.advance(30)
        phone.ping()
        pc.ping()
        self.clock.advance(30)
        self.relay.core.reap()
        self.assertEqual(self.relay.core.phone_state(pc.pair), "open")
        self.clock.advance(46)
        pc.ping()
        self.relay.core.reap()
        self.assertEqual(wait_server(pc, "phone_stream")["state"], "closed")
        self.assertEqual(self.relay.core.phone_state(pc.pair), "closed")

    def test_pc_gets_state_on_login_and_open_signal(self):
        pc, qr = self.relay.pending()
        events = drain(pc)
        self.assertIn(("server", {"type": "phone_stream", "state": "closed"}), events)
        phone = self.relay.phone()
        phone.scan(qr)
        pc.await_phone(5)
        self.assertEqual(wait_server(pc, "phone_stream")["state"], "open")


class RestartTest(RelayCase):
    clock_fake = False

    def test_restart_keeps_pairs_and_queue_and_invalidates_streams(self):
        pc, phone = self.relay.paired()
        phone.close()
        status, data, env = pc.send({"type": "text_request", "request_id": "a"})
        first_seq = data["seq"]
        old_pc_stream = pc.stream_id
        self.relay.stop()
        self.relay.start()
        for peer in (pc, phone):
            peer.http.port = self.relay.port
        status, data = raw_signed(pc, "ping", stream=old_pc_stream)
        self.assertEqual((status, data["error"]), (401, "stale_stream"))
        pc.login()
        phone.login()
        kind, body = phone.receive(5)
        self.assertEqual(body["request_id"], "a")
        self.assertEqual(pc.send({"type": "text_request", "request_id": "b"})[1]["seq"], first_seq + 1)
        self.assertEqual(pc.send({"type": "t"}, mid=env["id"])[1], {"seq": None, "dup": True})
        status, data, env2 = phone.send({"type": "permission_response", "request_id": "a", "decision": "allow", "reason": ""})
        self.assertEqual(status, 202)

    def test_restart_keeps_pending_claim_and_deleted_reason(self):
        pc, qr = self.relay.pending()
        paired_pc, paired_phone = self.relay.paired()
        paired_pc.delete("replaced")
        self.relay.stop()
        self.relay.start()
        for peer in (pc, paired_pc, paired_phone):
            peer.http.port = self.relay.port
        phone = self.relay.phone()
        phone.scan(qr)
        pc.login()
        pc.await_phone(5)
        with self.assertRaises(LoginRefused) as ctx:
            paired_phone.login()
        self.assertEqual((ctx.exception.status, ctx.exception.body["reason"]), (410, "replaced"))

    def test_challenges_do_not_survive_restart(self):
        pc, phone = self.relay.paired()
        challenge = pc.challenge()
        self.relay.stop()
        self.relay.start()
        pc.http.port = self.relay.port
        conn = pc.http.connection()
        sig = sign_p1363(pc.keys.sign, login_string(pc.pair, "pc", challenge))
        conn.request("GET", f"/v2/pairs/{pc.pair}/stream", headers={"Pult-From": "pc", "Pult-Challenge": challenge, "Pult-Sig": sig})
        self.assertEqual(conn.getresponse().status, 401)
        conn.close()

    def test_old_format_state_is_set_aside(self):
        self.relay.stop()
        path = os.path.join(self.tmp.name, "state.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"p1": {"seq": 3, "queue": [], "seen": []}}, f)
        self.relay.start()
        self.assertEqual(self.relay.store.pairs, {})
        self.assertTrue(os.path.exists(path + ".old"))

    def test_corrupt_state_is_set_aside(self):
        self.relay.stop()
        path = os.path.join(self.tmp.name, "state.json")
        with open(path, "w", encoding="utf-8") as f:
            f.write("{broken")
        self.relay.start()
        self.assertEqual(self.relay.store.pairs, {})
        self.assertTrue(os.path.exists(path + ".corrupt"))



class PortBindingTest(RelayCase):
    @unittest.skipUnless(os.name == "nt", "исключительная привязка — только Windows")
    def test_second_process_cannot_take_busy_port_on_windows(self):
        import socket
        intruder = socket.socket()
        self.addCleanup(intruder.close)
        intruder.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        with self.assertRaises(OSError):
            intruder.bind(("127.0.0.1", self.relay.port))
            intruder.listen(1)

    def test_second_relay_on_busy_port_fails(self):
        from pult_relay.app import create_server
        import dataclasses
        config = dataclasses.replace(self.relay.config, port=self.relay.port)
        core = type(self.relay.core)(self.relay.store, config, self.clock)
        with self.assertRaises(OSError):
            create_server(core)


if __name__ == "__main__":
    unittest.main()
