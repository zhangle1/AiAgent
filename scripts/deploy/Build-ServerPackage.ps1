[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\artifacts\server-package"),
    [string]$BackendApiUrl = "http://127.0.0.1:8081",
    [int]$FrontendPort = 8080,
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludePythonWorkers,
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$backendProject = Join-Path $projectRoot "backed\AiAgent.Backend.csproj"
$frontRoot = Join-Path $projectRoot "front"
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$stageRoot = Join-Path $outputRoot "AiAgent-server"
$zipPath = Join-Path $outputRoot "AiAgent-server.zip"
$frontendBuildRoot = Join-Path $outputRoot ".front-build"

if ($FrontendPort -lt 1024 -or $FrontendPort -gt 65535) {
    throw "FrontendPort must be between 1024 and 65535."
}
if (-not [Uri]::IsWellFormedUriString($BackendApiUrl, [UriKind]::Absolute)) {
    throw "BackendApiUrl must be an absolute URL, for example http://127.0.0.1:8081."
}

Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $frontendBuildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stageRoot, (Join-Path $stageRoot "backend"), (Join-Path $stageRoot "front") -Force | Out-Null

Write-Host "[1/4] Publishing backend..." -ForegroundColor Cyan
$publishArguments = @("publish", $backendProject, "-c", "Release", "-r", $RuntimeIdentifier, "-o", (Join-Path $stageRoot "backend"), "--self-contained:$($SelfContained.IsPresent.ToString().ToLowerInvariant())")
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "Backend publish failed with exit code $LASTEXITCODE." }

if (-not $IncludePythonWorkers) {
    Write-Host "Skipping Python workers and local virtual environments. Use -IncludePythonWorkers to include them." -ForegroundColor DarkYellow
    Remove-Item -LiteralPath (Join-Path $stageRoot "backend\Rag") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $stageRoot "backend\PythonWorkers") -Recurse -Force -ErrorAction SilentlyContinue
}

# Never place the current development/server secrets into the portable package.
Remove-Item -LiteralPath (Join-Path $stageRoot "backend\appsettings.json") -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $projectRoot "backed\appsettings.example.json") -Destination (Join-Path $stageRoot "backend\appsettings.Production.json.example") -Force

Write-Host "[2/4] Installing frontend dependencies and building standalone output..." -ForegroundColor Cyan
# Build in an isolated copy. A running `next dev` locks the native SWC module in
# front\node_modules, and `npm ci` would otherwise try to delete that locked file.
New-Item -ItemType Directory -Path $frontendBuildRoot -Force | Out-Null
Get-ChildItem -LiteralPath $frontRoot -Force |
    Where-Object { $_.Name -notin @("node_modules", ".next") } |
    Copy-Item -Destination $frontendBuildRoot -Recurse -Force
Push-Location $frontendBuildRoot
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    $env:NEXT_PUBLIC_AIAGENT_API_BASE_URL = $BackendApiUrl
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$standaloneRoot = Join-Path $frontendBuildRoot ".next\standalone"
if (-not (Test-Path -LiteralPath (Join-Path $standaloneRoot "server.js"))) {
    throw "Next.js standalone output was not created. Check front/next.config.js output setting."
}
Copy-Item -Path (Join-Path $standaloneRoot "*") -Destination (Join-Path $stageRoot "front") -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $stageRoot "front\.next") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $frontendBuildRoot ".next\static") -Destination (Join-Path $stageRoot "front\.next\static") -Recurse -Force
@"
{
  "backendApiUrl": ""
}
"@ | Set-Content -LiteralPath (Join-Path $stageRoot "front\api-proxy.json") -Encoding utf8
if (Test-Path -LiteralPath (Join-Path $frontendBuildRoot "public")) {
    Copy-Item -LiteralPath (Join-Path $frontendBuildRoot "public") -Destination (Join-Path $stageRoot "front\public") -Recurse -Force
}
$nodeCommand = Get-Command node.exe -CommandType Application -ErrorAction SilentlyContinue
if (-not $nodeCommand -or -not (Test-Path -LiteralPath $nodeCommand.Source)) {
    throw "node.exe was not found. Install Node.js on the build machine before creating the deployment package."
}
Copy-Item -LiteralPath $nodeCommand.Source -Destination (Join-Path $stageRoot "front\node.exe") -Force
Remove-Item -LiteralPath $frontendBuildRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "[3/4] Adding the server run script..." -ForegroundColor Cyan
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Run-AiAgent.ps1") -Destination (Join-Path $stageRoot "Run-AiAgent.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "DEPLOYMENT.md") -Destination (Join-Path $stageRoot "DEPLOYMENT.md") -Force
@"
FrontendPort=$FrontendPort
BackendApiUrl=$BackendApiUrl
RuntimeIdentifier=$RuntimeIdentifier
IncludePythonWorkers=$($IncludePythonWorkers.IsPresent)
SelfContained=$($SelfContained.IsPresent)
"@ | Set-Content -LiteralPath (Join-Path $stageRoot "package-info.txt") -Encoding utf8

Write-Host "[4/4] Creating zip package..." -ForegroundColor Cyan
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -LiteralPath $stageRoot -DestinationPath $zipPath -Force
Write-Host "Package complete: $zipPath" -ForegroundColor Green
