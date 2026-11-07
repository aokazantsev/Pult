import os
import tempfile
import unittest

from pult_relay.store import Store


class StoreTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)

    def test_corrupt_pairs_moved_aside(self):
        path = os.path.join(self.tmp.name, "pairs.json")
        with open(path, "w", encoding="utf-8") as f:
            f.write("{broken")
        store = Store(self.tmp.name)
        self.assertEqual(store.pairs, {})
        self.assertFalse(os.path.exists(path))
        self.assertTrue(os.path.exists(path + ".corrupt"))

    def test_corrupt_state_moved_aside(self):
        path = os.path.join(self.tmp.name, "state.json")
        with open(path, "w", encoding="utf-8") as f:
            f.write("")
        store = Store(self.tmp.name)
        self.assertEqual(store.state, {})
        self.assertTrue(os.path.exists(path + ".corrupt"))


if __name__ == "__main__":
    unittest.main()
