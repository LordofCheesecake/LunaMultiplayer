# Scripts\package.ps1
# Packages the built LmpClient output into a GameData zip ready for players to copy.
#
# Usage (from the repository root in PowerShell):
#   .\Scripts\package.ps1
#   .\Scripts\package.ps1 -Configuration Release   # default
#   .\Scripts\package.ps1 -Configuration Debug
#   .\Scripts\package.ps1 -OutputDir C:\drops
#
# The zip is written to <OutputDir>\LunaMultiplayer-<version>.zip and contains:
#   GameData\LunaMultiplayer\  (plugins, button, localization, part sync, icons, flags)
#   GameData\000_Harmony\      (Harmony patcher)
#
# Players only need to extract and merge GameData into their KSP install.
# No .NET SDK or Git is required on their machine.

param(
    [ValidateSet("Release","Debug")]
    [string]$Configuration = "Release",
    [string]$OutputDir = ".\dist"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path

# Derive version from LunaMultiplayer.version JSON
$versionFile = Join-Path $repoRoot "LunaMultiplayer.version"
$versionString = "unknown"
if (Test-Path $versionFile) {
    $v = (Get-Content $versionFile -Raw | ConvertFrom-Json).VERSION
    $versionString = "$($v.MAJOR).$($v.MINOR).$($v.PATCH)"
}

$zipName = "LunaMultiplayer-$versionString.zip"
$stagingDir = Join-Path $repoRoot ".package-staging"
$gameDataDir = Join-Path $stagingDir "GameData"
$pluginsDir = Join-Path $gameDataDir "LunaMultiplayer\Plugins"
$buttonDir = Join-Path $gameDataDir "LunaMultiplayer\Button"
$localizationDir = Join-Path $gameDataDir "LunaMultiplayer\Localization"
$partSyncDir = Join-Path $gameDataDir "LunaMultiplayer\PartSync"
$iconsDir = Join-Path $gameDataDir "LunaMultiplayer\Icons"
$flagsDir = Join-Path $gameDataDir "LunaMultiplayer\Flags"
$harmonyDir = Join-Path $gameDataDir "000_Harmony"

$clientBinDir = Join-Path $repoRoot "LmpClient\bin\$Configuration"

# Validate that the client has been built
if (-not (Test-Path (Join-Path $clientBinDir "LmpClient.dll"))) {
    Write-Error "LmpClient.dll not found in '$clientBinDir'. Build the client first:`n  dotnet build LmpClient\LmpClient.csproj -c $Configuration"
}

# Clean and recreate staging tree
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
foreach ($dir in @($pluginsDir, $buttonDir, $localizationDir, $partSyncDir, $iconsDir, $flagsDir, $harmonyDir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# Plugins: client DLLs + External\Dependencies DLLs (no subdirectories)
Get-ChildItem (Join-Path $clientBinDir "*.dll") | Copy-Item -Destination $pluginsDir
Get-ChildItem (Join-Path $repoRoot "External\Dependencies\*.dll") | Copy-Item -Destination $pluginsDir

# Harmony
Copy-Item (Join-Path $repoRoot "External\Dependencies\Harmony\000_Harmony\*") -Destination $harmonyDir -Recurse -Force

# Assets
Copy-Item (Join-Path $repoRoot "LmpClient\Resources\LMPButton.png") -Destination $buttonDir
Copy-Item (Join-Path $repoRoot "LmpClient\Localization\XML\*") -Destination $localizationDir -Recurse -Force
Copy-Item (Join-Path $repoRoot "LmpClient\ModuleStore\XML\*") -Destination $partSyncDir -Recurse -Force
Copy-Item (Join-Path $repoRoot "LmpClient\Resources\Icons\*") -Destination $iconsDir -Recurse -Force
Copy-Item (Join-Path $repoRoot "LmpClient\Resources\Flags\*") -Destination $flagsDir -Recurse -Force

# Write the zip
$outputPath = Join-Path (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Path (Join-Path $repoRoot $OutputDir) -Force)).Path $zipName
if (Test-Path $outputPath) { Remove-Item $outputPath -Force }
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $outputPath

# Clean up staging
Remove-Item $stagingDir -Recurse -Force

Write-Host ""
Write-Host "Package written to: $outputPath"
Write-Host ""
Write-Host "Players: extract the zip, then merge 'GameData' into your KSP folder."
