import dataclasses
import os
import tempfile
import time
import unittest
from unittest import mock

from pult_relay.config import Config
from pult_relay.core import CLOSE, Core, RelayError, message_size
from pult_relay.store import Store
from tests.helpers import Relay, drain, wait_until
from tools.pult_testclient import compact
from tests.test_limits import claim_body, connect


class MoreCase(unittest.TestCase):
    def relay(self, **config):
        relay = Relay(tempfile.mkdtemp(), None, **config)
        self.addCleanup(relay.close)
        return relay


class ClosedStreamTest(MoreCase):
    def test_messages_queued_on_a_closed_stream_are_not_written(self):
        relay = self.relay()
        pc, phone = relay.paired()
        stream = relay.core.streams[(phone.pair, "phone")]
        env = {"v": 2, "pair": phone.pair, "from": "pc", "id": "late", "ts": 0, "ttl": 0, "ct": "A" * 40}
        with relay.core.lock:
            self.assertTrue(stream.reserve(message_size(env), relay.core.backlog_limit))
            stream.closed = True
            del relay.core.streams[(phone.pair, "phone")]
            stream.outbox.put(("message", 99, env))
            stream.outbox.put(CLOSE)
        events = []
        deadline = time.time() + 5
        while time.time() < deadline:
            kind, data = phone.next_event(5)
            events.append((kind, data))
            if kind == "eof":
                break
        self.assertNotIn("message", [k for k, _ in events])
        wait_until(lambda: stream.backlog == 0)

    def test_deleted_pair_marks_streams_closed(self):
        relay = self.relay()
        pc, phone = relay.paired()
        stream = relay.core.streams[(phone.pair, "phone")]
        self.assertEqual(pc.delete("user")[0], 200)
        self.assertTrue(stream.closed)


class AddressEntryTest(MoreCase):
    def test_address_entry_disappears_when_its_connections_end(self):
        relay = self.relay()
        pc, phone = relay.paired()
        pc.close()
        phone.close()
        wait_until(lambda: "127.0.0.1" not in relay.core.addresses)

    def test_failed_error_response_still_leaves_the_address(self):
        relay = self.relay()
        handler = relay.server.RequestHandlerClass
        original = handler._json

        def broken(self, status, payload):
            if status >= 400:
                raise OSError("client gone")
            return original(self, status, payload)

        handler._json = broken
        self.addCleanup(setattr, handler, "_json", original)
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        sock.sendall(b"GET /nowhere HTTP/1.1\r\nHost: x\r\n\r\n")
        wait_until(lambda: "127.0.0.1" not in relay.core.addresses)

    def test_address_budget_is_half_of_the_total(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, max_total_queue_bytes=10000))
        core.enter("a")
        self.assertEqual(core.addresses["a"].budget.limit, 5000)
        core.leave("a")
        self.assertNotIn("a", core.addresses)

    def test_enter_refuses_over_the_limit(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, max_connections_per_addr=1))
        core.enter("a")
        with self.assertRaises(RelayError) as refused:
            core.enter("a")
        self.assertEqual(refused.exception.status, 429)
        core.enter("b")


class PersistenceTest(MoreCase):
    def test_zero_ttl_message_does_not_write_state(self):
        relay = self.relay()
        pc, phone = relay.paired()
        saves = []
        original = relay.core.store.save_state
        relay.core.store.save_state = lambda: (saves.append(1), original())
        self.assertEqual(pc.send({"type": "status", "thread": "t", "status": "working"}, ttl=0)[0], 202)
        self.assertEqual(saves, [])
        self.assertEqual(pc.send({"type": "t"}, ttl=60)[0], 202)
        self.assertEqual(saves, [1])


