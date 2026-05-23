$ErrorActionPreference = "Stop"

function Assert-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
  }
}

Assert-Admin

$buildScript = Join-Path $PSScriptRoot "build_hue_power_service.ps1"
$serviceExe = Join-Path $PSScriptRoot "HuePowerService.exe"
$serviceName = "HuePowerService"

try {
  sc.exe stop $serviceName 2>$null | Out-Null
} catch {
}

try {
  sc.exe delete $serviceName 2>$null | Out-Null
  Start-Sleep -Seconds 2
} catch {
}

powershell -ExecutionPolicy Bypass -File $buildScript | Out-Null

sc.exe create $serviceName binPath= "`"$serviceExe`"" start= auto DisplayName= "Hue Power Service" depend= Tcpip | Out-Null
sc.exe description $serviceName "Turns Hue lights on at startup, wake, and display-on, and off during display-off, sleep, and Windows shutdown." | Out-Null
& $serviceExe --configure-preshutdown
sc.exe start $serviceName | Out-Null

$legacyTask = Get-ScheduledTask -TaskName "HueLightsOnAtStartup" -ErrorAction SilentlyContinue
if ($legacyTask) {
  Unregister-ScheduledTask -TaskName "HueLightsOnAtStartup" -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
}

$shutdownCmdPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\Shutdown\HueLightsOff.cmd"
$scriptsIniPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\scripts.ini"
$removedLegacy = $false
if (Test-Path $shutdownCmdPath) {
  Remove-Item $shutdownCmdPath -Force
  $removedLegacy = $true
}
if (Test-Path $scriptsIniPath) {
  Remove-Item $scriptsIniPath -Force
  $removedLegacy = $true
}
if ($removedLegacy) {
  gpupdate /target:computer /force | Out-Null
}

Write-Output "Installed and started service: $serviceName"
