import json
import os
import secrets
import threading


def _atomic_write(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def _load(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return default
    except json.JSONDecodeError:
        os.replace(path, path + ".corrupt")
        return default


class Store:
    def __init__(self, data_dir):
        os.makedirs(data_dir, exist_ok=True)
        self.pairs_path = os.path.join(data_dir, "pairs.json")
        self.state_path = os.path.join(data_dir, "state.json")
        self._lock = threading.Lock()
        self.pairs = _load(self.pairs_path, {})
        self.state = _load(self.state_path, {})

    def add_pair(self, pair_id=None):
        pair_id = pair_id or secrets.token_hex(8)
        with self._lock:
            if pair_id in self.pairs:
                raise ValueError("pair exists")
            self.pairs[pair_id] = {"pc": secrets.token_urlsafe(24), "phone": secrets.token_urlsafe(24)}
            _atomic_write(self.pairs_path, self.pairs)
            return pair_id, dict(self.pairs[pair_id])

    def remove_pair(self, pair_id):
        with self._lock:
            self.pairs.pop(pair_id, None)
            self.state.pop(pair_id, None)
            _atomic_write(self.pairs_path, self.pairs)
            _atomic_write(self.state_path, self.state)

    def pair_state(self, pair_id):
        return self.state.setdefault(pair_id, {"seq": 0, "queue": [], "seen": []})

    def save_state(self):
        with self._lock:
            _atomic_write(self.state_path, self.state)
