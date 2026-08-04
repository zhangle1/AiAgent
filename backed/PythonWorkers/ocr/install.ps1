param(
    [string]$PythonPath = "python",
    [string]$VirtualEnvPath = (Join-Path $PSScriptRoot ".venv")
)

$ErrorActionPreference = "Stop"
$venvPython = Join-Path $VirtualEnvPath "Scripts/python.exe"
if (-not (Test-Path $venvPython)) {
    & $PythonPath -m venv $VirtualEnvPath
}

& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install -r (Join-Path $PSScriptRoot "requirements.txt")
