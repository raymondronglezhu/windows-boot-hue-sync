$ErrorActionPreference = "Stop"

$root = "C:\Users\Raymond\Documents\Smart_Home"
$startup = [Environment]::GetFolderPath("Startup")
$launcherPath = Join-Path $startup "HuePowerWatcher.cmd"
$pythonExe = (Get-Command python).Source
$pythonwExe = Join-Path (Split-Path $pythonExe) "pythonw.exe"
if (-not (Test-Path $pythonwExe)) {
  $pythonwExe = $pythonExe
}

$content = @"
@echo off
cd /d "$root"
start "" "$pythonwExe" "$root\automation\hue_power_watcher.py"
"@

Set-Content -Path $launcherPath -Value $content -Encoding ASCII
Write-Output "Installed launcher at $launcherPath"