class OverheadTest(MoreCase):
    def test_small_messages_are_counted_with_overhead(self):
        relay = self.relay()
        pc, phone = relay.paired()
        phone.close()
        relay.core.claim_addr.clear()
        self.assertEqual(pc.send({"type": "t"}, ttl=60)[0], 202)
        size = relay.core.store.pairs[pc.pair]["queue"][-1]["size"]
        relay.core.config = dataclasses.replace(relay.core.config, max_total_queue_bytes=6 * (size + 1024) + 15)
        self.assertEqual(pc.send({"type": "t"}, ttl=60)[0], 202)
        status, data, _ = pc.send({"type": "t"}, ttl=60)
        self.assertEqual((status, data["error"]), (507, "queue_bytes_full"))
        self.assertLess(3 * size, relay.core.config.max_total_queue_bytes // 3)


class TrailerTest(MoreCase):
    def test_chunked_body_with_trailers_is_read_to_the_end(self):
        relay = self.relay()
        pc = relay.pc()
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        body = (b'{"pc_sign":"x"}')
        chunk = b"%x\r\n" % len(body) + body + b"\r\n0\r\nX-T: 1\r\nX-U: 2\r\n\r\n"
        sock.sendall(b"POST /v2/claims HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n" + chunk)
        sock.settimeout(5)
        reply = b""
        while b"\r\n\r\n" not in reply:
            reply += sock.recv(4096)
        self.assertIn(b" 400 ", reply.split(b"\r\n", 1)[0] + b" ")
        self.assertEqual(pc.http.request("GET", "/info")[0], 200)


class SignalThrottleTest(MoreCase):
    def test_phone_incompatible_is_sent_at_most_once_a_minute(self):
        relay = self.relay()
        pc, phone = relay.paired()
        for i in range(5):
            env = {"v": 1, "pair": pc.pair, "from": "phone", "id": f"x{i}", "ts": 0, "ttl": 0, "inner": {"type": "t"}}
            self.assertEqual(phone.signed("messages", compact(env).encode())[0], 400)
        signals = [e for e in drain(pc, 0.5) if e[0] == "server" and e[1].get("type") == "phone_incompatible"]
        self.assertEqual(len(signals), 1)


class RestartAddressTest(MoreCase):
    def test_pc_login_after_restart_restores_the_pair_address(self):
        relay = self.relay(max_pairs_per_addr=1)
        pc, phone = relay.paired()
        relay.stop()
        relay.start()
        self.assertEqual(relay.core.claim_addr, {})
        pc.login()
        self.assertEqual(relay.core.claim_addr.get(pc.pair), "127.0.0.1")
        self.assertEqual(relay.pc().http.request("POST", "/v2/claims", claim_body())[0], 429)


class SeenWindowTest(MoreCase):
    def test_seen_keeps_the_latest_ids(self):
        relay = self.relay(seen_ids_per_pair=3, max_queue_per_pair=1)
        pc, phone = relay.paired()
        phone.close()
        for mid in ("a", "b", "c", "d"):
            self.assertFalse(pc.send({"type": "t"}, ttl=0, mid=mid)[1]["dup"])
        self.assertEqual(relay.core.store.pairs[pc.pair]["seen"], ["b", "c", "d"])
        self.assertTrue(pc.send({"type": "t"}, ttl=0, mid="d")[1]["dup"])
        self.assertFalse(pc.send({"type": "t"}, ttl=0, mid="a")[1]["dup"])

    def test_seen_window_covers_both_queues(self):
        relay = self.relay(seen_ids_per_pair=3, max_queue_per_pair=4)
        pc, phone = relay.paired()
        phone.close()
        for i in range(10):
            pc.send({"type": "t"}, ttl=0, mid=f"m{i}")
        self.assertEqual(len(relay.core.store.pairs[pc.pair]["seen"]), 8)


class SignalBookkeepingTest(unittest.TestCase):
    def core(self):
        tmp = tempfile.mkdtemp()
        return Core(Store(tmp), Config(data_dir=tmp))

    def pending(self, core, pair_id):
        core.store.pairs[pair_id] = {"status": "pending", "created": time.time(), "queue": [], "seen": [],
                                     "phone_sign": None, "phone_agree": None, "tag": None}

    def test_signal_kinds_are_throttled_separately(self):
        core = self.core()
        self.assertFalse(core._throttled("intrusion", "p"))
        self.assertFalse(core._throttled("phone_incompatible", "p"))
        self.assertTrue(core._throttled("intrusion", "p"))

    def test_deleting_or_expiring_a_pair_forgets_its_signals(self):
        core = self.core()
        for pair_id in ("p", "q"):
            self.pending(core, pair_id)
            core._throttled("intrusion", pair_id)
            core._throttled("phone_incompatible", pair_id)
        core.delete_pair("p", "user", "pc")
        core._remove_pair("q")
        self.assertEqual(core.last_intrusion, {})

    def test_pc_login_keeps_the_first_address_and_phone_sets_none(self):
        core = self.core()
        self.pending(core, "p")
        rec = core.store.pairs["p"]
        core._open_stream("p", "phone", rec, "10.0.0.3")
        self.assertNotIn("p", core.claim_addr)
        core._open_stream("p", "pc", rec, "10.0.0.1")
        core._open_stream("p", "pc", rec, "10.0.0.2")
        self.assertEqual(core.claim_addr["p"], "10.0.0.1")


class IncompatibleOrderTest(MoreCase):
    def test_pc_old_version_does_not_spend_the_phone_signal(self):
        relay = self.relay()
        pc, phone = relay.paired()
        for side, peer in (("pc", pc), ("phone", phone)):
            env = {"v": 1, "pair": pc.pair, "from": side, "id": f"o-{side}", "ts": 0, "ttl": 0, "inner": {"type": "t"}}
            self.assertEqual(peer.signed("messages", compact(env).encode())[0], 400)
        signals = [e for e in drain(pc, 0.5) if e[0] == "server" and e[1].get("type") == "phone_incompatible"]
        self.assertEqual(len(signals), 1)


class DefaultsTest(unittest.TestCase):
    def test_defaults_fit_each_other(self):
        config = Config()
        self.assertEqual((config.max_pairs_per_addr, config.seen_ids_per_pair, config.max_connections_per_addr), (6, 512, 16))
        self.assertLessEqual(2 * config.max_pairs_per_addr, config.max_connections_per_addr)
        self.assertGreaterEqual(config.seen_ids_per_pair, 2 * config.max_queue_per_pair)


class OwnerlessQueueTest(MoreCase):
    def test_pairs_without_address_share_one_third(self):
        relay = self.relay(max_total_queue_bytes=15000)
        big = {"type": "t", "pad": "x" * 900}
        pc, phone = relay.paired()
        phone.close()
        relay.core.claim_addr.clear()
        self.assertEqual(pc.send(big, ttl=60)[0], 202)
        self.assertEqual(pc.send(big, ttl=60)[0], 202)
        self.assertEqual(pc.send(big, ttl=60)[0], 507)


class WriteRateTest(MoreCase):
    def test_queue_writes_and_acks_are_rate_limited_per_address(self):
        relay = self.relay(rate_writes_per_min=5)
        pc, phone = relay.paired()
        statuses = [pc.send({"type": "t"}, ttl=60)[0] for _ in range(6)]
        self.assertIn(429, statuses)
        self.assertEqual(statuses[: statuses.index(429)], [202] * statuses.index(429))
        self.assertEqual(pc.send({"type": "t"}, ttl=0)[0], 202)
        self.assertEqual(pc.state()[0], 200)


class SignalBacklogTest(unittest.TestCase):
    def test_unread_stream_is_closed_instead_of_piling_up_signals(self):
        from pult_relay.core import SIGNAL_BACKLOG, Stream
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp))
        core.store.pairs["p"] = {"status": "active", "queue": []}
        stream = Stream("pc", "s", 0, 128)
        core.streams[("p", "pc")] = stream
        for _ in range(SIGNAL_BACKLOG):
            core._send("p", "pc", {"type": "phone_stream", "state": "open"})
        self.assertFalse(stream.closed)
        self.assertIs(core.streams[("p", "pc")], stream)
        for _ in range(5):
            core._send("p", "pc", {"type": "phone_stream", "state": "open"})
        self.assertTrue(stream.closed)
        self.assertNotIn(("p", "pc"), core.streams)
        self.assertLessEqual(stream.outbox.qsize(), SIGNAL_BACKLOG + 1)


class EnvTest(unittest.TestCase):
    def test_new_limits_are_read_from_environment(self):
        env = {
            "PULT_MAX_CONNECTIONS": "7",
            "PULT_MAX_CONNECTIONS_PER_ADDR": "3",
            "PULT_MAX_PAIRS": "9",
            "PULT_MAX_PAIRS_PER_ADDR": "2",
            "PULT_MAX_TOTAL_QUEUE_BYTES": "1234",
            "PULT_REQUEST_TIMEOUT": "4",
            "PULT_WRITE_TIMEOUT": "5",
            "PULT_TRUST_PROXY": "1",
        }
        with mock.patch.dict(os.environ, env):
            config = Config.from_env()
        self.assertEqual(
            (config.max_connections, config.max_connections_per_addr, config.max_pairs, config.max_pairs_per_addr,
             config.max_total_queue_bytes, config.request_timeout, config.write_timeout, config.trust_proxy),
            (7, 3, 9, 2, 1234, 4.0, 5.0, True),
        )


if __name__ == "__main__":
    unittest.main()
