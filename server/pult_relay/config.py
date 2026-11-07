import os
from dataclasses import dataclass

PROTOCOL_VERSIONS = [1]


@dataclass(frozen=True)
class Config:
    data_dir: str = "data"
    host: str = "127.0.0.1"
    port: int = 8082
    max_ttl: int = 900
    max_envelope_bytes: int = 65536
    max_queue_per_pair: int = 200
    max_queue_bytes_per_pair: int = 4194304
    seen_ids_per_pair: int = 2000
    keepalive_sec: float = 15.0
    liveness_sec: float = 45.0
    watchdog_sec: float = 5.0

    @staticmethod
    def from_env():
        d = Config()
        return Config(
            data_dir=os.environ.get("PULT_DATA", d.data_dir),
            host=os.environ.get("PULT_HOST", d.host),
            port=int(os.environ.get("PULT_PORT", d.port)),
            max_ttl=int(os.environ.get("PULT_MAX_TTL", d.max_ttl)),
            max_envelope_bytes=int(os.environ.get("PULT_MAX_ENVELOPE", d.max_envelope_bytes)),
            max_queue_per_pair=int(os.environ.get("PULT_MAX_QUEUE", d.max_queue_per_pair)),
            max_queue_bytes_per_pair=int(os.environ.get("PULT_MAX_QUEUE_BYTES", d.max_queue_bytes_per_pair)),
            keepalive_sec=float(os.environ.get("PULT_KEEPALIVE", d.keepalive_sec)),
            liveness_sec=float(os.environ.get("PULT_LIVENESS", d.liveness_sec)),
        )
