import os
import socket
import tempfile
import time
import unittest

from pult_relay.config import Config
from pult_relay.core import CLOSE, Budget, Core, Stream
from pult_relay.store import Store
from tests.helpers import Relay, wait_until
from tools.pult_testclient import KeyPair


def claim_body():
    keys = KeyPair()
    return {"pc_sign": keys.sign_public, "pc_agree": keys.agree_public}


def connect(port):
    sock = socket.create_connection(("127.0.0.1", port), timeout=5)
    return sock


def closed_by_server(sock, timeout=5):
    sock.settimeout(timeout)
    try:
        while True:
            chunk = sock.recv(4096)
            if not chunk:
                return True
    except (ConnectionResetError, ConnectionAbortedError):
        return True
    except socket.timeout:
        return False


class LimitsCase(unittest.TestCase):
    def relay(self, **config):
        relay = Relay(tempfile.mkdtemp(), None, **config)
        self.addCleanup(relay.close)
        return relay


class SlowClientTest(LimitsCase):
    def test_unfinished_body_is_dropped_after_request_timeout(self):
        relay = self.relay(request_timeout=0.5)
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        sock.sendall(b"POST /v2/claims HTTP/1.1\r\nHost: x\r\nContent-Length: 100\r\n\r\n{")
        started = time.time()
        self.assertTrue(closed_by_server(sock))
        self.assertLess(time.time() - started, 4)

    def test_body_dripping_within_idle_timeout_is_cut_by_total_deadline(self):
        relay = self.relay(request_timeout=1.0)
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        sock.sendall(b"POST /v2/claims HTTP/1.1\r\nHost: x\r\nContent-Length: 100\r\n\r\n")
        started = time.time()
        closed = False
        while time.time() - started < 5:
            try:
                sock.sendall(b" ")
            except OSError:
                closed = True
                break
            sock.settimeout(0.3)
            try:
                if sock.recv(4096) == b"":
                    closed = True
                    break
            except socket.timeout:
                pass
        self.assertTrue(closed)
        self.assertLess(time.time() - started, 4)

    def test_chunked_body_dripping_is_cut_by_total_deadline(self):
        relay = self.relay(request_timeout=1.0)
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        sock.sendall(b"POST /v2/claims HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n")
        started = time.time()
        closed = False
        while time.time() - started < 5:
            try:
                sock.sendall(b"1\r\n \r\n")
            except OSError:
                closed = True
                break
            sock.settimeout(0.3)
            try:
                if sock.recv(4096) == b"":
                    closed = True
                    break
            except socket.timeout:
                pass
        self.assertTrue(closed)
        self.assertLess(time.time() - started, 4)

    def test_connections_per_address_count_streams_and_requests(self):
        relay = self.relay(max_connections_per_addr=3)
        pc, phone = relay.paired()
        self.assertEqual(pc.http.request("GET", "/info")[0], 200)
        other = relay.pc()
        other.claim()
        other.login()
        self.assertEqual(pc.http.request("GET", "/info")[0], 429)
        other.close()
        wait_until(lambda: relay.core.addresses["127.0.0.1"].active <= 2)
        self.assertEqual(pc.http.request("GET", "/info")[0], 200)

    def test_connections_over_cap_are_closed_and_slots_come_back(self):
        relay = self.relay(max_connections=2, request_timeout=30)
        idle = [connect(relay.port) for _ in range(2)]
        for sock in idle:
            self.addCleanup(sock.close)
        wait_until(lambda: relay.server.slots._value == 0)
        extra = connect(relay.port)
        self.addCleanup(extra.close)
        self.assertTrue(closed_by_server(extra))
        idle[0].close()
        wait_until(lambda: relay.server.slots._value >= 1)
        pc = relay.pc()
        self.assertEqual(pc.http.request("GET", "/info")[0], 200)


class ClaimLimitsTest(LimitsCase):
    def test_pending_claims_per_address(self):
        relay = self.relay(max_pairs_per_addr=2)
        pc = relay.pc()
        statuses = [pc.http.request("POST", "/v2/claims", claim_body())[0] for _ in range(3)]
        self.assertEqual(statuses, [201, 201, 429])
        pair = next(iter(relay.core.store.pairs))
        relay.core.store.pairs[pair]["created"] -= relay.config.claim_ttl + 1
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 201)

    def test_active_pair_keeps_address_slot_until_deleted(self):
        relay = self.relay(max_pairs_per_addr=1)
        pc, phone = relay.paired()
        other = relay.pc()
        self.assertEqual(other.http.request("POST", "/v2/claims", claim_body())[0], 429)
        self.assertEqual(phone.delete("user")[0], 200)
        self.assertEqual(other.http.request("POST", "/v2/claims", claim_body())[0], 201)

    def test_pair_cap_evicts_oldest_deleted_then_refuses(self):
        relay = self.relay(max_pairs=2)
        first, phone = relay.paired()
        self.assertEqual(phone.delete("user")[0], 200)
        pc = relay.pc()
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 201)
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 201)
        self.assertNotIn(first.pair, relay.core.store.pairs)
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 429)


