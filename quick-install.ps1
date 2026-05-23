param(
  [switch]$Elevated,
  [string]$BridgeIp
)

$ErrorActionPreference = "Stop"

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

function Read-NumericChoice {
  param(
    [Parameter(Mandatory = $true)] [int]$Min,
    [Parameter(Mandatory = $true)] [int]$Max,
    [Parameter(Mandatory = $true)] [string]$Prompt
  )

  while ($true) {
    $raw = Read-Host $Prompt
    $value = 0
    if ([int]::TryParse($raw, [ref]$value)) {
      if ($value -ge $Min -and $value -le $Max) {
        return $value
      }
    }
    Write-Host "Enter a number between $Min and $Max." -ForegroundColor Yellow
  }
}

function Read-YesNo {
  param(
    [Parameter(Mandatory = $true)] [string]$Prompt,
    [Parameter(Mandatory = $true)] [bool]$Default
  )

  $hint = if ($Default) { "[Y/n]" } else { "[y/N]" }
  while ($true) {
    $raw = (Read-Host "$Prompt $hint").Trim().ToLowerInvariant()
    if ([string]::IsNullOrEmpty($raw)) { return $Default }
    if ($raw -eq "y" -or $raw -eq "yes") { return $true }
    if ($raw -eq "n" -or $raw -eq "no")  { return $false }
    Write-Host "Enter y or n." -ForegroundColor Yellow
  }
}

function Format-AgentArguments {
  param([string[]]$AgentArgs)

  $fullArgs = @("-m", "hue_agent") + $AgentArgs
  $quoted = foreach ($a in $fullArgs) {
    if ($a -match '[\s"]') {
      '"' + ($a -replace '"', '\"') + '"'
    } else {
      $a
    }
  }
  return ($quoted -join " ")
}

function Invoke-HueAgent {
  param(
    [Parameter(Mandatory = $true)] [string[]]$AgentArgs
  )

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName               = "python"
  $psi.Arguments              = Format-AgentArguments -AgentArgs $AgentArgs
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.UseShellExecute        = $false
  $psi.CreateNoWindow         = $true

  $proc = [System.Diagnostics.Process]::Start($psi)
  $stdout = $proc.StandardOutput.ReadToEnd()
  $stderr = $proc.StandardError.ReadToEnd()
  $proc.WaitForExit()
  $exitCode = $proc.ExitCode
  $proc.Dispose()

  return [pscustomobject]@{
    ExitCode = $exitCode
    Stdout   = $stdout
    Stderr   = $stderr
  }
}

function Get-AgentErrorMessage {
  param([string]$Stderr)

  if ([string]::IsNullOrWhiteSpace($Stderr)) { return "unknown error" }

  if ($Stderr -match '429.*Too Many Requests' -or $Stderr -match 'HTTP Error 429') {
    return "Hue cloud discovery is rate-limiting your network. Wait a few minutes and rerun this script."
  }

  try {
    $info = $Stderr | ConvertFrom-Json
    if ($info.error) { return $info.error }
  } catch {}

  $lastLine = ($Stderr -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)
  if ($lastLine) { return $lastLine.Trim() }
  return "unknown error"
}

function Invoke-HuePairWithCountdown {
  param(
    [Parameter(Mandatory = $true)] [int]$TimeoutSeconds,
    [Parameter(Mandatory = $true)] [string]$BridgeIp
  )

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName               = "python"
  $psi.Arguments              = Format-AgentArguments -AgentArgs @(
    "--bridge-ip", $BridgeIp, "pair", "--watch", "--timeout", $TimeoutSeconds.ToString()
  )
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.UseShellExecute        = $false
  $psi.CreateNoWindow         = $true

  $proc = [System.Diagnostics.Process]::Start($psi)
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  $lastShown = -1
  $maxLineLength = 0

  try {
    while (-not $proc.HasExited) {
      $remaining = [int]([Math]::Max(0, ($deadline - (Get-Date)).TotalSeconds))
      if ($remaining -ne $lastShown) {
        $line = "Now, press the button on your Hue Bridge to pair in the next $remaining seconds, go!"
        if ($line.Length -gt $maxLineLength) { $maxLineLength = $line.Length }
        [Console]::Write("`r" + $line.PadRight($maxLineLength))
        $lastShown = $remaining
      }
      Start-Sleep -Milliseconds 200
    }
  }
  finally {
    [Console]::Write("`r" + (' ' * $maxLineLength) + "`r")
  }

  $stdout   = $proc.StandardOutput.ReadToEnd()
  $stderr   = $proc.StandardError.ReadToEnd()
  $exitCode = $proc.ExitCode
  $proc.Dispose()

  return [pscustomobject]@{
    ExitCode = $exitCode
    Stdout   = $stdout
    Stderr   = $stderr
  }
}

