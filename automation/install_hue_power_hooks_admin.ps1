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
$startupScript = Join-Path $root "automation\hue_startup_admin.ps1"
$shutdownScript = Join-Path $root "automation\hue_shutdown_admin.ps1"
$taskName = "HueLightsOnAtStartup"
$powershellExe = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"

if (-not (Test-Path $startupScript)) {
  throw "Startup script not found at $startupScript"
}

if (-not (Test-Path $shutdownScript)) {
  throw "Shutdown script not found at $shutdownScript"
}

$action = New-ScheduledTaskAction -Execute $powershellExe -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$startupScript`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

$shutdownDir = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\Shutdown"
New-Item -ItemType Directory -Path $shutdownDir -Force | Out-Null

$shutdownCmdPath = Join-Path $shutdownDir "HueLightsOff.cmd"
$shutdownCmd = @"
@echo off
"$powershellExe" -NoProfile -ExecutionPolicy Bypass -File "$shutdownScript"
"@
Set-Content -Path $shutdownCmdPath -Value $shutdownCmd -Encoding ASCII

$gptIniPath = "$env:WINDIR\System32\GroupPolicy\gpt.ini"
if (-not (Test-Path $gptIniPath)) {
  Set-Content -Path $gptIniPath -Value "[General]`r`nVersion=1`r`n" -Encoding ASCII
}

$scriptsIniPath = "$env:WINDIR\System32\GroupPolicy\Machine\Scripts\scripts.ini"
$scriptsIni = @"
[Shutdown]
0CmdLine=HueLightsOff.cmd
0Parameters=
"@
Set-Content -Path $scriptsIniPath -Value $scriptsIni -Encoding ASCII

gpupdate /target:computer /force | Out-Null

Write-Output "Installed scheduled task: $taskName"
Write-Output "Installed shutdown script: $shutdownCmdPath"
Write-Output "Startup now runs at true system boot; shutdown runs at Windows shutdown."
