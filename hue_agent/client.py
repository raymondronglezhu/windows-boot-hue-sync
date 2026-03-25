from __future__ import annotations

import json
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


ROOM_TYPES = {"Room", "Zone"}


class HueError(RuntimeError):
    """Base error for Hue bridge operations."""


class HueHttpError(HueError):
    """Raised when the bridge cannot be reached or returns invalid data."""


class HueAuthenticationError(HueError):
    """Raised when a stored username is missing or rejected."""


class HueLinkButtonNotPressed(HueError):
    """Raised when pairing is attempted before the bridge button is pressed."""


class HueResourceNotFound(HueError):
    """Raised when a light, room, or scene selector does not match anything."""


class HueAmbiguousResource(HueError):
    """Raised when a selector matches more than one resource."""


class HueTransport:
    def __init__(self, bridge_ip: str, timeout: float = 10.0) -> None:
        self.base_url = f"http://{bridge_ip}".rstrip("/")
        self.timeout = timeout

    def request(self, method: str, path: str, payload: dict | None = None) -> Any:
        data = None
        headers: dict[str, str] = {}
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"

        request = Request(
            url=f"{self.base_url}{path}",
            data=data,
            headers=headers,
            method=method.upper(),
        )

        try:
            with urlopen(request, timeout=self.timeout) as response:
                raw = response.read().decode("utf-8")
        except HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise HueHttpError(f"Bridge returned HTTP {exc.code}: {body}") from exc
        except URLError as exc:
            raise HueHttpError(f"Could not reach Hue bridge: {exc.reason}") from exc

        if not raw:
            return None

        try:
            return json.loads(raw)
        except json.JSONDecodeError as exc:
            raise HueHttpError(f"Bridge returned invalid JSON: {raw}") from exc


