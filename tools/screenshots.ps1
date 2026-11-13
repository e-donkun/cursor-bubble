# Takes real screenshots of CursorBubble in action (used by the "screenshots" workflow).
# Launches the app, replays mouse / keyboard input, and saves PNG files into docs\.
# Keep this file ASCII-only (Windows PowerShell 5.1 reads scripts without BOM as ANSI).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class In {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32.dll")] public static extern bool DrawIcon(IntPtr hdc, int x, int y, IntPtr icon);
    public static void Mouse(uint f, int data) { mouse_event(f, 0, 0, data, UIntPtr.Zero); }
    public static void Key(byte vk, bool down) { keybd_event(vk, 0, down ? 0u : 2u, UIntPtr.Zero); }
    public static void DrawArrow(Graphics g, int x, int y) {
        IntPtr hdc = g.GetHdc();
        DrawIcon(hdc, x, y, LoadCursor(IntPtr.Zero, 32512));
        g.ReleaseHdc(hdc);
    }
}
'@

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'docs'
New-Item -ItemType Directory -Force $out | Out-Null
$script:cx = 0; $script:cy = 0

# Move with real (injected) mouse input so that low-level hooks see the movement
function MoveTo($x, $y) {
    $script:cx = $x; $script:cy = $y
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $ax = [int][Math]::Round($x * 65535 / ($b.Width - 1)); $ay = [int][Math]::Round($y * 65535 / ($b.Height - 1))
    [In]::mouse_event(0x8001, $ax, $ay, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
}
function Shot($name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $full = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($full)
    $g.CopyFromScreen(0, 0, 0, 0, $full.Size)
    [In]::DrawArrow($g, $script:cx, $script:cy)   # the mouse pointer is not part of a screen copy
    $g.Dispose()
    $w = 620; $h = 300
    $x = [Math]::Max(0, [Math]::Min($b.Width - $w, $script:cx - 190))
    $y = [Math]::Max(0, [Math]::Min($b.Height - $h, $script:cy - 110))
    $crop = $full.Clone((New-Object System.Drawing.Rectangle $x, $y, $w, $h), $full.PixelFormat)
    $crop.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose(); $full.Dispose()
}
function Click { [In]::Mouse(2, 0); Start-Sleep -Milliseconds 40; [In]::Mouse(4, 0) }
function Settle { Start-Sleep -Milliseconds 2000 }

# Larger bubbles for the screenshots
$cfg = Join-Path $env:APPDATA 'CursorBubble'
New-Item -ItemType Directory -Force $cfg | Out-Null
Set-Content (Join-Path $cfg 'settings.txt') "size=2`nallkeys=0`nhints=1`nripple=1"

# A text file to operate on
$sample = Join-Path $env:TEMP 'sample.txt'
$lines = 1..60 | ForEach-Object { "Line $_ : The quick brown fox jumps over the lazy dog." }
Set-Content $sample $lines
Start-Process notepad.exe $sample
Start-Sleep -Seconds 5
[In]::MoveWindow([In]::GetForegroundWindow(), 30, 30, 900, 620, $true) | Out-Null
Start-Sleep -Seconds 1

$app = Start-Process (Join-Path $root 'CursorBubble.exe') -PassThru
Start-Sleep -Seconds 4

# 1. left click
MoveTo 260 220; Click; Start-Sleep -Milliseconds 130; Shot 'click'; Settle
# 2. double click
MoveTo 330 262; Click; Start-Sleep -Milliseconds 90; Click; Start-Sleep -Milliseconds 130; Shot 'double-click'; Settle
# 3. drag (select text)
MoveTo 150 300; [In]::Mouse(2, 0)
foreach ($i in 1..12) { MoveTo (150 + $i * 25) (300 + $i * 4) }
Start-Sleep -Milliseconds 250; Shot 'drag'; [In]::Mouse(4, 0); Settle
# 4. shortcut: Ctrl + C
[In]::Key(0x11, $true); Start-Sleep -Milliseconds 150; [In]::Key(0x43, $true); [In]::Key(0x43, $false)
Start-Sleep -Milliseconds 250; Shot 'shortcut'; [In]::Key(0x11, $false); Settle
# 5. scroll
MoveTo 420 360; [In]::Mouse(0x0800, -120); Start-Sleep -Milliseconds 80; [In]::Mouse(0x0800, -120)
Start-Sleep -Milliseconds 250; Shot 'scroll'; Settle
# 6. special key pressed repeatedly
foreach ($i in 1..3) { [In]::Key(0x28, $true); [In]::Key(0x28, $false); Start-Sleep -Milliseconds 120 }
Start-Sleep -Milliseconds 150; Shot 'key'; Settle
# 7. right click
MoveTo 300 240; [In]::Mouse(8, 0); Start-Sleep -Milliseconds 40; [In]::Mouse(0x10, 0)
Start-Sleep -Milliseconds 350; Shot 'right-click'
[In]::Key(0x1B, $true); [In]::Key(0x1B, $false)

Start-Sleep -Seconds 1
Stop-Process -Id $app.Id -Force
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem $out
