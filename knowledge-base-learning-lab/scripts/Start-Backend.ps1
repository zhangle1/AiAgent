param(
    [int]$Port = 8010
)

$ErrorActionPreference = "Stop"
$backendDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "backend"
$venvPython = Join-Path $backendDirectory ".venv\Scripts\python.exe"

Set-Location $backendDirectory

if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Host "Creating Python virtual environment..." -ForegroundColor Cyan
    py -3 -m venv .venv
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    throw "Unable to create .venv. Install Python 3.10+ and make sure the 'py' launcher is available."
}

$fastApiInstalled = & $venvPython -c "import fastapi, uvicorn" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing backend dependencies..." -ForegroundColor Cyan
    & $venvPython -m pip install -r requirements.txt
}

Write-Host "Backend is starting at http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host "API documentation: http://127.0.0.1:$Port/docs" -ForegroundColor Green
& $venvPython -m uvicorn app:app --reload --host 127.0.0.1 --port $Port