class HueBridgeClient:
    def __init__(
        self,
        bridge_ip: str,
        username: str | None = None,
        transport: HueTransport | None = None,
    ) -> None:
        self.bridge_ip = bridge_ip
        self.username = username
        self.transport = transport or HueTransport(bridge_ip)

    def get_bridge_config(self) -> dict:
        payload = self.transport.request("GET", "/api/config")
        if not isinstance(payload, dict):
            raise HueHttpError("Unexpected bridge config payload")
        return payload

    def create_user(self, device_type: str) -> dict:
        payload = self.transport.request(
            "POST",
            "/api",
            {
                "devicetype": device_type,
                "generateclientkey": True,
            },
        )
        return self._unwrap_action_response(payload)

    def get_full_state(self) -> dict:
        payload = self.transport.request("GET", self._api_path(""))
        if not isinstance(payload, dict):
            raise HueHttpError("Unexpected bridge state payload")
        return payload

    def list_lights(self) -> list[dict]:
        state = self.get_full_state()
        room_map = self._build_room_membership(state)
        lights = []
        for light_id, light in sorted(state.get("lights", {}).items(), key=self._numeric_sort_key):
            light_state = light.get("state", {})
            lights.append(
                {
                    "id": light_id,
                    "name": light.get("name"),
                    "type": light.get("type"),
                    "modelid": light.get("modelid"),
                    "reachable": light_state.get("reachable"),
                    "on": light_state.get("on"),
                    "brightness": light_state.get("bri"),
                    "rooms": room_map.get(light_id, []),
                }
            )
        return lights

    def list_rooms(self) -> list[dict]:
        state = self.get_full_state()
        rooms = []
        for group_id, group in sorted(state.get("groups", {}).items(), key=self._numeric_sort_key):
            if group.get("type") not in ROOM_TYPES:
                continue
            action = group.get("action", {})
            rooms.append(
                {
                    "id": group_id,
                    "name": group.get("name"),
                    "type": group.get("type"),
                    "class": group.get("class"),
                    "light_ids": group.get("lights", []),
                    "lights_count": len(group.get("lights", [])),
                    "all_on": action.get("all_on"),
                    "any_on": action.get("on"),
                    "brightness": action.get("bri"),
                }
            )
        return rooms

    def list_scenes(self, room_selector: str | None = None) -> list[dict]:
        state = self.get_full_state()
        groups = state.get("groups", {})
        scenes = state.get("scenes", {})
        room_id = None
        if room_selector:
            room_id, _ = self._resolve_group(state, room_selector)

        scene_rows = []
        for scene_id, scene in scenes.items():
            if room_id and scene.get("group") != room_id:
                continue
            group_name = None
            if scene.get("group") and scene.get("group") in groups:
                group_name = groups[scene["group"]].get("name")
            scene_rows.append(
                {
                    "id": scene_id,
                    "name": scene.get("name"),
                    "type": scene.get("type"),
                    "group_id": scene.get("group"),
                    "group_name": group_name,
                    "lights_count": len(scene.get("lights", [])),
                    "locked": scene.get("locked"),
                    "recycle": scene.get("recycle"),
                }
            )

        return sorted(scene_rows, key=lambda scene: ((scene["group_name"] or ""), (scene["name"] or ""), scene["id"]))

    def set_light_state(
        self,
        selector: str,
        *,
        on: bool | None = None,
        brightness: int | None = None,
    ) -> dict:
        state = self.get_full_state()
        light_id, light = self._resolve_light(state, selector)
        payload: dict[str, Any] = {}
        if on is not None:
            payload["on"] = on
        if brightness is not None:
            self._validate_brightness(brightness)
            payload["bri"] = brightness
        if not payload:
            raise HueError("No light change requested")

        result = self._unwrap_action_response(
            self.transport.request("PUT", self._api_path(f"/lights/{light_id}/state"), payload)
        )
        return {
            "target": {
                "kind": "light",
                "id": light_id,
                "name": light.get("name"),
            },
            "changes": result,
        }

    def set_room_state(
        self,
        selector: str,
        *,
        on: bool | None = None,
        brightness: int | None = None,
    ) -> dict:
        state = self.get_full_state()
        group_id, group = self._resolve_group(state, selector)
        payload: dict[str, Any] = {}
        if on is not None:
            payload["on"] = on
        if brightness is not None:
            self._validate_brightness(brightness)
            payload["bri"] = brightness
        if not payload:
            raise HueError("No room change requested")

        result = self._unwrap_action_response(
            self.transport.request("PUT", self._api_path(f"/groups/{group_id}/action"), payload)
        )
        return {
            "target": {
                "kind": "room",
                "id": group_id,
                "name": group.get("name"),
            },
            "changes": result,
        }

    def activate_scene(self, scene_selector: str, room_selector: str | None = None) -> dict:
        state = self.get_full_state()
        scene_id, scene = self._resolve_scene(state, scene_selector, room_selector=room_selector)
        group_id = scene.get("group")
        if not group_id:
            raise HueError("Selected scene is not tied to a room/group")

        group_name = state.get("groups", {}).get(group_id, {}).get("name")
        result = self._unwrap_action_response(
            self.transport.request(
                "PUT",
                self._api_path(f"/groups/{group_id}/action"),
                {"scene": scene_id},
            )
        )
        return {
            "scene": {
                "id": scene_id,
                "name": scene.get("name"),
            },
            "room": {
                "id": group_id,
                "name": group_name,
            },
            "changes": result,
        }

    def all_lights_off(self) -> dict:
        state = self.get_full_state()
        results = []
        total_lights = len(state.get("lights", {}))

        for light_id, light in sorted(state.get("lights", {}).items(), key=self._numeric_sort_key):
            if not light.get("state", {}).get("on"):
                continue

            result = self._unwrap_action_response(
                self.transport.request("PUT", self._api_path(f"/lights/{light_id}/state"), {"on": False})
            )
            results.append(
                {
                    "id": light_id,
                    "name": light.get("name"),
                    "changes": result,
                }
            )

        return {
            "lights_seen": total_lights,
            "lights_targeted": len(results),
            "results": results,
        }

    def all_lights_on(self) -> dict:
        state = self.get_full_state()
        results = []
        total_lights = len(state.get("lights", {}))

        for light_id, light in sorted(state.get("lights", {}).items(), key=self._numeric_sort_key):
            if light.get("state", {}).get("on"):
                continue

            result = self._unwrap_action_response(
                self.transport.request("PUT", self._api_path(f"/lights/{light_id}/state"), {"on": True})
            )
            results.append(
                {
                    "id": light_id,
                    "name": light.get("name"),
                    "changes": result,
                }
            )

        return {
            "lights_seen": total_lights,
            "lights_targeted": len(results),
            "results": results,
        }

    def _api_path(self, suffix: str) -> str:
        if not self.username:
            raise HueAuthenticationError("No Hue username configured. Run `python -m hue_agent pair` first.")
        return f"/api/{self.username}{suffix}"

    def _unwrap_action_response(self, payload: Any) -> dict:
        if not isinstance(payload, list):
            raise HueHttpError(f"Unexpected action payload: {payload!r}")

        merged: dict[str, Any] = {}
        for item in payload:
            if "error" in item:
                self._raise_v1_error(item["error"])
            if "success" in item:
                merged.update(item["success"])
        return merged

    def _raise_v1_error(self, error: dict) -> None:
        description = error.get("description", "Unknown Hue bridge error")
        error_type = error.get("type")
        if error_type == 1:
            raise HueAuthenticationError(description)
        if error_type == 101:
            raise HueLinkButtonNotPressed(description)
        raise HueError(description)

    def _build_room_membership(self, state: dict) -> dict[str, list[str]]:
        membership: dict[str, list[str]] = {}
        for _, group in state.get("groups", {}).items():
            if group.get("type") not in ROOM_TYPES:
                continue
            for light_id in group.get("lights", []):
                membership.setdefault(light_id, []).append(group.get("name"))
        for room_names in membership.values():
            room_names.sort()
        return membership

    def _resolve_light(self, state: dict, selector: str) -> tuple[str, dict]:
        return self._resolve_named_resource(state.get("lights", {}), selector, resource_label="light")

    def _resolve_group(self, state: dict, selector: str) -> tuple[str, dict]:
        groups = {
            group_id: group
            for group_id, group in state.get("groups", {}).items()
            if group.get("type") in ROOM_TYPES
        }
        return self._resolve_named_resource(groups, selector, resource_label="room")

    def _resolve_scene(
        self,
        state: dict,
        selector: str,
        *,
        room_selector: str | None = None,
    ) -> tuple[str, dict]:
        scenes = state.get("scenes", {})
        if room_selector:
            room_id, _ = self._resolve_group(state, room_selector)
            scenes = {
                scene_id: scene
                for scene_id, scene in scenes.items()
                if scene.get("group") == room_id
            }
        return self._resolve_named_resource(scenes, selector, resource_label="scene")

    def _resolve_named_resource(
        self,
        resources: dict[str, dict],
        selector: str,
        *,
        resource_label: str,
    ) -> tuple[str, dict]:
        cleaned = selector.strip()
        if cleaned in resources:
            return cleaned, resources[cleaned]

        exact_matches = [
            (resource_id, resource)
            for resource_id, resource in resources.items()
            if (resource.get("name") or "").casefold() == cleaned.casefold()
        ]
        if len(exact_matches) == 1:
            return exact_matches[0]
        if len(exact_matches) > 1:
            names = ", ".join(f"{resource_id}:{resource.get('name')}" for resource_id, resource in exact_matches)
            raise HueAmbiguousResource(f"More than one {resource_label} matched '{selector}': {names}")

        partial_matches = [
            (resource_id, resource)
            for resource_id, resource in resources.items()
            if cleaned.casefold() in (resource.get("name") or "").casefold()
        ]
        if len(partial_matches) == 1:
            return partial_matches[0]
        if len(partial_matches) > 1:
            names = ", ".join(f"{resource_id}:{resource.get('name')}" for resource_id, resource in partial_matches)
            raise HueAmbiguousResource(f"More than one {resource_label} matched '{selector}': {names}")

        raise HueResourceNotFound(f"No {resource_label} matched '{selector}'")

    def _validate_brightness(self, brightness: int) -> None:
        if brightness < 1 or brightness > 254:
            raise HueError("Brightness must be between 1 and 254")

    @staticmethod
    def _numeric_sort_key(item: tuple[str, dict]) -> tuple[int, str]:
        resource_id = item[0]
        if resource_id.isdigit():
            return (0, f"{int(resource_id):08d}")
        return (1, resource_id)
