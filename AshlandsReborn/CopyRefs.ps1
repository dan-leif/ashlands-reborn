param(
    [Parameter(Mandatory=$false)]
    [string]$GamePath
)

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

$src = @(
    @{From="BepInEx\core\BepInEx.dll"; To="BepInEx.dll"},
    @{From="valheim_Data\Managed\UnityEngine.dll"; To="UnityEngine.dll"},
    @{From="valheim_Data\Managed\UnityEngine.CoreModule.dll"; To="UnityEngine.CoreModule.dll"},
    @{From="valheim_Data\Managed\Assembly-CSharp.dll"; To="Assembly-CSharp.dll"},
    @{From="valheim_Data\Managed\assembly_valheim.dll"; To="assembly_valheim.dll"}
)

$libDir = Join-Path $PSScriptRoot "Lib"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null

foreach ($item in $src) {
    $from = Join-Path $GamePath $item.From
    $to = Join-Path $libDir $item.To
    if (Test-Path $from) {
        Copy-Item $from $to -Force
        Write-Host "Copied: $($item.To)"
    } else {
        Write-Warning "Not found: $from"
    }
}
