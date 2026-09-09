$ErrorActionPreference = "Stop"

$backendScript = Join-Path $PSScriptRoot "Start-Backend.ps1"
$frontendScript = Join-Path $PSScriptRoot "Start-Frontend.ps1"

Start-Process -FilePath "powershell.exe" -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", ('"' + $backendScript + '"')
) -WorkingDirectory (Split-Path -Parent $PSScriptRoot)

Start-Process -FilePath "powershell.exe" -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", ('"' + $frontendScript + '"')
) -WorkingDirectory (Split-Path -Parent $PSScriptRoot)

Write-Host "Backend and frontend terminals were opened." -ForegroundColor Green
Write-Host "Open http://127.0.0.1:5173 after both services are ready." -ForegroundColor Green
