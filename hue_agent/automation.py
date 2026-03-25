from __future__ import annotations

import json
from pathlib import Path
import time

from hue_agent.client import HueBridgeClient, HueError


def load_plan(path: str) -> dict:
    file_path = Path(path).expanduser().resolve()
    with file_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise HueError("Automation plan must be a JSON object")
    if not isinstance(payload.get("actions"), list):
        raise HueError("Automation plan must contain an `actions` array")
    payload["_path"] = str(file_path)
    return payload


def run_plan(client: HueBridgeClient, plan: dict) -> dict:
    results = []
    for index, action in enumerate(plan["actions"], start=1):
        if not isinstance(action, dict):
            raise HueError(f"Action {index} must be a JSON object")
        action_type = action.get("type")

        if action_type == "wait":
            seconds = float(action.get("seconds", 0))
            if seconds < 0:
                raise HueError("Wait duration cannot be negative")
            time.sleep(seconds)
            results.append(
                {
                    "step": index,
                    "type": "wait",
                    "seconds": seconds,
                }
            )
            continue

        if action_type == "light_state":
            results.append(
                {
                    "step": index,
                    "type": action_type,
                    "result": client.set_light_state(
                        action["light"],
                        on=action.get("on"),
                        brightness=action.get("brightness"),
                    ),
                }
            )
            continue

        if action_type == "room_state":
            results.append(
                {
                    "step": index,
                    "type": action_type,
                    "result": client.set_room_state(
                        action["room"],
                        on=action.get("on"),
                        brightness=action.get("brightness"),
                    ),
                }
            )
            continue

        if action_type == "activate_scene":
            results.append(
                {
                    "step": index,
                    "type": action_type,
                    "result": client.activate_scene(
                        action["scene"],
                        room_selector=action.get("room"),
                    ),
                }
            )
            continue

        raise HueError(f"Unsupported action type at step {index}: {action_type}")

    return {
        "name": plan.get("name"),
        "path": plan.get("_path"),
        "steps_completed": len(results),
        "results": results,
    }
