# -*- coding: utf-8 -*-
# Build FullLogger.dll (Release)
param(
    [string]$TarkovDir = $env:EFT_GAME_ROOT
)

if (-not $TarkovDir) {
    $TarkovDir = "U:\Games\EscapeFromTarkov4"
    Write-Host "EFT_GAME_ROOT not set, using default: $TarkovDir"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet build (Join-Path $root "FullLogger.csproj") -c Release -p:TarkovDir="$TarkovDir\" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $root "bin\Release\FullLogger.dll"
Write-Host "Built: $dll"
