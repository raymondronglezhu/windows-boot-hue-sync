$ErrorActionPreference = "Stop"

function Assert-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
  }
}

Assert-Admin

$root = "C:\Users\Raymond\Documents\Smart_Home"
$buildScript = Join-Path $root "automation\build_hue_power_service.ps1"
$serviceExe = Join-Path $root "automation\HuePowerService.exe"
$serviceName = "HuePowerService"

try {
  sc.exe stop $serviceName | Out-Null
} catch {
}

try {
  sc.exe delete $serviceName | Out-Null
  Start-Sleep -Seconds 2
} catch {
}

powershell -ExecutionPolicy Bypass -File $buildScript | Out-Null

sc.exe create $serviceName binPath= "`"$serviceExe`"" start= auto DisplayName= "Hue Power Service" depend= Tcpip | Out-Null
sc.exe description $serviceName "Turns Hue lights on at startup and off during Windows shutdown." | Out-Null
& $serviceExe --configure-preshutdown
sc.exe start $serviceName | Out-Null

try {
  schtasks /Delete /TN "HueLightsOnAtStartup" /F | Out-Null
} catch {
}

$shutdownCmdPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\Shutdown\HueLightsOff.cmd"
$scriptsIniPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\scripts.ini"
if (Test-Path $shutdownCmdPath) {
  Remove-Item $shutdownCmdPath -Force
}
if (Test-Path $scriptsIniPath) {
  Remove-Item $scriptsIniPath -Force
}
gpupdate /target:computer /force | Out-Null

Write-Output "Installed and started service: $serviceName"
Write-Output "Removed old task/shutdown-script hooks if present"
