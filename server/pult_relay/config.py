import os
from dataclasses import dataclass, fields

PROTOCOL_VERSIONS = [2]


@dataclass(frozen=True)
class Config:
    data_dir: str = "data"
    host: str = "127.0.0.1"
    port: int = 8082
    max_ttl: int = 900
    max_envelope_bytes: int = 65536
    max_queue_per_pair: int = 200
    max_queue_bytes_per_pair: int = 4194304
    max_total_queue_bytes: int = 33554432
    seen_ids_per_pair: int = 512
    keepalive_sec: float = 15.0
    liveness_sec: float = 45.0
    watchdog_sec: float = 5.0
    claim_ttl: int = 600
    challenge_ttl: int = 60
    replay_window: int = 128
    pc_offline_sec: float = 300.0
    deleted_keep_sec: float = 604800.0
    max_pending: int = 200
    max_pairs_per_addr: int = 6
    max_pairs: int = 1000
    max_connections: int = 64
    max_connections_per_addr: int = 16
    request_timeout: float = 15.0
    write_timeout: float = 30.0
    rate_claims_per_min: int = 30
    rate_challenges_per_min: int = 120
    rate_writes_per_min: int = 120
    trust_proxy: bool = False

    @staticmethod
    def from_env():
        d = Config()
        env = os.environ
        values = {
            "data_dir": env.get("PULT_DATA", d.data_dir),
            "host": env.get("PULT_HOST", d.host),
            "port": int(env.get("PULT_PORT", d.port)),
            "max_ttl": int(env.get("PULT_MAX_TTL", d.max_ttl)),
            "max_envelope_bytes": int(env.get("PULT_MAX_ENVELOPE", d.max_envelope_bytes)),
            "max_queue_per_pair": int(env.get("PULT_MAX_QUEUE", d.max_queue_per_pair)),
            "max_queue_bytes_per_pair": int(env.get("PULT_MAX_QUEUE_BYTES", d.max_queue_bytes_per_pair)),
            "max_total_queue_bytes": int(env.get("PULT_MAX_TOTAL_QUEUE_BYTES", d.max_total_queue_bytes)),
            "keepalive_sec": float(env.get("PULT_KEEPALIVE", d.keepalive_sec)),
            "liveness_sec": float(env.get("PULT_LIVENESS", d.liveness_sec)),
            "claim_ttl": int(env.get("PULT_CLAIM_TTL", d.claim_ttl)),
            "pc_offline_sec": float(env.get("PULT_PC_OFFLINE", d.pc_offline_sec)),
            "deleted_keep_sec": float(env.get("PULT_DELETED_KEEP", d.deleted_keep_sec)),
            "max_pending": int(env.get("PULT_MAX_PENDING", d.max_pending)),
            "max_pairs_per_addr": int(env.get("PULT_MAX_PAIRS_PER_ADDR", d.max_pairs_per_addr)),
            "max_pairs": int(env.get("PULT_MAX_PAIRS", d.max_pairs)),
            "max_connections": int(env.get("PULT_MAX_CONNECTIONS", d.max_connections)),
            "max_connections_per_addr": int(env.get("PULT_MAX_CONNECTIONS_PER_ADDR", d.max_connections_per_addr)),
            "request_timeout": float(env.get("PULT_REQUEST_TIMEOUT", d.request_timeout)),
            "write_timeout": float(env.get("PULT_WRITE_TIMEOUT", d.write_timeout)),
            "rate_claims_per_min": int(env.get("PULT_RATE_CLAIMS", d.rate_claims_per_min)),
            "rate_challenges_per_min": int(env.get("PULT_RATE_CHALLENGES", d.rate_challenges_per_min)),
            "rate_writes_per_min": int(env.get("PULT_RATE_WRITES", d.rate_writes_per_min)),
            "trust_proxy": env.get("PULT_TRUST_PROXY", "") in ("1", "true", "yes"),
        }
        assert set(values) <= {f.name for f in fields(Config)}
        return Config(**values)
