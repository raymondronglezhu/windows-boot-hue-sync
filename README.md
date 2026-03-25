# Windows Boot Hue Sync

`Windows Boot Hue Sync` is a focused Philips Hue automation for one job:

- when Windows boots, apply the `Bright` scene to `Living room`
- when Windows shuts down, turn `Living room` off

It uses a native Windows service for the actual boot/shutdown behavior and a tiny local Hue helper for pairing and room/scene discovery.

## What Is In This Repo

- `automation/HuePowerService.cs`
  A native Windows service that handles startup and preshutdown.
- `automation/install_hue_power_service_admin.ps1`
  Builds and installs the service from an elevated PowerShell window.
- `automation/remove_hue_power_service_admin.ps1`
  Removes the service.
- `hue_agent/`
  Minimal Hue bridge onboarding commands for discovery, pairing, room lookup, scene lookup, room off, and scene activation.

## Pair The Hue Bridge

Discover the bridge:

```powershell
python -m hue_agent discover
```

Press the physical button on the Hue bridge, then pair:

```powershell
python -m hue_agent pair --watch --timeout 60
```

The bridge credentials are stored in `.hue-agent/config.json`.

## Inspect The Room And Scene

Check the configured room and scene names:

```powershell
python -m hue_agent rooms
python -m hue_agent scenes --room "Living room"
```

This repo currently targets:

- room: `Living room`
- startup scene: `Bright`

## Install The Windows Service

Fast path:

```powershell
powershell -ExecutionPolicy Bypass -File .\quick-install.ps1
```

That script:

- pairs with the Hue bridge if `.hue-agent/config.json` does not exist yet
- asks for Administrator approval when it is time to install the Windows service
- runs the existing admin installer for you

Manual path:

Run this from an elevated PowerShell window:

```powershell
cd C:\Users\Raymond\Documents\Smart_Home
powershell -ExecutionPolicy Bypass -File .\automation\install_hue_power_service_admin.ps1
```

That installer:

- builds `automation\HuePowerService.exe`
- installs the `HuePowerService`
- starts it
- removes the older scheduled-task and shutdown-script hooks if they exist

## Remove The Windows Service

Run this from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_service_admin.ps1
```

## Logs

- service log: `.hue-agent\hue-power-service.log`

## Notes

- This relies on the local Hue bridge API on your home network.
- The startup action is scene-based for speed.
- The shutdown action uses Windows service preshutdown handling so Windows gives it bounded time to finish.
- Sudden power loss cannot be handled.
