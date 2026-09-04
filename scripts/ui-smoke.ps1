<#
.SYNOPSIS
  End-to-end UI smoke test: real OS mouse + keyboard input against a running
  diaphane, asserting the OSR input pipeline (WinUI events -> CEF) actually works.

.DESCRIPTION
  Launches the debug build with DIAPHANE_SMOKE pointed at a temp dir. The app
  navigates to a fixed 3-control form page, writes a "ready" marker, then keeps
  a state.json of the live DOM. This script then:
    1. clicks the text box, types "hello world", and checks the value + that the
       field kept focus (the "flash then lose focus" regression),
    2. clicks the checkbox and checks it toggled,
    3. clicks the textarea, types two lines, checks the value.
  Exits non-zero on any failure. Needs an interactive desktop session.
#>
param(
    [string]$Exe = "$PSScriptRoot\..\src\Diaphane.App\bin\x64\Debug\net8.0-windows10.0.19041.0\Diaphane.App.exe"
)
$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Native {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int sz);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code,uint type);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MKI u; }
  [StructLayout(LayoutKind.Explicit)] public struct MKI {
    [FieldOffset(0)] public MI mi;
    [FieldOffset(0)] public KI ki;
  }
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int dx; public int dy; public uint data; public uint flags; public uint time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }

  public static void Foreground(IntPtr h) {
    uint me = GetCurrentThreadId(), other = GetWindowThreadProcessId(h, IntPtr.Zero);
    AttachThreadInput(me, other, true); ShowWindow(h, 9); BringWindowToTop(h); SetForegroundWindow(h);
    AttachThreadInput(me, other, false);
  }
  static void Send(INPUT i) { SendInput(1, new[]{i}, Marshal.SizeOf(typeof(INPUT))); }
  public static void ClickClient(IntPtr h, int cx, int cy) {
    var o = new POINT(); ClientToScreen(h, ref o);
    SetCursorPos(o.X + cx, o.Y + cy);
    System.Threading.Thread.Sleep(250);
    Send(new INPUT { type=0, u=new MKI{ mi=new MI{ flags=0x0002 } } });   // LEFTDOWN
    System.Threading.Thread.Sleep(90);
    Send(new INPUT { type=0, u=new MKI{ mi=new MI{ flags=0x0004 } } });   // LEFTUP
    System.Threading.Thread.Sleep(450);
  }
  public static void Key(ushort vk) {
    ushort sc = (ushort)MapVirtualKey(vk, 0);
    Send(new INPUT { type=1, u=new MKI{ ki=new KI{ vk=vk, scan=sc, flags=0x0008 } } });
    System.Threading.Thread.Sleep(25);
    Send(new INPUT { type=1, u=new MKI{ ki=new KI{ vk=vk, scan=sc, flags=0x000A } } });
    System.Threading.Thread.Sleep(45);
  }
  public static void Type(string s) {
    foreach (char c in s) {
      if (c == ' ') { Key(0x20); continue; }
      if (c == '\n') { Key(0x0D); continue; }
      Key((ushort)char.ToUpperInvariant(c));
    }
  }
}
"@

$dir = Join-Path $env:TEMP ("diaphane-smoke-" + [guid]::NewGuid().ToString("N"))
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
$env:DIAPHANE_SMOKE = $dir

$p = Start-Process $Exe -PassThru
try {
    $ready = Join-Path $dir "ready"
    $t = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $ready) -and $t.Elapsed.TotalSeconds -lt 40) { Start-Sleep -Milliseconds 300 }
    if (-not (Test-Path $ready)) { throw "app never reported ready" }

    $h = 0
    Get-Process Diaphane.App | ForEach-Object { if ($_.MainWindowHandle -ne 0) { $h = $_.MainWindowHandle } }
    [Native]::Foreground([IntPtr]$h)
    Start-Sleep 1

    $cr = New-Object Native+RECT
    [Native]::GetClientRect([IntPtr]$h, [ref]$cr) | Out-Null
    # page content starts below tab strip (40) + toolbar (44) + bookmarks bar (32)
    $pageTop = 116

    function State { Get-Content (Join-Path $dir "state.json") -Raw | ConvertFrom-Json }

    $fail = @()

    # 1. text box: click, type, check value + still focused
    [Native]::ClickClient([IntPtr]$h, 60, $pageTop + 45)
    Start-Sleep -Milliseconds 600
    [Native]::Type("hello world")
    Start-Sleep 1
    $s = State
    if ($s.txt -ne "hello world") { $fail += "text value: '$($s.txt)' (expected 'hello world')" }
    if (-not $s.txtFocused)        { $fail += "text box lost focus after click (regression)" }

    # 2. checkbox: click toggles
    [Native]::ClickClient([IntPtr]$h, 30, $pageTop + 150)
    Start-Sleep -Milliseconds 1200
    $s2 = State
    if (-not $s2.chk) { $fail += "checkbox did not toggle on click (state: $($s2 | ConvertTo-Json -Compress))" }

    # 3. textarea: click, type two lines
    [Native]::ClickClient([IntPtr]$h, 60, $pageTop + 240)
    Start-Sleep -Milliseconds 600
    [Native]::Type("line one`nline two")
    Start-Sleep 1
    if ((State).ta -notlike "line one*line two") { $fail += "textarea value: '$((State).ta)'" }

    if ($fail.Count) {
        Write-Host "UI SMOKE FAILED:" -ForegroundColor Red
        $fail | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "UI smoke passed: click focuses form fields, focus is retained, typing lands, checkbox toggles." -ForegroundColor Green
}
finally {
    if ($p -and -not $p.HasExited) { $p.Kill() }
    $env:DIAPHANE_SMOKE = $null
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
}
