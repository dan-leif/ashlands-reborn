# revert-vanilla.ps1 - return the Steam Valheim install to pure vanilla.
#
# Removes everything dev.ps1 (and an earlier manual BepInEx install) put into the game
# folder, archives the bits worth keeping first, and asks Steam to verify game files.
# The r2modman profile and this repo are NOT touched - see RESUME.md for how to come back.
#
# Usage:  powershell -ExecutionPolicy Bypass -File revert-vanilla.ps1
#
# JUNCTION SAFETY: <game>\BepInEx is a directory junction INTO the r2modman profile.
# It must be removed with `rmdir` (unlinks the junction). `Remove-Item -Recurse` on it
# follows the link and deletes the profile's plugins/config. This script guards for that.

$ErrorActionPreference = "Stop"

$gamePath    = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
$profilePath = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn"
$archiveRoot = "C:\DEV\ashlands-reborn-archive"
$archiveDir  = Join-Path $archiveRoot (Get-Date -Format 'yyyy-MM-dd')

if (-not (Test-Path "$gamePath\valheim.exe")) {
    Write-Host "ERROR: valheim.exe not found at $gamePath" -ForegroundColor Red
    exit 1
}

# --- 1. Stop the game ---
Stop-Process -Name valheim -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# --- 2. Archive before deleting anything ---
New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null
Write-Host "Archiving to $archiveDir" -ForegroundColor Cyan

$bvDir = "$gamePath\BepInEx_vanilla"
$bvZip = Join-Path $archiveDir "BepInEx_vanilla-from-game-dir.zip"
if (Test-Path $bvDir) {
    $bvItem = Get-Item $bvDir -Force
    if ($bvItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host "ERROR: BepInEx_vanilla is a reparse point - refusing to archive/delete it." -ForegroundColor Red
        exit 1
    }
    Compress-Archive -Path "$bvDir\*" -DestinationPath $bvZip -Force
    Write-Host "  zipped BepInEx_vanilla -> $bvZip"
}

foreach ($f in @("doorstop_config.ini", "winhttp.dll")) {
    if (Test-Path "$gamePath\$f") { Copy-Item "$gamePath\$f" $archiveDir -Force }
}

$cfgArchive = Join-Path $archiveDir "profile-config"
New-Item -ItemType Directory -Force -Path $cfgArchive | Out-Null
Copy-Item "$profilePath\BepInEx\config\*.cfg" $cfgArchive -Force
Copy-Item "$profilePath\mods.yml" $archiveDir -Force

$pkg = "$PSScriptRoot\AshlandsReborn\bin\Debug\AshlandsReborn.zip"
if (Test-Path $pkg) {
    Copy-Item $pkg (Join-Path $archiveDir "AshlandsReborn-thunderstore-package.zip") -Force
    Write-Host "  copied Thunderstore package"
}

# --- 3. Unlink the BepInEx junction (never recurse into it) ---
$gameBepInEx = "$gamePath\BepInEx"
if (Test-Path $gameBepInEx) {
    $item = Get-Item $gameBepInEx -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        cmd /c rmdir "$gameBepInEx"
        if (Test-Path $gameBepInEx) { Write-Host "ERROR: junction still present" -ForegroundColor Red; exit 1 }
        Write-Host "Unlinked BepInEx junction (profile untouched)" -ForegroundColor Green
    }
    else {
        Write-Host "ERROR: $gameBepInEx is a REAL directory, not the expected junction. Inspect it manually; not deleting." -ForegroundColor Red
        exit 1
    }
}

# --- 4. Delete mod-loader files ---
# Loader files + mod-manager leftovers (BepInExPack changelog, r2modman mods.yml backup,
# Thunderstore Mod Manager marker). steam_appid.txt is vanilla - leave it.
$loaderFiles = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version",
                 "start_game_bepinex.sh", "start_server_bepinex.sh",
                 "changelog.txt", "mods.yml.bak", ".thunderstoremm")
foreach ($f in $loaderFiles) {
    $p = "$gamePath\$f"
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "Deleted $f" }
}
if (Test-Path "$gamePath\doorstop_libs") {
    Remove-Item "$gamePath\doorstop_libs" -Recurse -Force
    Write-Host "Deleted doorstop_libs\"
}
if (Test-Path $bvDir) {
    if (-not (Test-Path $bvZip)) { Write-Host "ERROR: archive zip missing, not deleting BepInEx_vanilla" -ForegroundColor Red; exit 1 }
    Remove-Item $bvDir -Recurse -Force
    Write-Host "Deleted BepInEx_vanilla\ (archived)"
}

# --- 5. Sanity: profile still intact ---
$pluginDll = "$profilePath\BepInEx\plugins\Dan Moore-Ashlands Reborn\AshlandsReborn\AshlandsReborn.dll"
if (Test-Path $pluginDll) { Write-Host "Profile intact: $pluginDll" -ForegroundColor Green }
else { Write-Host "WARNING: profile plugin dll not found at $pluginDll" -ForegroundColor Yellow }

# --- 6. Report leftovers ---
$leftovers = Get-ChildItem $gamePath -Force | Where-Object {
    $_.Name -match '^(BepInEx|winhttp\.dll|doorstop|\.doorstop|start_.*bepinex)' }
if ($leftovers) {
    Write-Host "Mod-loader leftovers still in game dir:" -ForegroundColor Yellow
    $leftovers | ForEach-Object { Write-Host "  $($_.Name)" }
} else {
    Write-Host "Game dir clean: no mod-loader files remain." -ForegroundColor Green
}

# --- 7. Ask Steam to verify game files ---
Write-Host "Requesting Steam file verification (steam://validate/892970)..." -ForegroundColor Cyan
Start-Process "steam://validate/892970"
Write-Host "Done. Watch Steam's Downloads page for the validation to finish." -ForegroundColor Cyan