function Read-OnOffToggle {
  param(
    [Parameter(Mandatory = $true)] [string]$EventClause,
    [Parameter(Mandatory = $true)] [ValidateSet("ON", "OFF")] [string]$Active,
    [Parameter(Mandatory = $true)] [bool]$Default
  )

  $esc    = [char]27
  $bright = "$esc[1;97m"
  $dim    = "$esc[38;2;60;60;60m"
  $reset  = "$esc[0m"

  $onMark  = if ($Active -eq "ON")  { "${bright}ON${reset}"  } else { "${dim}ON${reset}"  }
  $offMark = if ($Active -eq "OFF") { "${bright}OFF${reset}" } else { "${dim}OFF${reset}" }
  $hint = if ($Default) { "[Y/n]" } else { "[y/N]" }

  while ($true) {
    Write-Host "Turn lights $onMark/$offMark $EventClause $hint`: " -NoNewline
    $raw = ([Console]::ReadLine()).Trim().ToLowerInvariant()
    if ([string]::IsNullOrEmpty($raw)) { return $Default }
    if ($raw -eq "y" -or $raw -eq "yes") { return $true }
    if ($raw -eq "n" -or $raw -eq "no")  { return $false }
    Write-Host "Enter y or n." -ForegroundColor Yellow
  }
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$pairingConfigPath = Join-Path $repoRoot ".hue-agent\config.json"
$serviceConfigPath = Join-Path $repoRoot "automation\service.json"
$installScript = Join-Path $repoRoot "automation\install_hue_power_service_admin.ps1"

Push-Location $repoRoot
try {
  if (-not $Elevated) {
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
      throw "Python was not found on PATH. Install Python first, then rerun this script."
    }

    if (-not (Test-Path $pairingConfigPath)) {
      if ($BridgeIp) {
        Write-Host ""
        Write-Host "Skipping cloud discovery, using bridge IP: $BridgeIp" -ForegroundColor Cyan
        $resolvedBridgeIp = $BridgeIp
      } else {
        Write-Host ""
        Write-Host "Discovering Hue Bridges in your local network..." -ForegroundColor Cyan
        $disc = Invoke-HueAgent -AgentArgs @("discover")
        if ($disc.ExitCode -ne 0) {
          $tip = "If discovery is rate-limiting you, rerun with: .\quick-install.ps1 -BridgeIp <your-bridge-ip>"
          throw ("Hue bridge discovery failed: " + (Get-AgentErrorMessage -Stderr $disc.Stderr) + "`n" + $tip)
        }
        $bridges = @(($disc.Stdout | ConvertFrom-Json).bridges)
        if ($bridges.Count -eq 0) {
          throw "No Hue bridges found on your local network."
        }

        $deviceWord = if ($bridges.Count -eq 1) { "device" } else { "devices" }
        Write-Host ("Success - {0} {1} found:" -f $bridges.Count, $deviceWord) -ForegroundColor Green
        foreach ($bridge in $bridges) {
          Write-Host ("  * id: {0}" -f $bridge.bridge_id) -ForegroundColor Gray
        }

        $resolvedBridgeIp = $bridges[0].bridge_ip
      }
      $firstAttempt = $true
      while ($true) {
        if (-not $firstAttempt) {
          Write-Host "Timed out: press enter to try again." -ForegroundColor Yellow
          [Console]::ReadLine() | Out-Null
        }
        $firstAttempt = $false

        Write-Host ""
        $pairResult = Invoke-HuePairWithCountdown -TimeoutSeconds 90 -BridgeIp $resolvedBridgeIp
        if ($pairResult.ExitCode -eq 0) {
          Write-Host "Paired successfully." -ForegroundColor Green
          break
        }

        $errInfo = $null
        if (-not [string]::IsNullOrWhiteSpace($pairResult.Stderr)) {
          try { $errInfo = $pairResult.Stderr | ConvertFrom-Json } catch {}
        }

        if ($errInfo -and $errInfo.type -eq "HueLinkButtonNotPressed") {
          continue
        }

        throw ("Hue pairing failed: " + (Get-AgentErrorMessage -Stderr $pairResult.Stderr))
      }
    } else {
      Write-Host "Hue config already exists at $pairingConfigPath" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Listing rooms on your Hue bridge..." -ForegroundColor Cyan
    $roomsResult = Invoke-HueAgent -AgentArgs @("rooms")
    if ($roomsResult.ExitCode -ne 0) {
      throw ("Could not list Hue rooms: " + (Get-AgentErrorMessage -Stderr $roomsResult.Stderr))
    }
    $rooms = @(($roomsResult.Stdout | ConvertFrom-Json).rooms)

    Write-Host ""
    Write-Host "Choose the room to control:" -ForegroundColor Cyan
    Write-Host "  [1] All lights (every Hue light on the bridge)"
    for ($i = 0; $i -lt $rooms.Count; $i++) {
      Write-Host ("  [{0}] {1}" -f ($i + 2), $rooms[$i].name)
    }
    $roomChoice = Read-NumericChoice -Min 1 -Max ($rooms.Count + 1) -Prompt "Room number"

    if ($roomChoice -eq 1) {
      $chosenRoom  = [pscustomobject]@{ id = "0";  name = "All lights" }
      $chosenScene = [pscustomobject]@{ id = "";   name = "Last used" }
    } else {
      $chosenRoom = $rooms[$roomChoice - 2]

      Write-Host ""
      Write-Host ("Listing scenes in '{0}'..." -f $chosenRoom.name) -ForegroundColor Cyan
      $scenesResult = Invoke-HueAgent -AgentArgs @("scenes", "--room", $chosenRoom.name)
      if ($scenesResult.ExitCode -ne 0) {
        throw ("Could not list Hue scenes: " + (Get-AgentErrorMessage -Stderr $scenesResult.Stderr))
      }
      $allScenes = @(($scenesResult.Stdout | ConvertFrom-Json).scenes)
      $scenes = @($allScenes |
        Where-Object { $_.type -eq "GroupScene" -and (-not $_.recycle) -and $_.group_id -eq $chosenRoom.id } |
        Sort-Object name)

      Write-Host ""
      Write-Host "Choose which scene to be applied when lights turn on:" -ForegroundColor Cyan
      Write-Host "  [1] Last used scene (restore whatever was active before lights turned off)"
      for ($i = 0; $i -lt $scenes.Count; $i++) {
        Write-Host ("  [{0}] {1}" -f ($i + 2), $scenes[$i].name)
      }
      $sceneChoice = Read-NumericChoice -Min 1 -Max ($scenes.Count + 1) -Prompt "Scene number"
      if ($sceneChoice -eq 1) {
        $chosenScene = [pscustomobject]@{ id = ""; name = "Last used" }
      } else {
        $chosenScene = $scenes[$sceneChoice - 2]
      }
    }

    Write-Host ""
    Write-Host "By default, lights will be turned on / off at the following events:"
    Write-Host "  * Windows boot up / shut down"
    Write-Host "  * Wake / sleep"
    Write-Host "  * Screen on / off after inactivity"
    Write-Host ""
    $customize = Read-YesNo -Prompt "Would you like to remove any trigger events?" -Default $false

    $triggers = [ordered]@{
      boot        = $true
      shutdown    = $true
      wake        = $true
      sleep       = $true
      display_on  = $true
      display_off = $true
    }

    if ($customize) {
      $triggers["boot"]        = Read-OnOffToggle -EventClause "at Windows boot"                              -Active "ON"  -Default $true
      $triggers["shutdown"]    = Read-OnOffToggle -EventClause "when Windows shuts down"                      -Active "OFF" -Default $true
      $triggers["wake"]        = Read-OnOffToggle -EventClause "when Windows wakes from sleep"                -Active "ON"  -Default $true
      $triggers["sleep"]       = Read-OnOffToggle -EventClause "when Windows sleeps"                          -Active "OFF" -Default $true
      $triggers["display_on"]  = Read-OnOffToggle -EventClause "when display turns back on after inactivity"  -Active "ON"  -Default $true
      $triggers["display_off"] = Read-OnOffToggle -EventClause "when display turns off after inactivity"      -Active "OFF" -Default $true
    }

    $pairing = Get-Content $pairingConfigPath -Raw | ConvertFrom-Json

    $serviceConfig = [ordered]@{
      bridge_ip  = $pairing.bridge_ip
      bridge_id  = $pairing.bridge_id
      username   = $pairing.username
      room_id    = $chosenRoom.id
      room_name  = $chosenRoom.name
      scene_id   = $chosenScene.id
      scene_name = $chosenScene.name
      triggers   = $triggers
    }
    $serviceConfig | ConvertTo-Json -Depth 5 | Set-Content -Path $serviceConfigPath -Encoding UTF8

    Write-Host ""
    Write-Host "Wrote service config to $serviceConfigPath" -ForegroundColor Green
    Write-Host ("  Room:  {0}" -f $chosenRoom.name) -ForegroundColor Green
    Write-Host ("  Scene: {0}" -f $chosenScene.name) -ForegroundColor Green
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

  $installFailed = $false
  $installError  = $null
  try {
    Write-Host ""
    Write-Host "Installing Windows Boot Hue Sync service..." -ForegroundColor Cyan
    Invoke-CheckedCommand -ScriptBlock { powershell -NoProfile -ExecutionPolicy Bypass -File $installScript | Out-Host } -ErrorMessage "Service installation failed."

    $cfg = Get-Content $serviceConfigPath -Raw | ConvertFrom-Json
    $sceneLabel = if ([string]::IsNullOrEmpty($cfg.scene_id)) { "Last used" } else { $cfg.scene_name }
    $logPath    = Join-Path $repoRoot "automation\service.log"

    $triggerRows = @(
      @{ Key = "boot";        Label = "Windows boot";       Action = "ON"  }
      @{ Key = "shutdown";    Label = "Windows shut down";  Action = "OFF" }
      @{ Key = "wake";        Label = "Windows wake";       Action = "ON"  }
      @{ Key = "sleep";       Label = "Windows sleep";      Action = "OFF" }
      @{ Key = "display_on";  Label = "Display turns on";   Action = "ON"  }
      @{ Key = "display_off"; Label = "Display turns off";  Action = "OFF" }
    )

    Write-Host ""
    Write-Host "================================================" -ForegroundColor Green
    Write-Host "  Hue Power Service installed successfully" -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Service : HuePowerService (running)"
    Write-Host ("  Room    : {0}" -f $cfg.room_name)
    Write-Host ("  Scene   : {0}" -f $sceneLabel)
    Write-Host ""
    Write-Host "  Triggers:"
    foreach ($row in $triggerRows) {
      $enabled = $cfg.triggers.($row.Key)
      if ($enabled) {
        Write-Host ("    {0,-22}  lights {1}" -f $row.Label, $row.Action)
      } else {
        Write-Host ("    {0,-22}  (disabled)" -f $row.Label) -ForegroundColor DarkGray
      }
    }
    Write-Host ""
    Write-Host ("  Config  : {0}" -f $serviceConfigPath) -ForegroundColor Gray
    Write-Host ("  Log     : {0}" -f $logPath) -ForegroundColor Gray
    Write-Host ""
    Write-Host "Edit service.json and restart HuePowerService to change settings."
  } catch {
    $installFailed = $true
    $installError  = $_.Exception.Message
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Red
    Write-Host "  Hue Power Service install FAILED" -ForegroundColor Red
    Write-Host "================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host $installError -ForegroundColor Red
    Write-Host ""
    Write-Host "Check the service log if it exists:" -ForegroundColor Yellow
    Write-Host ("  {0}" -f (Join-Path $repoRoot "automation\service.log")) -ForegroundColor Yellow
  }
} finally {
  Pop-Location
  if ($Elevated) {
    try { [Console]::Out.Flush() } catch {}
    while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) }
    Write-Host ""
    $null = Read-Host "Press Enter to close this window"
  }
}

if ($installFailed) { exit 1 }
