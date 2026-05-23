# Hue Power Service

The Windows service that watches power events and drives the Hue bridge.

Behavior is configured by `service.json` next to the .exe (written by the top-level [`quick-install.ps1`](../quick-install.ps1)). Each of the six event types can be enabled or disabled independently.

## Files

- `HuePowerService.cs`
  Native Windows service source.
- `build_hue_power_service.ps1`
  Compiles `HuePowerService.exe`.
- `install_hue_power_service_admin.ps1`
  Installs the service from an elevated PowerShell window. Assumes `service.json` already exists in this directory.
- `remove_hue_power_service_admin.ps1`
  Removes the service.

## Install

Use the top-level interactive installer:

```powershell
powershell -ExecutionPolicy Bypass -File ..\quick-install.ps1
```

Or, if `service.json` already exists in this directory, install directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\install_hue_power_service_admin.ps1
```

## Remove

```powershell
powershell -ExecutionPolicy Bypass -File .\remove_hue_power_service_admin.ps1
```

## service.json

```json
{
  "bridge_ip": "192.168.1.111",
  "bridge_id": "...",
  "username": "...",
  "room_id": "81",
  "room_name": "Living room",
  "scene_id": "...",
  "scene_name": "Bright",
  "triggers": {
    "boot": true,
    "wake": true,
    "display_on": true,
    "display_off": true,
    "sleep": true,
    "shutdown": true
  }
}
```

After editing, restart the service:

```powershell
sc.exe stop HuePowerService
sc.exe start HuePowerService
```

## Log

`service.log` (next to the .exe).
