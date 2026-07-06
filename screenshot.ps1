Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@
$proc = Get-Process valheim -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "Valheim not running!"; exit 1 }
Write-Host "Found valheim PID $($proc.Id), handle $($proc.MainWindowHandle)"
[Win32]::ShowWindow($proc.MainWindowHandle, 9)
Start-Sleep -Milliseconds 500
$result = [Win32]::SetForegroundWindow($proc.MainWindowHandle)
Write-Host "SetForegroundWindow returned: $result"
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('%{PRTSC}')
Start-Sleep -Seconds 1
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($img -eq $null) { Write-Host "Clipboard has no image!"; exit 1 }
$path = 'C:\Users\Dev\AppData\LocalLow\IronGate\Valheim\screenshots\claude_shot.png'
$img.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Screenshot saved to $path"
