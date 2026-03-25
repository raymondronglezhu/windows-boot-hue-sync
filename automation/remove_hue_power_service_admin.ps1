$ErrorActionPreference = "Stop"

function Assert-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
  }
}

Assert-Admin

$serviceName = "HuePowerService"

try {
  sc.exe stop $serviceName | Out-Null
} catch {
}

try {
  sc.exe delete $serviceName | Out-Null
  Write-Output "Removed service: $serviceName"
} catch {
  Write-Output "Service not found: $serviceName"
}

