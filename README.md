# Hue Agent

`Hue Agent` is a small local-first Philips Hue control layer for shell scripts and AI agents.

It is designed around three jobs:

- discover the Hue bridge on your network
- pair once with the physical bridge button
- expose JSON-friendly commands for listing devices and running actions

## What It Can Do

- discover bridges through `discovery.meethue.com`
- pair with the local Hue bridge and save credentials in `.hue-agent/config.json`
- list lights, rooms, and scenes
- turn lights or rooms on and off
- set brightness
- activate scenes
- run a multi-step automation plan from JSON

## Quick Start

From this folder:

```powershell
python -m hue_agent discover
```

Press the physical button on the Hue bridge, then within about 30 seconds run:

```powershell
python -m hue_agent pair
```

If you want the CLI to keep retrying while you walk over to the bridge:

```powershell
python -m hue_agent pair --watch --timeout 60
```

Once paired:

```powershell
python -m hue_agent lights
python -m hue_agent rooms
python -m hue_agent scenes
python -m hue_agent all-on
python -m hue_agent all-off
python -m hue_agent light-on "Desk Lamp"
python -m hue_agent room-brightness "Living Room" 120
python -m hue_agent activate-scene "Relax" --room "Living Room"
```

## Automation Plans

Automation plans are plain JSON files that an AI agent can create or edit.

Example:

```json
{
  "name": "movie-night",
  "actions": [
    { "type": "activate_scene", "scene": "Relax", "room": "Living Room" },
    { "type": "room_state", "room": "Kitchen", "on": false },
    { "type": "wait", "seconds": 2 },
    { "type": "light_state", "light": "TV Lightstrip", "on": true, "brightness": 180 }
  ]
}
```

Run it with:

```powershell
python -m hue_agent run-plan .\plans\movie-night.example.json
```

## Agent Workflow

For agent integrations, the easiest pattern is:

1. use `python -m hue_agent lights` / `rooms` / `scenes` to inspect the system
2. resolve names from the returned JSON
3. issue focused actions with `light-on`, `room-off`, `activate-scene`, and related commands
4. store repeatable routines as JSON plan files and execute them with `run-plan`

## Notes

- This project uses the local Hue bridge API over your home network.
- Credentials are stored in `.hue-agent/config.json`, which is ignored by `.gitignore`.
- Pairing cannot be completed without pressing the physical button on the bridge.
