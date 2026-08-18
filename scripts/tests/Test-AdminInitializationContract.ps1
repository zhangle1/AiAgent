$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

function Read-ProjectText([string]$relativePath) {
    return Get-Content -LiteralPath (Join-Path $projectRoot $relativePath) -Raw
}

function Assert-Contract([bool]$condition, [string]$message) {
    if (-not $condition) { throw "Auth initialization contract failed: $message" }
}

$authSource = Read-ProjectText "backed\Services\Auth\AuthService.cs"
$programSource = Read-ProjectText "backed\Program.cs"
$readme = Read-ProjectText "README.md"
$spec = Read-ProjectText "docs\admin-configuration-and-access-spec.md"
$deployment = Read-ProjectText "scripts\deploy\DEPLOYMENT.md"

Assert-Contract ($authSource.Contains('Authentication:InitialAdministratorUsername')) "initial administrator username must come from configuration"
Assert-Contract ($authSource.Contains('Authentication:InitialAdministratorPassword')) "initial administrator password must come from configuration"
Assert-Contract ($authSource.Contains('password.Length < 16')) "initial administrator password must have a high minimum length"
Assert-Contract ($authSource.Contains('item.Role == "admin" && !item.IsDisabled')) "an existing usable administrator must prevent initialization"
Assert-Contract (-not [regex]::IsMatch($authSource, 'CreateUserAsync\s*\(\s*username\s*,\s*"')) "administrator creation must not pass a hardcoded password"
Assert-Contract (-not $authSource.Contains('EnsureDefaultAdministratorAsync')) "the old default administrator initialization contract must be removed"
Assert-Contract ($programSource.Contains('EnsureInitialAdministratorAsync')) "startup must call the explicit initialization flow"
Assert-Contract ($programSource.Contains('Startup is aborted')) "initialization failure must abort startup"

foreach ($document in @(@{ Name = "README"; Text = $readme }, @{ Name = "admin spec"; Text = $spec }, @{ Name = "deployment guide"; Text = $deployment })) {
    Assert-Contract ($document.Text.Contains('Authentication:InitialAdministratorUsername')) "$($document.Name) must document the required username setting"
    Assert-Contract ($document.Text.Contains('Authentication:InitialAdministratorPassword')) "$($document.Name) must document the required password setting"
    Assert-Contract (-not $document.Text.Contains('superadmin')) "$($document.Name) must not advertise a fixed administrator username"
}

foreach ($relativePath in @("backed\appsettings.example.json", "backed\appsettings.dev.example.json")) {
    $settings = Get-Content -LiteralPath (Join-Path $projectRoot $relativePath) -Raw | ConvertFrom-Json
    Assert-Contract ($settings.Authentication.InitialAdministratorUsername -eq "") "$relativePath must not contain an administrator username"
    Assert-Contract ($settings.Authentication.InitialAdministratorPassword -eq "") "$relativePath must not contain an administrator password"
}

Write-Output "Admin initialization contract passed."
