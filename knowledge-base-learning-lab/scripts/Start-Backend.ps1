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
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the Python virtual environment." }
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    throw "Unable to create .venv. Install Python 3.10+ and make sure the 'py' launcher is available."
}

& $venvPython -c "import sys
try:
    import fastapi, uvicorn
except ImportError as exc:
    print('Backend dependencies need installation: ' + str(exc))
    sys.exit(1)
"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing backend dependencies..." -ForegroundColor Cyan
    & $venvPython -m pip install -r requirements.txt
    if ($LASTEXITCODE -ne 0) { throw "Backend dependency installation failed. Check the pip output above and retry." }
}

Write-Host "Backend is starting at http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host "API documentation: http://127.0.0.1:$Port/docs" -ForegroundColor Green
& $venvPython -m uvicorn app:app --reload --host 127.0.0.1 --port $Port
if ($LASTEXITCODE -ne 0) { throw "Backend startup failed. Check the output above." }
