# Hue Power Automation

This setup supports two modes for one simple rule:

- when the PC starts, turn the Hue lights on
- when the PC shuts down, turn the Hue lights off

## User-Level Mode

This mode is already installed.

- Startup trigger: when you sign in
- Shutdown trigger: when Windows closes the watcher on logoff or shutdown

- `automation\hue_power_watcher.py` keeps a small background process alive
- `automation\install_hue_power_watcher.ps1` installs a Startup-folder launcher
- `automation\remove_hue_power_watcher.ps1` removes that launcher

## Preferred Admin Mode

This is now the recommended setup.

- trigger: real Windows service running as `LocalSystem`
- startup behavior: apply `Bright` to `Living room`
- shutdown behavior: handle Windows preshutdown and turn off `Living room`

- `automation\HuePowerService.cs` is the native service source
- `automation\build_hue_power_service.ps1` compiles it
- `automation\install_hue_power_service_admin.ps1` installs it
- `automation\remove_hue_power_service_admin.ps1` removes it

Install from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\install_hue_power_service_admin.ps1
```

Remove from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_service_admin.ps1
```

## Older Admin Script Mode

This older mode is kept for reference but is no longer the preferred path.

- Startup trigger: true Windows boot via Scheduled Task running as `SYSTEM`
- Shutdown trigger: Local Group Policy machine shutdown script

- `automation\hue_startup_admin.ps1` applies the `Bright` scene to `Living room`
- `automation\hue_shutdown_admin.ps1` turns off `Living room` with retries during shutdown
- `automation\install_hue_power_hooks_admin.ps1` installs the system hooks
- `automation\remove_hue_power_hooks_admin.ps1` removes the system hooks

## Install Admin-Level Mode

Run this from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\install_hue_power_hooks_admin.ps1
```

## Remove Admin-Level Mode

Run this from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_hooks_admin.ps1
```

## Install User-Level Mode

- `automation\hue_power_watcher.py` keeps a small background process alive
- `automation\install_hue_power_watcher.ps1` installs a Startup-folder launcher
- `automation\remove_hue_power_watcher.ps1` removes that launcher

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\install_hue_power_watcher.ps1
```

## Remove User-Level Mode

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_watcher.ps1
```

## Notes

- The admin-level hooks now use PowerShell directly instead of Python, so they no longer depend on the current user's Python alias path.
- `hue_startup_admin.ps1` retries a room-scene activation of `Bright` for `Living room` instead of turning lights on one by one.
- `hue_shutdown_admin.ps1` sends a single room-level off command with short retries so Windows has a better chance to wait for a quick local off request.
- The native service path is stronger than the older script path because it can register for preshutdown and ask Windows for bounded time before final shutdown.
- Admin-level hook logs now go to `.hue-agent\power-hooks.log`.
- The user-level watcher still logs to `.hue-agent\power-watcher.log`.
- Neither mode can react to sudden power loss.
