param(
    [Parameter(Mandatory=$false)]
    [string]$GamePath,
    # BepInEx core dir used when the game dir has no BepInEx\core (e.g. after revert-vanilla.ps1).
    # Defaults to the r2modman "Ashlands Reborn" profile.
    [Parameter(Mandatory=$false)]
    [string]$BepInExCorePath
)

# CopyRefs.ps1 - freeze the game + BepInEx reference assemblies into Lib\ so the
# project builds without a modded game folder (AshlandsReborn.csproj falls back to Lib\
# when $(GamePath)\BepInEx\core\BepInEx.dll does not exist).
#
# WARNING: this OVERWRITES Lib\. If Lib\ was frozen against an older game build on
# purpose (see RESUME.md), only run this once you are ready to port to the new build.

# Resolve path
if (-not $GamePath) {
    $GamePath = [Microsoft.Win32.Registry]::GetValue("HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 892970", "InstallLocation", $null)
}
if (-not $GamePath) {
    $GamePath = [Microsoft.Win32.Registry]::GetValue("HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", $null)
    if ($GamePath) { $GamePath = Join-Path $GamePath "steamapps\common\Valheim" }
}
if (-not $GamePath) {
    $GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
}

# BepInEx core: prefer the game dir (present while modded), else the r2modman profile.
$gameCore = Join-Path $GamePath "BepInEx\core"
if (-not $BepInExCorePath) {
    if (Test-Path (Join-Path $gameCore "BepInEx.dll")) {
        $BepInExCorePath = $gameCore
    } else {
        $BepInExCorePath = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\core"
    }
}
Write-Host "Game path:    $GamePath"
Write-Host "BepInEx core: $BepInExCorePath"

$managed = Join-Path $GamePath "valheim_Data\Managed"

# Required: the build fails without these.
$required = @(
    @{From=(Join-Path $BepInExCorePath "BepInEx.dll");  To="BepInEx.dll"},
    @{From=(Join-Path $BepInExCorePath "0Harmony.dll"); To="0Harmony.dll"},
    @{From=(Join-Path $managed "UnityEngine.dll");            To="UnityEngine.dll"},
    @{From=(Join-Path $managed "UnityEngine.CoreModule.dll"); To="UnityEngine.CoreModule.dll"},
    @{From=(Join-Path $managed "Assembly-CSharp.dll");        To="Assembly-CSharp.dll"},
    @{From=(Join-Path $managed "assembly_valheim.dll");       To="assembly_valheim.dll"}
)
# Optional: referenced by the csproj only when present (mirrors the GamePath branch).
$optional = @(
    @{From=(Join-Path $BepInExCorePath "BepInEx.Harmony.dll");            To="BepInEx.Harmony.dll"},
    @{From=(Join-Path $managed "UnityEngine.InputLegacyModule.dll");    To="UnityEngine.InputLegacyModule.dll"},
    @{From=(Join-Path $managed "UnityEngine.PhysicsModule.dll");        To="UnityEngine.PhysicsModule.dll"},
    @{From=(Join-Path $managed "UnityEngine.AnimationModule.dll");      To="UnityEngine.AnimationModule.dll"},
    @{From=(Join-Path $managed "UnityEngine.ParticleSystemModule.dll"); To="UnityEngine.ParticleSystemModule.dll"},
    @{From=(Join-Path $managed "UnityEngine.ScreenCaptureModule.dll");  To="UnityEngine.ScreenCaptureModule.dll"}
)

$libDir = Join-Path $PSScriptRoot "Lib"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$missing = 0
foreach ($item in $required) {
    $to = Join-Path $libDir $item.To
    if (Test-Path $item.From) {
        Copy-Item $item.From $to -Force
        Write-Host "Copied: $($item.To)"
    } else {
        Write-Warning "REQUIRED not found: $($item.From)"
        $missing++
    }
}
foreach ($item in $optional) {
    $to = Join-Path $libDir $item.To
    if (Test-Path $item.From) {
        Copy-Item $item.From $to -Force
        Write-Host "Copied: $($item.To)"
    } else {
        Write-Host "Skipped (optional, not found): $($item.To)"
    }
}

# Record which game build the refs came from.
$manifest = Join-Path $GamePath "..\..\appmanifest_892970.acf"
$buildId = "unknown"
if (Test-Path $manifest) {
    $m = Select-String -Path $manifest -Pattern '"buildid"\s+"(\d+)"'
    if ($m) { $buildId = $m.Matches[0].Groups[1].Value }
}
$stamp = "Frozen $(Get-Date -Format 'yyyy-MM-dd HH:mm') from Steam buildid $buildId`nGamePath=$GamePath`nBepInExCore=$BepInExCorePath"
Set-Content -Path (Join-Path $libDir "FROZEN_FROM.txt") -Value $stamp -Encoding utf8
Write-Host $stamp

if ($missing -gt 0) {
    Write-Error "$missing required reference(s) missing - the Lib fallback build will fail."
    exit 1
}
