# dev.ps1 - One-click build + launch for Ashlands Reborn
# Usage: .\dev.ps1
#   Builds the plugin, then launches Valheim with BepInEx loaded from the r2modman profile.
#
# How it works:
#   Unity Doorstop (winhttp.dll) must live next to valheim.exe, and BepInEx resolves
#   its plugin/config paths relative to the exe directory's BepInEx/ folder.
#   r2modman solves this by copying doorstop files into the game dir at launch.
#   We replicate this by pointing the game dir's BepInEx/ at the profile via a
#   directory junction (no admin required, instant, no file duplication).

$ErrorActionPreference = "Stop"

# --- Build ---
Push-Location "$PSScriptRoot\AshlandsReborn"
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed - aborting." -ForegroundColor Red
        exit 1
    }
    Write-Host "Build succeeded." -ForegroundColor Green
}
finally {
    Pop-Location
}

# --- Paths ---
$profilePath = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn"
$gamePath = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
$gameBepInEx = "$gamePath\BepInEx"
$gameBepInExBackup = "$gamePath\BepInEx_vanilla"
$profileBepInEx = "$profilePath\BepInEx"

if (-not (Test-Path "$gamePath\valheim.exe")) {
    Write-Host 'ERROR: valheim.exe not found. Update $gamePath in this script.' -ForegroundColor Red
    exit 1
}

# --- Deploy Doorstop ---
Copy-Item "$profilePath\winhttp.dll" "$gamePath\winhttp.dll" -Force
Copy-Item "$profilePath\doorstop_config.ini" "$gamePath\doorstop_config.ini" -Force

# --- Create BepInEx junction to profile ---
# If it's already a junction pointing to our profile, leave it alone.
# If it's a real directory (vanilla BepInEx), rename it as backup first.
if (Test-Path $gameBepInEx) {
    $item = Get-Item $gameBepInEx -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        # Already a junction - remove it so we can recreate (target may have changed)
        cmd /c rmdir $gameBepInEx
    }
    else {
        # Real directory - back it up
        if (Test-Path $gameBepInExBackup) {
            Write-Host "Backup already exists at $gameBepInExBackup" -ForegroundColor Yellow
            # Remove the real dir since we have a backup
            Remove-Item $gameBepInEx -Recurse -Force
        }
        else {
            Rename-Item $gameBepInEx $gameBepInExBackup
            Write-Host "Backed up vanilla BepInEx to BepInEx_vanilla" -ForegroundColor Yellow
        }
    }
}

cmd /c mklink /J $gameBepInEx $profileBepInEx
Write-Host "Linked BepInEx -> profile" -ForegroundColor Cyan

# --- Launch ---
Write-Host "Launching Valheim via Steam URI..." -ForegroundColor Cyan
Start-Process "steam://rungameid/892970"
