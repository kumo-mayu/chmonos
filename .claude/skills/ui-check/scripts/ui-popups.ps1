# 台（ViewShot）で描けない物を、起動したアプリで撮る：吹き出し・右クリックのメニュー・プルダウンの開いた一覧・乗せた色・窓の枠（2026-11-14）。
# ui-kit.ps1 を読んだ後にドットで読み込む：
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-popups.ps1"
#
# なぜ別にしたか：吹き出しとメニューは主の窓とは別の窓に出るので、PrintWindow（Save-ChmonosShot）では写らない。
# 画面そのものから切り取る（CopyFromScreen）ので、アプリが前面にあり、上に何も重なっていないときだけ撮れる。
# **ここの関数は全部、画面を使う（並行で使えない）。**先に Lock-ChmonosScreen、終わったら Unlock-ChmonosScreen。
# マウスを動かす物（Show-ChmonosHover・右クリック）は実入力なので、使う前にユーザへ告げ、-UserWasTold を付ける。
#
# 例：
#   Use-ChmonosStore tour-dense-ui; Lock-ChmonosScreen
#   Set-ChmonosWindowSize -Width 1440 -Height 900
#   画面ごとに全部を通しで撮るのは popups-tour.ps1
#   Show-ChmonosHover -Element (Get-ChmonosById SearchClearFilters) -UserWasTold
#   Save-ChmonosScreenArea -Name 'popups\sort-tooltip'
#   Open-ChmonosDropDown -Element (Get-ChmonosById SearchSortField); Save-ChmonosScreenArea -Name 'popups\sort-open'
#   Unlock-ChmonosScreen

if (-not ('ChmonosWinPos' -as [type])) { Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ChmonosWinPos {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
  // 見えている枠の四角。GetWindowRect は Windows 10 以降、周りの見えない縁（大きさを変えるつかみ、約 7px）を含む
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
  public static Rect VisibleRect(IntPtr h) {
    Rect r;
    if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Rect))) != 0) { GetWindowRect(h, out r); }   // 9 = DWMWA_EXTENDED_FRAME_BOUNDS
    return r;
  }
}
"@ }

# 主の窓の外側の大きさを決める（最大化していたら戻してから）。左上は今の位置のまま。
# 台の --size は題の帯を除いた中身の大きさで、こちらは見えない縁・枠・題の帯を含む（100% で 900×600 を渡すと、見える窓は 886×593、中身は約 884×562。2026-11-14 に測った）。
# 窓の最小（MinWidth 900）も同じ数え方なので、実際の最小の中身は台の幅 900 より 16 ほど狭い
function Set-ChmonosWindowSize {
  param([Parameter(Mandatory)][int]$Width, [Parameter(Mandatory)][int]$Height, [string]$Store)
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  if ([ChmonosWinPos]::IsZoomed($h)) { [void][ChmonosWinPos]::ShowWindow($h, 9) }   # 9 = SW_RESTORE
  $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
  # 0x0004 = SWP_NOZORDER・0x0010 = SWP_NOACTIVATE（前面の取り合いを起こさない）
  [void][ChmonosWinPos]::SetWindowPos($h, [IntPtr]::Zero, $r.L, $r.T, $Width, $Height, 0x0014)
  Start-Sleep -Milliseconds 600   # 並べ直しと描き直しを待つ（WPF は大きさの知らせの後、1〜2コマで描き直す）
  [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
  "窓: $($r.R - $r.L)×$($r.B - $r.T)（$($r.L),$($r.T)）"
}

# 画面から、主の窓の四角（と周りの -Margin）を切り取る。窓の外へはみ出す吹き出し・メニューを入れたいときは -Margin を広げる。
# 前面に出してから撮る。出せない・上に別の窓があるときは撮らずに理由を返す（ほかのアプリを撮って渡さないように）
function Save-ChmonosScreenArea {
  param([Parameter(Mandatory)][string]$Name, [int]$Margin = 0, [string]$Store)
  $deny = Get-ChmonosScreenDenial $Store
  if ($deny) { return "撮らなかった：$deny" }
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  $fgRoot = [ChmonosWin]::GetAncestor([ChmonosWin]::GetForegroundWindow(), 3)
  if ($fgRoot -ne $h) {
    # メニュー・吹き出しは前面の窓を替えると閉じるので、ここでは前に出さない（開く前に Show-ChmonosFront を済ませておく）
    return '撮らなかった：アプリが前面に無い（開く前に Show-ChmonosFront）'
  }
  # 見えない縁を含めると、窓の後ろにある別のアプリが端に細く写る（2026-11-14 に出た）
  $r = [ChmonosWinPos]::VisibleRect($h)
  $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $x = [Math]::Max($screen.Left, $r.L - $Margin); $y = [Math]::Max($screen.Top, $r.T - $Margin)
  $w = [Math]::Min($screen.Right, $r.R + $Margin) - $x; $hh = [Math]::Min($screen.Bottom, $r.B + $Margin) - $y
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $hh)); $g.Dispose()
  $path = Join-Path $ChmonosShotDir "$Name.png"
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $path
}

