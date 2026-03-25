param(
  [string]$Root = "C:\Users\Raymond\Documents\Smart_Home",
  [string]$RoomName = "Living room",
  [string]$SceneName = "Bright",
  [int]$InitialDelaySeconds = 3,
  [int]$MaxAttempts = 20,
  [int]$RetryDelaySeconds = 3
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

function Resolve-SceneId {
  param($State, [string]$RoomId, [string]$TargetSceneName)
  foreach ($property in $State.scenes.PSObject.Properties) {
    $scene = $property.Value
    if ($scene.name -eq $TargetSceneName -and $scene.group -eq $RoomId) {
      return $property.Name
    }
  }
  throw "Scene '$TargetSceneName' not found for room id $RoomId"
}

Start-Sleep -Seconds $InitialDelaySeconds

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
  try {
    Write-Log -Scope "startup" -Message "Attempt $attempt/$MaxAttempts"
    $config = Get-HueConfig
    $state = Get-HueState -Config $config
    $roomId = Resolve-RoomId -State $state -TargetRoomName $RoomName
    $sceneId = Resolve-SceneId -State $state -RoomId $roomId -TargetSceneName $SceneName
    $uri = "http://{0}/api/{1}/groups/{2}/action" -f $config.bridge_ip, $config.username, $roomId
    $body = @{ scene = $sceneId } | ConvertTo-Json -Compress
    $result = Invoke-RestMethod -Method Put -Uri $uri -Body $body -ContentType "application/json"
    Write-Log -Scope "startup" -Message "Applied scene '$SceneName' to '$RoomName': $($result | ConvertTo-Json -Compress)"
    exit 0
  } catch {
    Write-Log -Scope "startup" -Message "Attempt failed: $($_.Exception.Message)"
    if ($attempt -lt $MaxAttempts) {
      Start-Sleep -Seconds $RetryDelaySeconds
    }
  }
}

Write-Log -Scope "startup" -Message "Failed after $MaxAttempts attempts"
exit 1
