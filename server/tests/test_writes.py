import json
import os
import tempfile
import unittest
from unittest import mock

from pult_relay.codec import b64u
from pult_relay.config import Config
from pult_relay.core import Core, RelayError
from pult_relay.store import Store
from tests.helpers import Clock


def envelope(pair_id, mid, ttl=60, size=28):
    env = {"v": 2, "pair": pair_id, "from": "pc", "id": mid, "ts": 0, "ttl": ttl, "ct": b64u(b"\0" * size)}
    return json.dumps(env).encode("utf-8")


class WriteCase(unittest.TestCase):
    def core(self, **config):
        tmp = tempfile.mkdtemp()
        self.clock = Clock()
        return Core(Store(tmp), Config(data_dir=tmp, **config), self.clock)

    def pair(self, core, pair_id, addr=None):
        core.store.pairs[pair_id] = {"status": "active", "created": 0, "queue": [], "seen": [], "seq": 0,
                                     "pc_sign": None, "pc_agree": None, "phone_sign": None, "phone_agree": None, "tag": None}
        if addr is not None:
            core.claim_addr[pair_id] = addr
        return core.store.pairs[pair_id]

    def post(self, core, pair_id, mid, addr, ttl=60, size=28):
        return core._post(pair_id, "pc", core.store.pairs[pair_id], envelope(pair_id, mid, ttl, size), addr)

    def error(self, call):
        with self.assertRaises(RelayError) as refused:
            call()
        return refused.exception.status, refused.exception.code


class WriteRateTest(WriteCase):
    def test_limit_is_per_address(self):
        core = self.core(rate_writes_per_min=2)
        self.pair(core, "p")
        self.post(core, "p", "a1", "a")
        self.post(core, "p", "a2", "a")
        self.assertEqual(self.error(lambda: self.post(core, "p", "a3", "a")), (429, "rate_limited"))
        self.assertFalse(self.post(core, "p", "b1", "b")["dup"])

    def test_zero_ttl_and_dup_do_not_spend_the_limit(self):
        core = self.core(rate_writes_per_min=1)
        self.pair(core, "p")
        self.post(core, "p", "live", "a", ttl=0)
        self.post(core, "p", "q1", "a")
        self.assertTrue(self.post(core, "p", "q1", "a")["dup"])
        self.assertEqual(self.error(lambda: self.post(core, "p", "q2", "a")), (429, "rate_limited"))

    def test_rejected_queue_does_not_spend_the_limit(self):
        core = self.core(rate_writes_per_min=2, max_queue_per_pair=1)
        self.pair(core, "p")
        self.post(core, "p", "q1", "a")
        for i in range(3):
            self.assertEqual(self.error(lambda: self.post(core, "p", f"x{i}", "a")), (429, "queue_full"))
        rec = core.store.pairs["p"]
        core._ack("phone", rec, json.dumps({"ids": ["q1"]}).encode(), "a")
        self.assertEqual(self.error(lambda: self.post(core, "p", "q2", "a")), (429, "rate_limited"))

    def test_ack_spends_the_limit_only_when_it_removes(self):
        core = self.core(rate_writes_per_min=1)
        rec = self.pair(core, "p")
        self.post(core, "p", "q1", "a")
        self.assertEqual(core._ack("phone", rec, json.dumps({"ids": ["nothing"]}).encode(), "a"), {"removed": 0})
        self.assertEqual(self.error(lambda: core._ack("phone", rec, json.dumps({"ids": ["q1"]}).encode(), "a")),
                         (429, "rate_limited"))
        self.assertEqual(len(rec["queue"]), 1)
        self.clock.advance(61)
        self.assertEqual(core._ack("phone", rec, json.dumps({"ids": ["q1"]}).encode(), "a"), {"removed": 1})


    def test_queued_id_is_dup_after_zero_ttl_flood(self):
        core = self.core(max_queue_per_pair=1, seen_ids_per_pair=1)
        rec = self.pair(core, "p")
        self.post(core, "p", "q1", "a")
        for i in range(4):
            self.post(core, "p", f"z{i}", "a", ttl=0)
        self.assertNotIn("q1", rec["seen"])
        self.assertTrue(self.post(core, "p", "q1", "a")["dup"])
        self.assertEqual(len(rec["queue"]), 1)

