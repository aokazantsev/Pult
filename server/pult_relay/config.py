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
    seen_ids_per_pair: int = 2000
    keepalive_sec: float = 15.0
    liveness_sec: float = 45.0
    watchdog_sec: float = 5.0
    claim_ttl: int = 600
    challenge_ttl: int = 60
    replay_window: int = 128
    pc_offline_sec: float = 300.0
    deleted_keep_sec: float = 604800.0
    max_pending: int = 200
    rate_claims_per_min: int = 30
    rate_challenges_per_min: int = 120
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
            "keepalive_sec": float(env.get("PULT_KEEPALIVE", d.keepalive_sec)),
            "liveness_sec": float(env.get("PULT_LIVENESS", d.liveness_sec)),
            "claim_ttl": int(env.get("PULT_CLAIM_TTL", d.claim_ttl)),
            "pc_offline_sec": float(env.get("PULT_PC_OFFLINE", d.pc_offline_sec)),
            "deleted_keep_sec": float(env.get("PULT_DELETED_KEEP", d.deleted_keep_sec)),
            "max_pending": int(env.get("PULT_MAX_PENDING", d.max_pending)),
            "rate_claims_per_min": int(env.get("PULT_RATE_CLAIMS", d.rate_claims_per_min)),
            "rate_challenges_per_min": int(env.get("PULT_RATE_CHALLENGES", d.rate_challenges_per_min)),
            "trust_proxy": env.get("PULT_TRUST_PROXY", "") in ("1", "true", "yes"),
        }
        assert set(values) <= {f.name for f in fields(Config)}
        return Config(**values)
