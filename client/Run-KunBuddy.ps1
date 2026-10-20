param(
    [ValidateSet('dev', 'preview')]
    [string]$Mode = 'dev',
    [string]$DataDir
)

$ErrorActionPreference = 'Stop'
$clientRoot = $PSScriptRoot
$previousDataDir = $env:KUNBUDDY_DATA_DIR
Push-Location -LiteralPath $clientRoot
try {
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        throw 'pnpm is required. Install Node.js and pnpm, then run pnpm install --frozen-lockfile in client/.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $clientRoot 'node_modules'))) {
        throw 'Dependencies are missing. Run pnpm install --frozen-lockfile in client/ first.'
    }
    if ($DataDir) {
        $profilePath = if ([System.IO.Path]::IsPathRooted($DataDir)) { $DataDir } else { Join-Path $clientRoot $DataDir }
        $env:KUNBUDDY_DATA_DIR = [System.IO.Path]::GetFullPath($profilePath)
    }
    & pnpm $Mode
    if ($LASTEXITCODE -ne 0) { throw "KunBuddy $Mode exited with code $LASTEXITCODE." }
}
finally {
    $env:KUNBUDDY_DATA_DIR = $previousDataDir
    Pop-Location
}
