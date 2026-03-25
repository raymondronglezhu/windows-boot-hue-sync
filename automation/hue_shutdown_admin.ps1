param(
  [string]$Root = "C:\Users\Raymond\Documents\Smart_Home",
  [string]$RoomName = "Living room",
  [int]$MaxAttempts = 6,
  [int]$RetryDelaySeconds = 2
)

$ErrorActionPreference = "Stop"

$configPath = Join-Path $Root ".hue-agent\config.json"
$logPath = Join-Path $Root ".hue-agent\power-hooks.log"

function Write-Log {
  param([string]$Scope, [string]$Message)
  $logDir = Split-Path $logPath -Parent
  New-Item -ItemType Directory -Path $logDir -Force | Out-Null
  $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
  Add-Content -Path $logPath -Value "[$timestamp] [$Scope] $Message"
}

function Get-HueConfig {
  if (-not (Test-Path $configPath)) {
    throw "Hue config not found at $configPath"
  }
  return (Get-Content $configPath -Raw | ConvertFrom-Json)
}

function Get-HueState {
  param($Config)
  $uri = "http://{0}/api/{1}" -f $Config.bridge_ip, $Config.username
  return Invoke-RestMethod -Method Get -Uri $uri
}

function Resolve-RoomId {
  param($State, [string]$TargetRoomName)
  foreach ($property in $State.groups.PSObject.Properties) {
    $group = $property.Value
    if (($group.type -eq "Room" -or $group.type -eq "Zone") -and $group.name -eq $TargetRoomName) {
      return $property.Name
    }
  }
  throw "Room '$TargetRoomName' not found"
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
  try {
    Write-Log -Scope "shutdown" -Message "Attempt $attempt/$MaxAttempts"
    $config = Get-HueConfig
    $state = Get-HueState -Config $config
    $roomId = Resolve-RoomId -State $state -TargetRoomName $RoomName
    $uri = "http://{0}/api/{1}/groups/{2}/action" -f $config.bridge_ip, $config.username, $roomId
    $body = @{ on = $false } | ConvertTo-Json -Compress
    $result = Invoke-RestMethod -Method Put -Uri $uri -Body $body -ContentType "application/json"
    Write-Log -Scope "shutdown" -Message "Turned off '$RoomName': $($result | ConvertTo-Json -Compress)"
    exit 0
  } catch {
    Write-Log -Scope "shutdown" -Message "Attempt failed: $($_.Exception.Message)"
    if ($attempt -lt $MaxAttempts) {
      Start-Sleep -Seconds $RetryDelaySeconds
    }
  }
}

Write-Log -Scope "shutdown" -Message "Failed after $MaxAttempts attempts"
exit 1
