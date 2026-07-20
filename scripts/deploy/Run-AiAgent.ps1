[CmdletBinding()]
param(
    [ValidateSet("Start", "Stop", "Restart")]
    [string]$Action = "Start",
    [int]$BackendPort = 5000,
    [int]$FrontendPort = 3782
)

$ErrorActionPreference = "Stop"
$packageRoot = $PSScriptRoot
$backendRoot = Join-Path $packageRoot "backend"
$frontRoot = Join-Path $packageRoot "front"
$runtimeRoot = Join-Path $packageRoot "runtime"
$backendPidPath = Join-Path $runtimeRoot "backend.pid"
$frontPidPath = Join-Path $runtimeRoot "front.pid"

function Stop-AiAgentProcesses {
    foreach ($item in @(@{ Name = "front"; PidPath = $frontPidPath }, @{ Name = "backend"; PidPath = $backendPidPath })) {
        if (-not (Test-Path -LiteralPath $item.PidPath)) { continue }
        $processId = [int](Get-Content -LiteralPath $item.PidPath -Raw)
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $processId -Force
            Write-Host "Stopped $($item.Name) process $processId."
        }
        Remove-Item -LiteralPath $item.PidPath -Force
    }
}

if ($Action -eq "Stop") {
    Stop-AiAgentProcesses
    return
}
foreach ($port in @($BackendPort, $FrontendPort)) {
    if ($port -lt 1024 -or $port -gt 65535) { throw "Port must be between 1024 and 65535." }
}
if (-not (Test-Path -LiteralPath (Join-Path $backendRoot "appsettings.Production.json"))) {
    throw "Missing backend\appsettings.Production.json. Copy appsettings.Production.json.example and fill in SQL Server, CORS and allowed roots first."
}
if (-not (Test-Path -LiteralPath (Join-Path $frontRoot "server.js"))) {
    throw "Missing front\server.js. Use the zip package produced by Build-ServerPackage.ps1."
}
$bundledNode = Join-Path $frontRoot "node.exe"
if (Test-Path -LiteralPath $bundledNode) {
    $nodeExe = $bundledNode
}
else {
    $nodeCommand = Get-Command node.exe -CommandType Application -ErrorAction SilentlyContinue
    if (-not $nodeCommand -or -not (Test-Path -LiteralPath $nodeCommand.Source)) {
        throw "Node.js was not found. Recreate and deploy the package so front\node.exe is included, or install Node.js on this server."
    }
    $nodeExe = $nodeCommand.Source
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
if ($Action -eq "Restart") {
    Stop-AiAgentProcesses
}
elseif ((Test-Path -LiteralPath $backendPidPath) -or (Test-Path -LiteralPath $frontPidPath)) {
    throw "AiAgent appears to be running. Use -Action Restart or -Action Stop first."
}

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://0.0.0.0:$BackendPort"
$backendExe = Join-Path $backendRoot "AiAgent.Backend.exe"
if (Test-Path -LiteralPath $backendExe) {
    $backend = Start-Process -FilePath $backendExe -WorkingDirectory $backendRoot -RedirectStandardOutput (Join-Path $runtimeRoot "backend.out.log") -RedirectStandardError (Join-Path $runtimeRoot "backend.err.log") -PassThru
}
else {
    $backendDll = Join-Path $backendRoot "AiAgent.Backend.dll"
    if (-not (Test-Path -LiteralPath $backendDll)) { throw "Backend executable was not found." }
    $backend = Start-Process -FilePath "dotnet" -ArgumentList @($backendDll) -WorkingDirectory $backendRoot -RedirectStandardOutput (Join-Path $runtimeRoot "backend.out.log") -RedirectStandardError (Join-Path $runtimeRoot "backend.err.log") -PassThru
}

try {
    $env:PORT = "$FrontendPort"
    $env:HOSTNAME = "0.0.0.0"
    $front = Start-Process -FilePath $nodeExe -ArgumentList @("server.js") -WorkingDirectory $frontRoot -RedirectStandardOutput (Join-Path $runtimeRoot "front.out.log") -RedirectStandardError (Join-Path $runtimeRoot "front.err.log") -PassThru
}
catch {
    if ($backend -and -not $backend.HasExited) { Stop-Process -Id $backend.Id -Force }
    throw
}

$backend.Id | Set-Content -LiteralPath $backendPidPath -Encoding ascii
$front.Id | Set-Content -LiteralPath $frontPidPath -Encoding ascii
Write-Host "AiAgent started. Frontend: http://0.0.0.0:$FrontendPort  Backend: http://0.0.0.0:$BackendPort/swagger" -ForegroundColor Green
