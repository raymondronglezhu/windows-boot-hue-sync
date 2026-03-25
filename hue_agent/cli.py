from __future__ import annotations

import argparse
import json
import sys
import time

from hue_agent.client import HueBridgeClient, HueError, HueLinkButtonNotPressed
from hue_agent.config import HueConfig, load_config, save_config
from hue_agent.discovery import discover_bridges


DEFAULT_DEVICE_TYPE = "smart_home_cli#desktop"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Minimal Philips Hue setup helper for Windows Boot Hue Sync")
    parser.add_argument("--config", help="Override config path")
    parser.add_argument("--bridge-ip", help="Override Hue bridge IP")
    parser.add_argument("--username", help="Override stored Hue username")

    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("discover", help="Discover Hue bridges on the network")

    pair_parser = subparsers.add_parser("pair", help="Create a Hue API user after pressing the bridge button")
    pair_parser.add_argument("--device-type", default=DEFAULT_DEVICE_TYPE, help="Label stored by the Hue bridge")
    pair_parser.add_argument("--watch", action="store_true", help="Retry until the bridge button is pressed")
    pair_parser.add_argument("--timeout", type=float, default=30.0, help="How long watch mode should wait")
    pair_parser.add_argument("--interval", type=float, default=2.0, help="Retry interval for watch mode")

    subparsers.add_parser("config", help="Show local Hue config")
    subparsers.add_parser("bridge-info", help="Show bridge metadata")
    subparsers.add_parser("rooms", help="List rooms and zones")

    scenes_parser = subparsers.add_parser("scenes", help="List scenes")
    scenes_parser.add_argument("--room", help="Filter scenes to a specific room")

    room_off_parser = subparsers.add_parser("room-off", help="Turn a room off")
    room_off_parser.add_argument("room")

    activate_scene_parser = subparsers.add_parser("activate-scene", help="Activate a Hue scene")
    activate_scene_parser.add_argument("scene")
    activate_scene_parser.add_argument("--room", help="Use this room to disambiguate scene names")

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        result = dispatch(args)
    except HueError as exc:
        print(
            json.dumps(
                {
                    "ok": False,
                    "error": str(exc),
                    "type": exc.__class__.__name__,
                },
                indent=2,
            ),
            file=sys.stderr,
        )
        return 1

    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


def dispatch(args: argparse.Namespace) -> dict:
    config_path, config = load_config(args.config)

    if args.command == "discover":
        bridges = discover_bridges()
        return {
            "ok": True,
            "bridges": bridges,
        }

    if args.command == "config":
        return {
            "ok": True,
            "config_path": str(config_path),
            "config": config.to_dict(),
        }

    bridge_ip = _resolve_bridge_ip(args, config)

    if args.command == "pair":
        client = HueBridgeClient(bridge_ip=bridge_ip)
        pair_result = _pair_with_optional_watch(
            client,
            device_type=args.device_type,
            watch=args.watch,
            timeout=args.timeout,
            interval=args.interval,
        )
        bridge_info = client.get_bridge_config()
        next_config = HueConfig(
            bridge_ip=bridge_ip,
            bridge_id=bridge_info.get("bridgeid"),
            username=pair_result.get("username"),
            clientkey=pair_result.get("clientkey"),
            device_type=args.device_type,
        )
        saved_path = save_config(next_config, args.config)
        return {
            "ok": True,
            "config_path": str(saved_path),
            "bridge_ip": bridge_ip,
            "bridge_id": bridge_info.get("bridgeid"),
            "username": pair_result.get("username"),
            "clientkey": pair_result.get("clientkey"),
        }

    client = HueBridgeClient(
        bridge_ip=bridge_ip,
        username=args.username or config.username,
    )

    if args.command == "bridge-info":
        return {
            "ok": True,
            "bridge": client.get_bridge_config(),
        }

    if args.command == "rooms":
        return {
            "ok": True,
            "bridge_ip": bridge_ip,
            "rooms": client.list_rooms(),
        }

    if args.command == "scenes":
        return {
            "ok": True,
            "bridge_ip": bridge_ip,
            "scenes": client.list_scenes(room_selector=args.room),
        }

    if args.command == "room-off":
        return {
            "ok": True,
            "bridge_ip": bridge_ip,
            "result": client.set_room_state(args.room, on=False),
        }

    if args.command == "activate-scene":
        return {
            "ok": True,
            "bridge_ip": bridge_ip,
            "result": client.activate_scene(args.scene, room_selector=args.room),
        }

    raise HueError(f"Unsupported command: {args.command}")


def _resolve_bridge_ip(args: argparse.Namespace, config: HueConfig) -> str:
    if args.bridge_ip:
        return args.bridge_ip

    if config.bridge_ip:
        return config.bridge_ip

    bridges = discover_bridges()
    if not bridges:
        raise HueError("No Hue bridges discovered on the network")
    if len(bridges) > 1:
        ips = ", ".join(bridge["bridge_ip"] for bridge in bridges if bridge.get("bridge_ip"))
        raise HueError(f"Multiple Hue bridges found. Re-run with --bridge-ip. Found: {ips}")
    return bridges[0]["bridge_ip"]


def _pair_with_optional_watch(
    client: HueBridgeClient,
    *,
    device_type: str,
    watch: bool,
    timeout: float,
    interval: float,
) -> dict:
    if not watch:
        return client.create_user(device_type)

    deadline = time.monotonic() + timeout
    while True:
        try:
            return client.create_user(device_type)
        except HueLinkButtonNotPressed:
            if time.monotonic() >= deadline:
                raise
            time.sleep(interval)
