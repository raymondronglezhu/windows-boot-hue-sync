$ErrorActionPreference = "Stop"

param(
  [switch]$Elevated
)

function Test-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)]
    [scriptblock]$ScriptBlock,
    [Parameter(Mandatory = $true)]
    [string]$ErrorMessage
  )

  & $ScriptBlock
  if ($LASTEXITCODE -ne 0) {
    throw $ErrorMessage
  }
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $repoRoot ".hue-agent\config.json"
$installScript = Join-Path $repoRoot "automation\install_hue_power_service_admin.ps1"

Push-Location $repoRoot
try {
  if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw "Python was not found on PATH. Install Python first, then rerun this script."
  }

  if (-not (Test-Path $configPath)) {
    Write-Host "No Hue config found. Discovering the bridge..." -ForegroundColor Cyan
    Invoke-CheckedCommand -ScriptBlock { python -m hue_agent discover } -ErrorMessage "Hue bridge discovery failed."

    Write-Host ""
    Write-Host "Press the button on your Hue bridge now. Pairing will wait up to 90 seconds..." -ForegroundColor Yellow
    Invoke-CheckedCommand -ScriptBlock { python -m hue_agent pair --watch --timeout 90 } -ErrorMessage "Hue pairing failed."
  } else {
    Write-Host "Hue config already exists at $configPath" -ForegroundColor Green
  }

  if (-not (Test-Admin)) {
    Write-Host ""
    Write-Host "Requesting Administrator approval to install the Windows service..." -ForegroundColor Yellow

    $quotedScriptPath = '"' + $MyInvocation.MyCommand.Path + '"'
    $arguments = @(
      "-NoProfile"
      "-ExecutionPolicy"
      "Bypass"
      "-File"
      $quotedScriptPath
      "-Elevated"
    )

    $process = Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
      throw "Elevated install failed with exit code $($process.ExitCode)."
    }
    return
  }

  if (-not (Test-Path $installScript)) {
    throw "Install script not found: $installScript"
  }

  Write-Host ""
  Write-Host "Installing Windows Boot Hue Sync service..." -ForegroundColor Cyan
  Invoke-CheckedCommand -ScriptBlock { powershell -NoProfile -ExecutionPolicy Bypass -File $installScript } -ErrorMessage "Service installation failed."

  Write-Host ""
  Write-Host "Windows Boot Hue Sync is installed." -ForegroundColor Green
  Write-Host "Boot, wake, and display-on: apply the 'Bright' scene to 'Living room'." -ForegroundColor Green
  Write-Host "Display-off, sleep, and shutdown: turn 'Living room' off." -ForegroundColor Green
} finally {
  Pop-Location
}
