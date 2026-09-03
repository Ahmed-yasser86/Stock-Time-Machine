# Local verification gate: backend tests + frontend lint + frontend build.
# Run from the repository root:  .\scripts\verify.ps1
# Fails fast on the first failing stage.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> backend tests"
dotnet test backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj --nologo -v minimal /m:1

Write-Host "==> frontend lint"
Push-Location frontend
npm run lint
Write-Host "==> frontend build"
npm run build
Pop-Location

Write-Host "VERIFY OK"
