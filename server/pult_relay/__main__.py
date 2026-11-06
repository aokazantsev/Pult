import argparse
import sys

from .app import create_server
from .config import Config
from .core import Core
from .store import Store


def main(argv=None):
    parser = argparse.ArgumentParser(prog="pult_relay")
    sub = parser.add_subparsers(dest="cmd")
    sub.add_parser("run")
    add = sub.add_parser("add-pair")
    add.add_argument("--id")
    rm = sub.add_parser("remove-pair")
    rm.add_argument("id")
    args = parser.parse_args(argv)
    config = Config.from_env()
    store = Store(config.data_dir)
    if args.cmd == "add-pair":
        pair_id, tokens = store.add_pair(args.id)
        print(f"pair={pair_id}\npc_token={tokens['pc']}\nphone_token={tokens['phone']}")
        return 0
    if args.cmd == "remove-pair":
        store.remove_pair(args.id)
        return 0
    if not store.pairs:
        pair_id, tokens = store.add_pair()
        print(f"created pair={pair_id}\npc_token={tokens['pc']}\nphone_token={tokens['phone']}", flush=True)
    core = Core(store, config)
    core.start_watchdog()
    server = create_server(core)
    print(f"pult-relay on {config.host}:{config.port}, data {config.data_dir}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
