import json
import tempfile
import unittest

from pult_relay.config import Config
from pult_relay.core import CLOSE, Core, RelayError
from pult_relay.store import Store
from tests.helpers import Clock, drain, envelope


def make(tmp, **overrides):
    config = Config(data_dir=tmp, **overrides)
    store = Store(tmp)
    pair, tokens = store.add_pair("p1")
    clock = Clock()
    return Core(store, config, clock), store, clock, tokens


def post(core, side, mid, ttl=60):
    env = envelope("p1", side, mid, ttl)
    return core.post("p1", side, env, len(json.dumps(env)))


class CoreTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)

    def test_authenticate_side_by_token(self):
        core, _, _, tokens = make(self.tmp.name)
        self.assertEqual(core.authenticate("p1", tokens["pc"]), "pc")
        self.assertEqual(core.authenticate("p1", tokens["phone"]), "phone")
        with self.assertRaises(RelayError):
            core.authenticate("p1", "wrong")
        with self.assertRaises(RelayError):
            core.authenticate("nope", tokens["pc"])

    def test_queued_message_replayed_on_open_in_order(self):
        core, *_ = make(self.tmp.name)
        post(core, "pc", "a")
        post(core, "pc", "b")
        stream = core.open_stream("p1", "phone")
        ids = [item[2]["id"] for item in drain(stream) if item[0] == "message"]
        self.assertEqual(ids, ["a", "b"])

    def test_zero_ttl_bypasses_queue_and_is_dropped_without_stream(self):
        core, store, *_ = make(self.tmp.name)
        post(core, "pc", "s", ttl=0)
        self.assertEqual(store.pair_state("p1")["queue"], [])
        stream = core.open_stream("p1", "phone")
        self.assertEqual(drain(stream), [])

    def test_zero_ttl_delivered_to_open_stream_in_order_with_queued(self):
        core, *_ = make(self.tmp.name)
        stream = core.open_stream("p1", "phone")
        post(core, "pc", "a", ttl=60)
        post(core, "pc", "b", ttl=0)
        post(core, "pc", "c", ttl=60)
        seqs = [item[1] for item in drain(stream)]
        self.assertEqual(seqs, sorted(seqs))
        self.assertEqual(len(seqs), 3)

    def test_ack_removes_from_queue(self):
        core, *_ = make(self.tmp.name)
        post(core, "pc", "a")
        post(core, "pc", "b")
        self.assertEqual(core.ack("p1", "phone", ["a"])["removed"], 1)
        stream = core.open_stream("p1", "phone")
        ids = [item[2]["id"] for item in drain(stream)]
        self.assertEqual(ids, ["b"])

    def test_ack_only_for_own_recipient(self):
        core, *_ = make(self.tmp.name)
        post(core, "pc", "a")
        self.assertEqual(core.ack("p1", "pc", ["a"])["removed"], 0)

    def test_unacked_message_redelivered_after_reconnect(self):
        core, *_ = make(self.tmp.name)
        post(core, "pc", "a")
        first = core.open_stream("p1", "phone")
        self.assertEqual(len(drain(first)), 1)
        core.close_stream("p1", first)
        second = core.open_stream("p1", "phone")
        self.assertEqual(len(drain(second)), 1)

    def test_expired_not_delivered(self):
        core, _, clock, _ = make(self.tmp.name)
        post(core, "pc", "a", ttl=30)
        clock.now += 31
        self.assertEqual(drain(core.open_stream("p1", "phone")), [])

    def test_ttl_clamped_to_max(self):
        core, store, clock, _ = make(self.tmp.name, max_ttl=100)
        post(core, "pc", "a", ttl=99999)
        self.assertEqual(store.pair_state("p1")["queue"][0]["expires"], clock.now + 100)

    def test_duplicate_id_acknowledged_not_requeued(self):
        core, store, *_ = make(self.tmp.name)
        first = post(core, "pc", "a")
        second = post(core, "pc", "a")
        self.assertFalse(first["dup"])
        self.assertTrue(second["dup"])
        self.assertEqual(len(store.pair_state("p1")["queue"]), 1)

    def test_queue_full_rejects(self):
        core, *_ = make(self.tmp.name, max_queue_per_pair=2)
        post(core, "pc", "a")
        post(core, "pc", "b")
        with self.assertRaises(RelayError) as ctx:
            post(core, "pc", "c")
        self.assertEqual(ctx.exception.status, 429)

    def test_queue_bytes_limit_rejects(self):
        core, store, *_ = make(self.tmp.name, max_queue_bytes_per_pair=250)
        env = envelope("p1", "pc", "a")
        core.post("p1", "pc", env, 100)
        core.post("p1", "pc", envelope("p1", "pc", "b"), 100)
        with self.assertRaises(RelayError) as ctx:
            core.post("p1", "pc", envelope("p1", "pc", "c"), 100)
        self.assertEqual(ctx.exception.status, 507)
        core.post("p1", "phone", envelope("p1", "phone", "d"), 100)
        self.assertEqual(len(store.pair_state("p1")["queue"]), 3)

    def test_queue_bytes_freed_by_ack(self):
        core, *_ = make(self.tmp.name, max_queue_bytes_per_pair=250)
        core.post("p1", "pc", envelope("p1", "pc", "a"), 100)
        core.post("p1", "pc", envelope("p1", "pc", "b"), 100)
        core.ack("p1", "phone", ["a"])
        core.post("p1", "pc", envelope("p1", "pc", "c"), 100)

    def test_non_ascii_token_unauthorized(self):
        core, *_ = make(self.tmp.name)
        for token in ("тест", "café"):
            with self.assertRaises(RelayError) as ctx:
                core.authenticate("p1", token)
            self.assertEqual(ctx.exception.status, 401)

    def test_too_large_rejected(self):
        core, *_ = make(self.tmp.name, max_envelope_bytes=50)
        with self.assertRaises(RelayError) as ctx:
            core.post("p1", "pc", envelope("p1", "pc", "a"), 51)
        self.assertEqual(ctx.exception.status, 413)

    def test_bad_envelopes(self):
        core, *_ = make(self.tmp.name)
        bad = [
            {**envelope("p1", "pc", "a"), "v": 2},
            {**envelope("p1", "pc", "a"), "from": "phone"},
            {**envelope("p1", "pc", "a"), "pair": "other"},
            {**envelope("p1", "pc", "a"), "ttl": -1},
            {**envelope("p1", "pc", "a"), "ttl": "5"},
            {**envelope("p1", "pc", ""), },
        ]
        for env in bad:
            with self.assertRaises(RelayError, msg=str(env)):
                core.post("p1", "pc", env, 10)
        env = envelope("p1", "pc", "a")
        del env["inner"]
        with self.assertRaises(RelayError):
            core.post("p1", "pc", env, 10)

    def test_ciphertext_envelope_accepted(self):
        core, *_ = make(self.tmp.name)
        env = envelope("p1", "pc", "a")
        del env["inner"]
        env["ct"] = "AAAA"
        self.assertFalse(core.post("p1", "pc", env, 10)["dup"])

    def test_pc_gets_current_phone_state_on_open(self):
        core, *_ = make(self.tmp.name)
        pc = core.open_stream("p1", "pc")
        self.assertEqual(drain(pc)[0][2], {"type": "phone_stream", "state": "closed"})
        core.open_stream("p1", "phone")
        pc2 = core.open_stream("p1", "pc")
        self.assertEqual(drain(pc2)[0][2], {"type": "phone_stream", "state": "open"})

    def test_phone_open_close_signals_pc(self):
        core, *_ = make(self.tmp.name)
        pc = core.open_stream("p1", "pc")
        drain(pc)
        phone = core.open_stream("p1", "phone")
        core.close_stream("p1", phone)
        states = [item[2]["state"] for item in drain(pc) if item[0] == "server"]
        self.assertEqual(states, ["open", "closed"])

    def test_closed_signal_after_messages_from_phone(self):
        core, *_ = make(self.tmp.name)
        pc = core.open_stream("p1", "pc")
        phone = core.open_stream("p1", "phone")
        drain(pc)
        post(core, "phone", "r1")
        core.close_stream("p1", phone)
        kinds = [item[0] for item in drain(pc)]
        self.assertEqual(kinds, ["message", "server"])

    def test_phone_stream_replacement_gives_no_signal(self):
        core, *_ = make(self.tmp.name)
        pc = core.open_stream("p1", "pc")
        first = core.open_stream("p1", "phone")
        drain(pc)
        core.open_stream("p1", "phone")
        self.assertIn(CLOSE, drain(first))
        self.assertEqual([i for i in drain(pc) if i is not CLOSE], [])
        core.close_stream("p1", first)
        self.assertEqual(drain(pc), [])
        self.assertEqual(core.phone_state("p1"), "open")

    def test_dead_stream_reaped_after_liveness_threshold(self):
        core, _, clock, _ = make(self.tmp.name, liveness_sec=45)
        pc = core.open_stream("p1", "pc")
        core.open_stream("p1", "phone")
        drain(pc)
        clock.now += 30
        core.touch("p1", "pc")
        core.reap_dead()
        self.assertEqual(core.phone_state("p1"), "open")
        clock.now += 30
        core.touch("p1", "pc")
        core.reap_dead()
        self.assertEqual(core.phone_state("p1"), "closed")
        self.assertEqual([i[2]["state"] for i in drain(pc)], ["closed"])

    def test_activity_keeps_stream_alive(self):
        core, _, clock, _ = make(self.tmp.name, liveness_sec=45)
        core.open_stream("p1", "phone")
        for _ in range(5):
            clock.now += 30
            core.touch("p1", "phone")
            core.reap_dead()
        self.assertEqual(core.phone_state("p1"), "open")

    def test_state_persists_across_core_instances(self):
        core, store, clock, tokens = make(self.tmp.name)
        post(core, "pc", "a")
        post(core, "phone", "r")
        store2 = Store(self.tmp.name)
        core2 = Core(store2, Config(data_dir=self.tmp.name), clock)
        ids = [item[2]["id"] for item in drain(core2.open_stream("p1", "phone"))]
        self.assertEqual(ids, ["a"])
        self.assertTrue(post(core2, "pc", "a")["dup"])
        self.assertEqual(post(core2, "pc", "b")["seq"], 3)


if __name__ == "__main__":
    unittest.main()
