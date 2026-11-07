import argparse
import sys

from .app import create_server
from .config import Config
from .core import Core
from .store import Store


def main(argv=None):
    parser = argparse.ArgumentParser(prog="pult_relay")
    parser.add_argument("cmd", nargs="?", choices=["run"], default="run")
    parser.parse_args(argv)
    config = Config.from_env()
    core = Core(Store(config.data_dir), config)
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
