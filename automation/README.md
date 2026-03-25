# Hue Power Service

This folder contains the final Windows service implementation for one feature:

- apply `Bright` to `Living room` at boot
- turn `Living room` off at shutdown

## Files

- `HuePowerService.cs`
  Native Windows service source.
- `build_hue_power_service.ps1`
  Compiles `HuePowerService.exe`.
- `install_hue_power_service_admin.ps1`
  Installs the service from an elevated PowerShell window.
- `remove_hue_power_service_admin.ps1`
  Removes the service.

## Install

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\install_hue_power_service_admin.ps1
```

## Remove

```powershell
powershell -ExecutionPolicy Bypass -File .\automation\remove_hue_power_service_admin.ps1
```

## Behavior

- startup: apply `Bright` to `Living room`
- shutdown: use Windows preshutdown handling and turn off `Living room`

## Log

- `.hue-agent\hue-power-service.log`
