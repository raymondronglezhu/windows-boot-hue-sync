$ErrorActionPreference = "Stop"

$root = "C:\Users\Raymond\Documents\Smart_Home"
$source = Join-Path $root "automation\HuePowerService.cs"
$output = Join-Path $root "automation\HuePowerService.exe"
$candidates = @(
  "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
  throw "Could not find csc.exe"
}

& $csc /nologo /target:exe /out:$output /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll $source

if ($LASTEXITCODE -ne 0) {
  throw "csc.exe failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $output)) {
  throw "Build did not produce $output"
}

Write-Output "Built $output"
