$startup = [Environment]::GetFolderPath("Startup")
$launcherPath = Join-Path $startup "HuePowerWatcher.cmd"
if (Test-Path $launcherPath) {
  Remove-Item $launcherPath -Force
  Write-Output "Removed $launcherPath"
} else {
  Write-Output "Launcher not found at $launcherPath"
}

