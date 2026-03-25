from __future__ import annotations

import json
from urllib.request import urlopen


DISCOVERY_URL = "https://discovery.meethue.com/"


def discover_bridges(timeout: float = 5.0) -> list[dict]:
    with urlopen(DISCOVERY_URL, timeout=timeout) as response:
        payload = json.loads(response.read().decode("utf-8"))

    bridges = []
    for item in payload:
        bridges.append(
            {
                "bridge_id": item.get("id"),
                "bridge_ip": item.get("internalipaddress"),
                "port": item.get("port"),
            }
        )

    return sorted(bridges, key=lambda bridge: (bridge["bridge_ip"] or "", bridge["bridge_id"] or ""))

