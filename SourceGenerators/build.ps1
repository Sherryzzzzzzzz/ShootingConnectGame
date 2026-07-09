# Build script for ShootingGame Source Generators
# Run this script to compile the Source Generator DLL and copy it to the correct Unity package location.
#
# Prerequisites: .NET 8+ SDK installed
# Usage: ./build.ps1

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ScriptDir "ShootingGame.SourceGen.csproj"
$OutputDir = Join-Path $ScriptDir "bin\Release\netstandard2.0"
$TargetDir = Join-Path $ScriptDir "..\Packages\com.shootinggame.network\Analyzers"

Write-Host "Building ShootingGame Source Generators..." -ForegroundColor Cyan

# Restore and build
dotnet restore $ProjectFile
dotnet build $ProjectFile -c Release

# Create target directory
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir | Out-Null
}

# Copy DLL
$dllPath = Join-Path $OutputDir "ShootingGame.SourceGen.dll"
if (Test-Path $dllPath) {
    Copy-Item $dllPath $TargetDir -Force
    Write-Host "Copied ShootingGame.SourceGen.dll to $TargetDir" -ForegroundColor Green
} else {
    Write-Host "ERROR: DLL not found at $dllPath" -ForegroundColor Red
    exit 1
}

Write-Host "Done!" -ForegroundColor Green
