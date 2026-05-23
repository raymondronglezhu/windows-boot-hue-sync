# 🖥️ Windows Boot Hue Sync 💡

A tiny Windows service that keeps your Philips Hue lights in sync with your PC.

- 🖥️ Your PC turns on → your lights turn on.
- 💤 Your PC sleeps or shuts down → your lights turn off.
- 👀 Your screen wakes from idle → your lights turn on. Your screen blanks → they turn off.

You pick which room and scene to use during install. You can turn off any of the six events if you don't want it.

## 📦 Install

Clone this repo, open PowerShell in the folder, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1
```

You'll be walked through five questions:

1. **Press the button on your Hue bridge** (you have 90 seconds, with a countdown).
2. **Pick a room** — or `All lights` if you haven't sorted lights into rooms yet.
3. **Pick a scene** — or `Last used` to just restore whatever was on before.
4. **Keep the default triggers, or customize?** Saying yes lets you turn off any of the six events individually.
5. **Approve the Administrator prompt** so the service can install itself.

Done. Your lights will now follow your PC.

## ✏️ Change settings later

Re-run the installer. It remembers your bridge, so you go straight to the room / scene / triggers questions.

## 🗑️ Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_service_admin.ps1
```

## 📋 Requirements

- Windows 10 or 11
- Python 3.11+ on `PATH` (only used during install; the running service is a native `.exe`)
- A Philips Hue bridge on your home network

---

## 🔧 Troubleshooting

**"Hue cloud discovery is rate-limiting your network."**
The discovery service Philips runs gets touchy if you retry too quickly. Either wait ~10 minutes, or skip discovery by giving it your bridge IP directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1 -BridgeIp 192.168.1.2
```

(Find your bridge IP in your router's connected-devices list — look for "Philips hue" or MAC prefix `ec:b5:fa`.)

**Lights didn't react to something.**
Check `automation\service.log` — it records every event with a timestamp.

## ⚙️ Customizing without rerunning the installer

The installer writes `automation\service.json`. Edit it directly, then restart the service:

```powershell
sc.exe stop HuePowerService
sc.exe start HuePowerService
```

Each entry in `triggers` independently turns one of the six events on or off. An empty `scene_id` means "restore last used" instead of activating a specific scene.

```json
{
  "bridge_ip": "192.168.1.2",
  "username": "...",
  "room_id": "1",
  "room_name": "Living room",
  "scene_id": "...",
  "scene_name": "Bright",
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

## 📝 Notes

- All traffic stays on your home network — no cloud after the one-time discovery call.
- Your Hue API key lives in `automation\service.json` as plaintext, readable by any local user on the machine. Treat it like a saved Wi-Fi password.
- `service.log` grows over time and isn't auto-rotated — prune it manually if you keep the service running for many months.
- A sudden power loss can't be handled — Windows doesn't give the service a chance to react.

## 🛠️ For developers

The repo has three parts:

- `automation/HuePowerService.cs` — a native Windows service that talks to the Hue bridge over LAN HTTP. Compiled with `csc.exe` from .NET Framework 4.x (ships with Windows 10/11, no SDK needed).
- `automation/*.ps1` — installer / uninstaller scripts.
- `hue_agent/` — a small Python CLI used only at install time, for bridge discovery, pairing, and listing rooms / scenes.

Manual CLI use:

```powershell
python -m hue_agent discover
python -m hue_agent pair --watch --timeout 60
python -m hue_agent rooms
python -m hue_agent scenes --room "Living room"
python -m hue_agent room-off "Living room"
python -m hue_agent activate-scene "Bright" --room "Living room"
```

Tests:

```powershell
python -m unittest discover -s tests -v
```

Licensed MIT. PRs welcome.
