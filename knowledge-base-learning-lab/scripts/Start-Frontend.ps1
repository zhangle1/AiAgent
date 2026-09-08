param(
    [int]$Port = 5173
)

$ErrorActionPreference = "Stop"
$frontendDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "frontend"

Set-Location $frontendDirectory

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm was not found. Install Node.js 18+ and restart PowerShell."
}

if (-not (Test-Path -LiteralPath (Join-Path $frontendDirectory "node_modules"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Cyan
    npm ci
}

Write-Host "Frontend is starting at http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host "Keep the backend running at http://127.0.0.1:8010." -ForegroundColor Yellow
npm run dev -- --host 127.0.0.1 --port $Port
