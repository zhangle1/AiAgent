param(
    [int]$Port = 5173
)

$ErrorActionPreference = "Stop"
$frontendDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "frontend"

Set-Location $frontendDirectory

if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw "npm was not found. Install Node.js 22.12+ and restart PowerShell."
}

if (-not (Test-Path -LiteralPath (Join-Path $frontendDirectory "node_modules\.bin\vite.cmd"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Cyan
    if (Test-Path -LiteralPath (Join-Path $frontendDirectory "package-lock.json")) {
        npm.cmd ci
    } else {
        npm.cmd install
    }
    if ($LASTEXITCODE -ne 0) { throw "Frontend dependency installation failed. Check the npm output above and retry." }
}

Write-Host "Frontend is starting at http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host "Keep the backend running at http://127.0.0.1:8010." -ForegroundColor Yellow
npm.cmd run dev -- --host 127.0.0.1 --port $Port --strictPort
if ($LASTEXITCODE -ne 0) { throw "Frontend startup failed. Check the output above." }
