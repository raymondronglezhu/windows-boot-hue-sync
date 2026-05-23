# Windows Boot Hue Sync

`Windows Boot Hue Sync` is a small Windows service that keeps a Philips Hue room (or all your lights) in sync with your PC's power state. By default it:

- turns the lights ON when Windows boots
- turns the lights ON when Windows wakes from sleep
- turns the lights ON when the display turns back on after inactivity
- turns the lights OFF when the display turns off after inactivity
- turns the lights OFF when Windows goes to sleep
- turns the lights OFF when Windows shuts down

Each of those six events is a toggle in `automation\service.json` — disable any you don't want, no rebuild required.

## What Is In This Repo

- `automation/HuePowerService.cs`
  Native Windows service. Reads `service.json` next to the .exe at runtime.
- `automation/install_hue_power_service_admin.ps1`
  Builds and installs the service from an elevated PowerShell window. Called by `quick-install.ps1`.
- `automation/remove_hue_power_service_admin.ps1`
  Stops and unregisters the service.
- `hue_agent/`
  Minimal Python CLI for Hue bridge discovery, pairing, and room/scene lookup. Only used during install.
- `quick-install.ps1`
  Interactive top-level installer. Pairs the bridge if needed, lets you pick a room and scene, asks which power events should trigger the lights, writes `service.json`, then installs and starts the service.

## Requirements

- Windows 10 or 11
- Python 3.11+ on `PATH` (only needed during install — the service itself is a native .exe)
- A Philips Hue bridge on the same network
- Administrator approval at install time (UAC prompt)

## Install

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1
```

### What you'll see

1. **Bridge discovery** — `Discovering Hue Bridges in your local network...` followed by `Success - 1 device found:` and the bridge ID. Skipped on subsequent installs because the credentials are cached in `.hue-agent\config.json`.
2. **Pairing** — if pairing is needed, you'll see `Now, press the button on your Hue Bridge to pair in the next 90 seconds, go!` with a live countdown. Press the physical button on the bridge. If you miss the window, hit Enter to restart the timer.
3. **Room picker** — numbered list of your bridge's rooms plus an `All lights` option (works even if you haven't sorted lights into rooms in the Hue app).
4. **Scene picker** — numbered list of scenes in the chosen room, plus a `Last used scene` option that restores whatever was last active at runtime instead of locking in a specific scene. Skipped if you picked `All lights`.
5. **Trigger summary** — shows the default trigger set and asks whether to remove any. Answer `n` to keep all six. Answer `y` to walk through each event individually with an `ON`/`OFF` toggle (the active action is highlighted, the alternative is dimmed).
6. **UAC prompt** — Windows asks for Administrator approval to install the service.
7. **Elevated window** — the service builds and installs, then a green banner confirms everything worked:

   ```
   ================================================
     Hue Power Service installed successfully
   ================================================

     Service : HuePowerService (running)
     Room    : All lights
     Scene   : Last used

     Triggers:
       Windows boot            lights ON
       Windows shut down       lights OFF
       Windows wake            lights ON
       Windows sleep           lights OFF
       Display turns on        lights ON
       Display turns off       lights OFF

     Config  : ...\automation\service.json
     Log     : ...\automation\service.log

   Press Enter to close this window:
   ```

If the elevated window has a problem, the same banner appears in red with the error message.

### If cloud discovery is rate-limited

`discovery.meethue.com` rate-limits per source IP. If you see `Hue cloud discovery is rate-limiting your network. Wait a few minutes...`, either wait 5–10 minutes or skip discovery entirely by passing the bridge IP yourself:

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1 -BridgeIp 192.168.1.111
```

You can find the bridge IP from your router's DHCP table (look for a device with MAC prefix `ec:b5:fa`, which is Philips Hue's vendor block).

## Change Room, Scene, or Triggers After Install

Two options:

**Rerun the installer** — it overwrites `service.json` with your new choices:

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1
```

**Edit `service.json` directly** and restart the service:

```powershell
sc.exe stop HuePowerService
sc.exe start HuePowerService
```

The file looks like this:

```json
{
  "bridge_ip": "192.168.1.111",
  "bridge_id": "ecb5fafffe...",
  "username": "...",
  "room_id": "0",
  "room_name": "All lights",
  "scene_id": "",
  "scene_name": "Last used",
  "triggers": {
    "boot": true,
    "shutdown": true,
    "wake": true,
    "sleep": true,
    "display_on": true,
    "display_off": true
  }
}
```

- `room_id` is the Hue group ID — `"0"` means all lights on the bridge.
- An empty `scene_id` (or `"Last used"` as scene_name) tells the service to send `{"on": true}` to the group instead of activating a specific scene, restoring whatever was last active per bulb.
- Each trigger key independently enables or disables that event's action.

## Remove

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_service_admin.ps1
```

Stops the service and unregisters it from Windows. Does not delete `service.json` or your bridge credentials — those stay so a future reinstall is faster.

## Logs

`automation\service.log` — appended to on every event (boot, wake, sleep, etc.) with timestamps. Useful for diagnosing why a particular event didn't trigger the lights.

## Notes

- This relies on the local Hue bridge API on your home network. No traffic leaves your LAN except the one-time discovery call.
- Boot, wake, and display-on are scene-based for speed (single API call to activate a scene).
- Sleep, shutdown, and display-off send a direct group-off command.
- Shutdown uses Windows service preshutdown handling so Windows gives the service bounded time (20s) to finish before killing it.
- The bridge IP is rediscovered automatically via `discovery.meethue.com` if the cached IP stops responding (e.g. after a router reboot reassigns the DHCP lease).
- The bridge username (Hue API key) is stored as plaintext JSON. This matches the Hue v1 API model — the key is LAN-bound and has no remote access.
- Sudden power loss cannot be handled.

## Manual CLI Use

The `hue_agent` Python module is also a small standalone CLI:

```powershell
python -m hue_agent discover
python -m hue_agent pair --watch --timeout 60
python -m hue_agent rooms
python -m hue_agent scenes --room "Living room"
python -m hue_agent room-off "Living room"
python -m hue_agent activate-scene "Bright" --room "Living room"
```
