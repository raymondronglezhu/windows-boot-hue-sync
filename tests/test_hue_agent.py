from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from hue_agent.client import HueBridgeClient, HueLinkButtonNotPressed
from hue_agent.config import HueConfig, load_config, save_config


class FakeTransport:
    def __init__(self, responses: dict[tuple[str, str], object]) -> None:
        self.responses = responses
        self.calls: list[tuple[str, str, dict | None]] = []

    def request(self, method: str, path: str, payload: dict | None = None):
        self.calls.append((method, path, payload))
        response = self.responses[(method, path)]
        if callable(response):
            return response(payload)
        return response


FULL_STATE = {
    "lights": {},
    "groups": {
        "1": {
            "name": "Living Room",
            "type": "Room",
            "class": "Living room",
            "lights": ["1", "2"],
            "action": {"on": True, "all_on": False, "bri": 180},
        },
        "2": {
            "name": "Living Room Lamps",
            "type": "LightGroup",
            "lights": ["1"],
            "action": {"on": True, "all_on": True, "bri": 200},
        },
    },
    "scenes": {
        "scene-relax": {
            "name": "Relax",
            "type": "GroupScene",
            "group": "1",
            "lights": ["1", "2"],
            "locked": False,
            "recycle": False,
        }
    },
}


class ClientTests(unittest.TestCase):
    def test_create_user_raises_link_button_error(self) -> None:
        transport = FakeTransport(
            {
                ("POST", "/api"): [{"error": {"type": 101, "description": "link button not pressed"}}]
            }
        )
        client = HueBridgeClient("192.168.1.10", transport=transport)
        with self.assertRaises(HueLinkButtonNotPressed):
            client.create_user("smart_home_cli#desktop")

    def test_list_rooms_returns_only_rooms_and_zones(self) -> None:
        transport = FakeTransport({("GET", "/api/test-user"): FULL_STATE})
        client = HueBridgeClient("192.168.1.10", username="test-user", transport=transport)

        rooms = client.list_rooms()

        self.assertEqual(1, len(rooms))
        self.assertEqual("Living Room", rooms[0]["name"])

    def test_activate_scene_calls_group_action(self) -> None:
        responses = {
            ("GET", "/api/test-user"): FULL_STATE,
            ("PUT", "/api/test-user/groups/1/action"): [{"success": {"/groups/1/action/scene": "scene-relax"}}],
        }
        transport = FakeTransport(responses)
        client = HueBridgeClient("192.168.1.10", username="test-user", transport=transport)

        result = client.activate_scene("Relax", room_selector="Living Room")

        self.assertEqual("scene-relax", result["scene"]["id"])
        self.assertEqual("Living Room", result["room"]["name"])
        self.assertEqual(("PUT", "/api/test-user/groups/1/action", {"scene": "scene-relax"}), transport.calls[-1])

    def test_room_off_calls_group_action(self) -> None:
        responses = {
            ("GET", "/api/test-user"): FULL_STATE,
            ("PUT", "/api/test-user/groups/1/action"): [{"success": {"/groups/1/action/on": False}}],
        }
        transport = FakeTransport(responses)
        client = HueBridgeClient("192.168.1.10", username="test-user", transport=transport)

        result = client.set_room_state("Living Room", on=False)

        self.assertEqual("Living Room", result["target"]["name"])
        self.assertEqual(("PUT", "/api/test-user/groups/1/action", {"on": False}), transport.calls[-1])


class ConfigTests(unittest.TestCase):
    def test_config_round_trip(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            config_path = Path(temp_dir) / "config.json"
            save_config(
                HueConfig(
                    bridge_ip="192.168.1.113",
                    bridge_id="ECB5FAFFFE144A00",
                    username="test-user",
                    clientkey="test-key",
                    device_type="smart_home_cli#desktop",
                ),
                str(config_path),
            )

            path, config = load_config(str(config_path))

            self.assertEqual(config_path.resolve(), path)
            self.assertEqual("192.168.1.113", config.bridge_ip)
            self.assertEqual("test-user", config.username)


if __name__ == "__main__":
    unittest.main()