# 要素の上にマウスを置いて待つ（吹き出しと乗せた色を出す）。実入力（マウスが動く）。
# WPF の吹き出しは乗せてから約 0.4 秒で出て、5 秒で消える（ToolTipService の既定）。-WaitMs はその間に収める
function Show-ChmonosHover {
  param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][switch]$UserWasTold, [int]$WaitMs = 1200, [string]$Store)
  if (-not $UserWasTold) { throw '実入力の前にユーザへ告げる（CLAUDE.md「確かめ方」）' }
  $deny = Get-ChmonosScreenDenial $Store
  if ($deny) { return "乗せなかった：$deny" }
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  if ([ChmonosWin]::GetAncestor([ChmonosWin]::GetForegroundWindow(), 3) -ne $h) { [void][ChmonosWin]::Bring($h); Start-Sleep -Milliseconds 400 }
  $c = Get-ChmonosCenter $Element
  $pt = New-Object ChmonosWin+POINT; $pt.X = $c.X; $pt.Y = $c.Y
  if ([ChmonosWin]::GetAncestor([ChmonosWin]::WindowFromPoint($pt), 3) -ne $h) { return "乗せなかった：($($c.X),$($c.Y)) の上に別の窓がある" }
  # 前に乗せていた要素の吹き出しを閉じるため、一度窓の外の端へ逃がしてから乗せる（同じ所のままだと新しい吹き出しが出ない）
  $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
  [void][ChmonosWin]::SetCursorPos($r.L + 4, $r.B - 4); Start-Sleep -Milliseconds 300
  [void][ChmonosWin]::SetCursorPos($c.X, $c.Y)
  Start-Sleep -Milliseconds $WaitMs
  "乗せた: 「$($Element.Current.Name)」($($c.X),$($c.Y))"
}

# マウスを窓の外の端へ逃がす（乗せた色・吹き出しを消してから次を撮る）
function Clear-ChmonosHover {
  param([string]$Store)
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
  [void][ChmonosWin]::SetCursorPos($r.R + 40, $r.T + 40); Start-Sleep -Milliseconds 300
}

# プルダウン（ComboBox・開閉のあるボタン）を UI Automation で開く／閉じる。実入力は使わないが、開いた一覧は前面に無いと閉じるので、先に前に出す
function Open-ChmonosDropDown {
  param([Parameter(Mandatory)]$Element, [switch]$Close, [string]$Store)
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  if ([ChmonosWin]::GetAncestor([ChmonosWin]::GetForegroundWindow(), 3) -ne $h) { [void][ChmonosWin]::Bring($h); Start-Sleep -Milliseconds 400 }
  $p = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  if ($Close) { $p.Collapse() } else { $p.Expand() }
  Start-Sleep -Milliseconds 500   # 開く動き（WPF の Popup の既定の動き）を待つ
  "$(if ($Close) { '閉じた' } else { '開いた' }): 「$($Element.Current.Name)」"
}
