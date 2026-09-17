[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$Workspace,
  [string]$Profile = 'aiagent-remote',
  [string]$DshRoot = (Join-Path (Split-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) -Parent) 'deepseek-harness')
)

$ErrorActionPreference = 'Stop'
$workspacePath = (Resolve-Path $Workspace).Path

try {
  Push-Location $workspacePath
  if (Test-Path -LiteralPath (Join-Path $DshRoot 'package.json')) {
    & npx --yes pnpm@11.7.0 -C $DshRoot dsh --profile $Profile
  } else {
    & dsh --profile $Profile
  }
} finally {
  Pop-Location
}

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
