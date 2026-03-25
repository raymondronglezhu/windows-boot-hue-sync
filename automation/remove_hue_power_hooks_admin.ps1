$ErrorActionPreference = "Stop"

function Assert-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
  }
}

Assert-Admin

$taskName = "HueLightsOnAtStartup"
try {
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
  Write-Output "Removed scheduled task: $taskName"
} catch {
  Write-Output "Scheduled task not found: $taskName"
}

$shutdownCmdPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\Shutdown\HueLightsOff.cmd"
$scriptsIniPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\scripts.ini"

if (Test-Path $shutdownCmdPath) {
  Remove-Item $shutdownCmdPath -Force
  Write-Output "Removed shutdown script: $shutdownCmdPath"
}

if (Test-Path $scriptsIniPath) {
  Remove-Item $scriptsIniPath -Force
  Write-Output "Removed script registration: $scriptsIniPath"
}

gpupdate /target:computer /force | Out-Null