class RejectedIdTest(WriteCase):
    def test_rejected_message_can_be_sent_again_with_the_same_id(self):
        core = self.core(rate_writes_per_min=1, max_queue_per_pair=1)
        rec = self.pair(core, "p")
        self.post(core, "p", "q1", "a")
        self.assertEqual(self.error(lambda: self.post(core, "p", "q2", "a")), (429, "queue_full"))
        self.assertNotIn("q2", rec["seen"])
        rec["queue"].clear()
        self.assertEqual(self.error(lambda: self.post(core, "p", "q2", "a")), (429, "rate_limited"))
        self.assertNotIn("q2", rec["seen"])
        self.clock.advance(61)
        self.assertFalse(self.post(core, "p", "q2", "a")["dup"])


class OwnerGroupTest(WriteCase):
    def test_pairs_with_and_without_address_are_counted_apart(self):
        core = self.core(max_total_queue_bytes=60000)
        self.pair(core, "owned", "a")
        self.pair(core, "loose")
        for i in range(3):
            self.post(core, "owned", f"o{i}", "a", size=3000)
        self.assertFalse(self.post(core, "loose", "l0", "x", size=3000)["dup"])

    def test_pairs_without_address_share_one_third(self):
        core = self.core(max_total_queue_bytes=60000)
        self.pair(core, "loose1")
        self.pair(core, "loose2")
        for i in range(3):
            self.post(core, "loose1", f"a{i}", None, size=3000)
        self.assertEqual(self.error(lambda: self.post(core, "loose2", "b0", None, size=3000)), (507, "queue_bytes_full"))
        core.store.pairs["loose1"]["queue"].clear()
        self.assertFalse(self.post(core, "loose2", "b0", None, size=3000)["dup"])

    def test_writers_bind_loose_pairs_to_their_own_thirds(self):
        core = self.core(max_total_queue_bytes=60000)
        self.pair(core, "loose1")
        self.pair(core, "loose2")
        for i in range(3):
            self.post(core, "loose1", f"a{i}", "x", size=3000)
        self.assertFalse(self.post(core, "loose2", "b0", "y", size=3000)["dup"])
        self.assertEqual((core.claim_addr["loose1"], core.claim_addr["loose2"]), ("x", "y"))


class ExpiryTest(WriteCase):
    def test_reap_drops_expired_messages_of_untouched_pairs(self):
        core = self.core(max_total_queue_bytes=60000)
        rec = self.pair(core, "p", "a")
        for i in range(3):
            self.post(core, "p", f"m{i}", "a", ttl=10, size=3000)
        saves = []
        original = core.store.save_state
        core.store.save_state = lambda: (saves.append(1), original())
        self.clock.advance(11)
        core.reap()
        self.assertEqual(rec["queue"], [])
        self.assertEqual(saves, [1])
        core.reap()
        self.assertEqual(saves, [1])


class ExpiryManyTest(WriteCase):
    def test_one_write_for_many_expired_pairs_and_records(self):
        core = self.core(max_total_queue_bytes=600000)
        recs = [self.pair(core, f"p{i}", f"10.0.0.{i}") for i in range(3)]
        for i in range(3):
            self.post(core, f"p{i}", f"m{i}", f"10.0.0.{i}", ttl=10)
        core.store.pairs["old"] = {"status": "pending", "created": self.clock.now, "queue": [], "seen": []}
        saves = []
        original = core.store.save_state
        core.store.save_state = lambda: (saves.append(1), original())
        self.clock.advance(core.config.claim_ttl + 1)
        core.reap()
        self.assertEqual(saves, [1])
        self.assertEqual([r["queue"] for r in recs], [[], [], []])
        self.assertNotIn("old", core.store.pairs)


class RestartOwnerTest(WriteCase):
    def test_pair_without_address_is_bound_to_its_first_writer(self):
        core = self.core(max_total_queue_bytes=60000)
        self.pair(core, "old")
        self.pair(core, "new", "a")
        for i in range(2):
            self.post(core, "new", f"n{i}", "a", size=3000)
        self.post(core, "old", "o0", "a", size=3000)
        self.assertEqual(core.claim_addr["old"], "a")
        self.assertEqual(self.error(lambda: self.post(core, "old", "o1", "a", size=3000)), (507, "queue_bytes_full"))
        self.pair(core, "other")
        self.assertFalse(self.post(core, "other", "x0", "b", size=3000)["dup"])


class WriteEnvTest(unittest.TestCase):
    def test_rate_writes_is_read_from_environment(self):
        with mock.patch.dict(os.environ, {"PULT_RATE_WRITES": "7"}):
            self.assertEqual(Config.from_env().rate_writes_per_min, 7)


if __name__ == "__main__":
    unittest.main()
