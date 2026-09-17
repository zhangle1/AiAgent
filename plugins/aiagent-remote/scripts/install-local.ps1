[CmdletBinding()]
param(
  [string]$Profile = 'aiagent-remote',
  [string]$DshRoot = (Join-Path (Split-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) -Parent) 'deepseek-harness')
)

$ErrorActionPreference = 'Stop'
$pluginRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Dsh {
  param([string[]]$Arguments)

  if (Test-Path -LiteralPath (Join-Path $DshRoot 'package.json')) {
    Push-Location $DshRoot
    try { & npx --yes pnpm@11.7.0 dsh @Arguments } finally { Pop-Location }
  } else {
    & dsh @Arguments
  }

  if ($LASTEXITCODE -ne 0) { throw "DSH command failed with exit code $LASTEXITCODE" }
}

Write-Host 'Building the local plugin package...'
Push-Location $pluginRoot
try { & npm run build } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE" }

# A web profile gives this local integration run a normal interactive DSH surface.
$dshHome = $env:DSH_HOME
if ([string]::IsNullOrWhiteSpace($dshHome)) {
  $dshHome = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dsh'
}
$profileManifest = Join-Path $dshHome "profiles\$Profile\package.json"
if (Test-Path -LiteralPath $profileManifest) {
  Invoke-Dsh @('--profile', $Profile, '--help')
} else {
  Invoke-Dsh @('--profile', $Profile, '--from-default-profile', 'web', '--help')
}
Invoke-Dsh @('plugin', '--profile', $Profile, 'add', $pluginRoot)
Invoke-Dsh @('--profile', $Profile, '--dump-config')

Write-Host ''
Write-Host "Installed @aiagent/deepseek-plugin-remote into DSH profile '$Profile'."
Write-Host "Start the local end-to-end flow with:"
Write-Host ".\scripts\start-local.ps1 -Workspace <your-test-folder> -Profile $Profile"