class QueueLimitsTest(LimitsCase):
    def test_total_queue_bytes_across_pairs(self):
        relay = self.relay(max_total_queue_bytes=7500)
        big = {"type": "t", "pad": "x" * 900}
        pc1, phone1 = relay.paired()
        pc2, phone2 = relay.paired()
        relay.core.claim_addr.update({pc1.pair: "10.0.0.1", pc2.pair: "10.0.0.2"})
        phone1.close()
        phone2.close()
        self.assertEqual(pc1.send(big, ttl=60)[0], 202)
        self.assertEqual(pc2.send(big, ttl=60)[0], 202)
        status, data, _ = pc2.send(big, ttl=60)
        self.assertEqual((status, data["error"]), (507, "queue_bytes_full"))
        self.assertEqual(pc2.send(big, ttl=0)[0], 202)

    def test_one_address_gets_a_third_of_the_total_queue(self):
        relay = self.relay(max_total_queue_bytes=15000)
        big = {"type": "t", "pad": "x" * 900}
        mine, mine_phone = relay.paired()
        other, other_phone = relay.paired()
        relay.core.claim_addr[other.pair] = "10.0.0.2"
        mine_phone.close()
        other_phone.close()
        self.assertEqual(mine.send(big, ttl=60)[0], 202)
        self.assertEqual(mine.send(big, ttl=60)[0], 202)
        status, data, _ = mine.send(big, ttl=60)
        self.assertEqual((status, data["error"]), (507, "queue_bytes_full"))
        self.assertEqual(other.send(big, ttl=60)[0], 202)


class BacklogTest(unittest.TestCase):
    def test_unread_stream_is_closed_when_backlog_overflows(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, max_queue_bytes_per_pair=1000))
        core.store.pairs["p"] = {"status": "active"}
        stream = Stream("phone", "s", 0, 128)
        core.streams[("p", "phone")] = stream
        env = {"ct": "A" * 300000}
        for seq in range(1, 4):
            core._deliver("p", stream, seq, env)
        self.assertIn(("p", "phone"), core.streams)
        core._deliver("p", stream, 4, env)
        self.assertTrue(stream.closed)
        self.assertNotIn(("p", "phone"), core.streams)

    def test_written_messages_free_backlog(self):
        stream = Stream("phone", "s", 0, 128)
        self.assertTrue(stream.reserve(600, 1000))
        self.assertFalse(stream.reserve(600, 1000))
        stream.release(600)
        self.assertTrue(stream.reserve(600, 1000))

    def test_shared_budget_limits_all_streams_and_drop_returns_it(self):
        budget = Budget(1000)
        a = Stream("pc", "a", 0, 128, [budget])
        b = Stream("phone", "b", 0, 128, [budget])
        self.assertTrue(a.reserve(700, 5000))
        self.assertFalse(b.reserve(400, 5000))
        a.drop()
        self.assertEqual(budget.total, 0)
        self.assertTrue(b.reserve(400, 5000))

    def test_every_budget_of_a_stream_is_charged(self):
        shared, own = Budget(10000), Budget(1000)
        a = Stream("pc", "a", 0, 128, [shared, own])
        self.assertTrue(a.reserve(900, 5000))
        self.assertFalse(a.reserve(200, 5000))
        self.assertEqual((shared.total, own.total), (900, 900))
        a.drop()
        self.assertEqual((shared.total, own.total, a.backlog), (0, 0, 0))

    def test_queue_that_does_not_fit_closes_the_new_stream_in_order(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, max_total_queue_bytes=4000))
        env = lambda n: {"ct": "A" * n}
        rec = {"status": "active", "queue": [
            {"seq": 1, "to": "phone", "env": env(100), "size": 100, "expires": 1e12},
            {"seq": 2, "to": "phone", "env": env(3000), "size": 3000, "expires": 1e12},
            {"seq": 3, "to": "phone", "env": env(100), "size": 100, "expires": 1e12}]}
        core.store.pairs["p"] = rec
        stream = core._open_stream("p", "phone", rec)
        items = []
        while not stream.outbox.empty():
            items.append(stream.outbox.get())
        seqs = [i[1] for i in items if i is not CLOSE and i[0] == "message"]
        self.assertEqual(seqs, [1])
        self.assertIs(items[-1], CLOSE)
        self.assertIs(core.streams[("p", "phone")], stream)
        core.close_stream("p", stream)
        stream.drop()
        self.assertNotIn(("p", "phone"), core.streams)
        self.assertEqual(core.backlog_budget.total, 0)

    def test_queue_that_does_not_fit_replaces_previous_stream_cleanly(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, max_total_queue_bytes=4000))
        rec = {"status": "active", "queue": [], "phone_sign": None, "phone_agree": None, "tag": None}
        core.store.pairs["p"] = rec
        first = core._open_stream("p", "pc", rec)
        rec["queue"].append({"seq": 1, "to": "pc", "env": {"ct": "A" * 5000}, "size": 5000, "expires": 1e12})
        second = core._open_stream("p", "pc", rec)
        self.assertTrue(first.closed)
        self.assertIs(core.streams[("p", "pc")], second)
        core.close_stream("p", second)
        second.drop()
        self.assertIn("p", core.pc_down)
        core._deliver("p", first, 2, {"ct": "A" * 10})
        self.assertEqual(core.backlog_budget.total, 0)


class WatchdogTest(unittest.TestCase):
    def test_watchdog_survives_failing_reap(self):
        tmp = tempfile.mkdtemp()
        core = Core(Store(tmp), Config(data_dir=tmp, watchdog_sec=0.02))
        calls = []

        def reap():
            calls.append(1)
            raise OSError("disk full")

        core.reap = reap
        devnull = open(os.devnull, "w")
        self.addCleanup(devnull.close)
        import sys
        saved = sys.stderr
        sys.stderr = devnull
        try:
            core.start_watchdog()
            wait_until(lambda: len(calls) >= 3)
        finally:
            core.stop()
            sys.stderr = saved


if __name__ == "__main__":
    unittest.main()
