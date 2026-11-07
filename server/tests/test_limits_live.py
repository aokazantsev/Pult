import tempfile
import time
import unittest

from pult_relay.config import Config
from pult_relay.core import Core
from pult_relay.store import Store
from tests.helpers import Clock, Relay, wait_until
from tests.test_limits import claim_body, closed_by_server, connect


class LiveCase(unittest.TestCase):
    def relay(self, clock=None, **config):
        relay = Relay(tempfile.mkdtemp(), clock, **config)
        self.addCleanup(relay.close)
        return relay


class HeaderTimeoutTest(LiveCase):
    def test_unfinished_headers_are_dropped_and_slot_returns(self):
        relay = self.relay(request_timeout=0.5, max_connections=4)
        sock = connect(relay.port)
        self.addCleanup(sock.close)
        sock.sendall(b"GET /info HTTP/1.1\r\n")
        started = time.time()
        self.assertTrue(closed_by_server(sock))
        self.assertLess(time.time() - started, 4)
        wait_until(lambda: relay.server.slots._value == 4)


class ProxyAddressTest(LiveCase):
    def claim(self, pc, forwarded):
        return pc.http.request("POST", "/v2/claims", claim_body(), {"X-Forwarded-For": forwarded})[0]

    def test_last_forwarded_address_is_the_client_when_proxy_is_trusted(self):
        relay = self.relay(trust_proxy=True, max_pairs_per_addr=1)
        pc = relay.pc()
        self.assertEqual(self.claim(pc, "1.1.1.1"), 201)
        self.assertEqual(self.claim(pc, "2.2.2.2"), 201)
        self.assertEqual(self.claim(pc, "9.9.9.9, 1.1.1.1"), 429)
        self.assertEqual(self.claim(pc, "1.1.1.1, 3.3.3.3"), 201)

    def test_forwarded_header_is_ignored_without_trust(self):
        relay = self.relay(max_pairs_per_addr=1)
        pc = relay.pc()
        self.assertEqual(self.claim(pc, "1.1.1.1"), 201)
        self.assertEqual(self.claim(pc, "2.2.2.2"), 429)


class ClaimSlotTest(LiveCase):
    def test_deleted_claim_frees_address_slot(self):
        relay = self.relay(max_pairs_per_addr=1)
        pc = relay.pc()
        pc.claim()
        pc.login()
        self.assertEqual(pc.delete("user")[0], 200)
        self.assertEqual(relay.pc().http.request("POST", "/v2/claims", claim_body())[0], 201)

    def test_pair_cap_evicts_the_oldest_deleted_record(self):
        clock = Clock()
        relay = self.relay(clock, max_pairs=3)
        old_pc, old_phone = relay.paired()
        self.assertEqual(old_phone.delete("user")[0], 200)
        clock.advance(10)
        new_pc, new_phone = relay.paired()
        self.assertEqual(new_phone.delete("user")[0], 200)
        pc = relay.pc()
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 201)
        self.assertEqual(pc.http.request("POST", "/v2/claims", claim_body())[0], 201)
        self.assertNotIn(old_pc.pair, relay.core.store.pairs)
        self.assertEqual(relay.core.store.pairs[new_pc.pair]["status"], "deleted")


class QueueTotalTest(LiveCase):
    def test_expired_messages_do_not_hold_the_total_limit(self):
        clock = Clock()
        relay = self.relay(clock, max_total_queue_bytes=7500)
        big = {"type": "t", "pad": "x" * 900}
        pc1, phone1 = relay.paired()
        pc2, phone2 = relay.paired()
        relay.core.claim_addr.update({pc1.pair: "10.0.0.1", pc2.pair: "10.0.0.2"})
        phone1.close()
        phone2.close()
        self.assertEqual(pc1.send(big, ttl=60)[0], 202)
        self.assertEqual(pc2.send(big, ttl=60)[0], 202)
        self.assertEqual(pc2.send(big, ttl=60)[0], 507)
        clock.advance(61)
        self.assertEqual(pc2.send(big, ttl=60)[0], 202)


class BacklogLiveTest(LiveCase):
    def test_reading_stream_frees_backlog_and_stays_open(self):
        relay = self.relay(max_queue_bytes_per_pair=100000)
        pc, phone = relay.paired()
        stream = relay.core.streams[(phone.pair, "phone")]
        self.assertIn(relay.core.backlog_budget, stream.budgets)
        self.assertIn(relay.core.addresses["127.0.0.1"].budget, stream.budgets)
        sent = 0
        while sent < relay.core.backlog_limit + 200000:
            self.assertEqual(pc.send({"type": "t", "pad": "x" * 40000}, ttl=0)[0], 202)
            sent += 40000
            kind, _ = phone.receive(5)
            self.assertEqual(kind, "message")
        self.assertIs(relay.core.streams[(phone.pair, "phone")], stream)
        wait_until(lambda: stream.backlog == 0)

    def test_closed_stream_returns_budget(self):
        relay = self.relay()
        pc, phone = relay.paired()
        stream = relay.core.streams[(phone.pair, "phone")]
        phone.close()
        wait_until(lambda: stream.closed and stream.backlog == 0)
        pc.close()
        wait_until(lambda: relay.core.backlog_budget.total == 0)


class SweepTest(unittest.TestCase):
    def test_reap_writes_state_once_for_many_expired_claims(self):
        tmp = tempfile.mkdtemp()
        clock = Clock()
        core = Core(Store(tmp), Config(data_dir=tmp), clock)
        for i in range(5):
            core.store.pairs[f"p{i}"] = {"status": "pending", "created": clock.now, "queue": []}
        core.store.save_state()
        clock.advance(core.config.claim_ttl + 1)
        saves = []
        original = core.store.save_state
        core.store.save_state = lambda: (saves.append(1), original())
        core.reap()
        self.assertEqual(len(saves), 1)
        self.assertEqual(Store(tmp).pairs, {})


if __name__ == "__main__":
    unittest.main()
