[CmdletBinding()]
param(
  [string]$Workspace = (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent),
  [string]$Profile = 'aiagent-remote',
  [string]$DshRoot,
  [ValidateRange(1, 65535)]
  [int]$Port = 3080,
  [switch]$OpenBrowser
)

$ErrorActionPreference = 'Stop'
$workspacePath = (Resolve-Path -LiteralPath $Workspace).Path
if ([string]::IsNullOrWhiteSpace($DshRoot)) {
  $DshRoot = Join-Path (Split-Path $workspacePath -Parent) 'deepseek-harness'
}

$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $listener) {
  throw "Port $Port is already in use by PID $($listener.OwningProcess). Stop that process or choose a different port."
}

$dshArguments = @('--profile', $Profile, '--port', $Port)
if (-not $OpenBrowser) { $dshArguments += '--no-open' }

try {
  Push-Location $workspacePath
  if (Test-Path -LiteralPath (Join-Path $DshRoot 'package.json')) {
    & npx --yes pnpm@11.7.0 -C $DshRoot dsh @dshArguments
  } else {
    & dsh @dshArguments
  }
} finally {
  Pop-Location
}

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
