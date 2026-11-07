import json
import os
import threading

STATE_VERSION = 2


def _atomic_write(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def _load(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        return None
    except ValueError:
        os.replace(path, path + ".corrupt")
        return None
    if not isinstance(data, dict) or data.get("version") != STATE_VERSION or not isinstance(data.get("pairs"), dict):
        os.replace(path, path + ".old")
        return None
    return data


class StoreError(Exception):
    pass


class Store:
    def __init__(self, data_dir):
        os.makedirs(data_dir, exist_ok=True)
        self.state_path = os.path.join(data_dir, "state.json")
        self._lock = threading.Lock()
        data = _load(self.state_path)
        self.pairs = data["pairs"] if data else {}

    def save_state(self):
        with self._lock:
            try:
                _atomic_write(self.state_path, {"version": STATE_VERSION, "pairs": self.pairs})
            except OSError as e:
                raise StoreError(f"state.json not written: {e}") from e
