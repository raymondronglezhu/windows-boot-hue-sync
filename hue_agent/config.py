from __future__ import annotations

from dataclasses import asdict, dataclass
import json
import os
from pathlib import Path


DEFAULT_CONFIG_DIRNAME = ".hue-agent"
DEFAULT_CONFIG_FILENAME = "config.json"


@dataclass
class HueConfig:
    bridge_ip: str | None = None
    bridge_id: str | None = None
    username: str | None = None
    clientkey: str | None = None
    device_type: str | None = None

    @classmethod
    def from_dict(cls, data: dict) -> "HueConfig":
        return cls(
            bridge_ip=data.get("bridge_ip"),
            bridge_id=data.get("bridge_id"),
            username=data.get("username"),
            clientkey=data.get("clientkey"),
            device_type=data.get("device_type"),
        )

    def to_dict(self) -> dict:
        return {
            key: value
            for key, value in asdict(self).items()
            if value is not None
        }


def resolve_config_path(config_path: str | None = None) -> Path:
    if config_path:
        return Path(config_path).expanduser().resolve()

    env_path = os.environ.get("HUE_AGENT_CONFIG")
    if env_path:
        return Path(env_path).expanduser().resolve()

    return (Path.cwd() / DEFAULT_CONFIG_DIRNAME / DEFAULT_CONFIG_FILENAME).resolve()


def load_config(config_path: str | None = None) -> tuple[Path, HueConfig]:
    path = resolve_config_path(config_path)
    if not path.exists():
        return path, HueConfig()

    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    return path, HueConfig.from_dict(data)


def save_config(config: HueConfig, config_path: str | None = None) -> Path:
    path = resolve_config_path(config_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(config.to_dict(), handle, indent=2, sort_keys=True)
        handle.write("\n")
    return path

