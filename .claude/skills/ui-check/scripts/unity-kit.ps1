# Unity を相手にする確かめの道具（取り込み画面・プロジェクトの窓）。ui-kit と同じく、呼び出しごとにドットで読み込む：
#   . "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"
#   . "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\unity-kit.ps1"
#
# **Unity を実際に動かす確かめは、ユーザが「試して」と言ったときだけ**（CLAUDE.md）。使ってよいプロジェクトは
# `D:\work\vrchat\VRChatProjects\cleanTest - コピー`（ユーザ 2026-09-19）。
#
# Unity の窓の中は IMGUI で、UI Automation には何も出ない。だから：
# - 在るかは窓の題（Import Unity Package）で見る
# - 中身を見たいときは絵で撮る（PrintWindow。後ろに隠れていても撮れる）
# - ボタンは実入力で押す（位置は窓の右下から測る。下の Close-UnityImport）
Add-Type -AssemblyName System.Drawing
if (-not ('ChmonosUnity' -as [type])) { Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class ChmonosUnity {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static List<IntPtr> Windows(int processId) {
    var found = new List<IntPtr>();
    EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pid == processId && IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
    return found;
  }
  public static string Title(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
}
"@ }

$UnityImportTitle = 'Import Unity Package'

# 開いているエディタ（AssetImportWorker は窓が無いので出てこない）
function Get-UnityEditors { Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } }

# エディタの見えている窓（題と四角）。-Title で絞る
function Get-UnityWindows {
  param([string]$Title = '*', $Editor)
  $procs = if ($Editor) { @($Editor) } else { @(Get-UnityEditors) }
  foreach ($p in $procs) {
    foreach ($h in [ChmonosUnity]::Windows($p.Id)) {
      $t = [ChmonosUnity]::Title($h)
      if ($t -notlike $Title) { continue }
      $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
      [pscustomobject]@{ Handle = $h; Title = $t; X = $r.L; Y = $r.T; Width = $r.R - $r.L; Height = $r.B - $r.T }
    }
  }
}

# 取り込み画面が出るまで待つ（大きい zip は中を読むのに十数秒かかる）
function Wait-UnityImportWindow {
  param([double]$TimeoutSeconds = 60)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-UnityWindows -Title $UnityImportTitle | Select-Object -First 1 }
}

# 取り込み画面の中を見る（絵で撮って Read で開く）。「Nothing to import!」か中身の一覧かは、これで分かる
function Save-UnityShot {
  param([Parameter(Mandatory)]$Window, [Parameter(Mandatory)][string]$Name)
  $bmp = New-Object System.Drawing.Bitmap $Window.Width, $Window.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][ChmonosWin]::PrintWindow($Window.Handle, $dc, 2)   # 2 = PW_RENDERFULLCONTENT
  $g.ReleaseHdc($dc); $g.Dispose()
  New-Item -ItemType Directory -Force (Join-Path $env:TEMP 'chmonos-shots') | Out-Null
  $path = Join-Path $env:TEMP "chmonos-shots\$Name.png"
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $path
}

# 取り込み画面のボタンの位置は、窓の右下から測る（実機の 366×589 で採寸：
# Import は右から45・下から25、OK は右から37、Cancel は右から98）。Unity の窓は IMGUI なので座標でしか押せない
$UnityImportButtons = @{ Import = 45; OK = 37; Cancel = 98 }

# 取り込み画面を閉じる。**閉じたことを確かめ**、閉じなければもう一度押す
# （1回目のクリックが窓を前に出すだけに使われる。実機で何度も起きた）
function Close-UnityImport {
  param(
    [ValidateSet('Import', 'OK', 'Cancel')][string]$Button = 'OK',
    [Parameter(Mandatory)][switch]$UserWasTold, [double]$TimeoutSeconds = 60)
  if (-not $UserWasTold) { throw '実入力の前にユーザへ告げる（CLAUDE.md「確かめ方」）' }
  $w = Wait-UnityImportWindow -TimeoutSeconds $TimeoutSeconds
  if (-not $w) { return '取り込み画面が出ていない' }

  $x = $w.X + $w.Width - $UnityImportButtons[$Button]
  $y = $w.Y + $w.Height - 25
  for ($try = 1; $try -le 3; $try++) {
    [void][ChmonosWin]::Bring($w.Handle); Start-Sleep -Milliseconds 400
    [void][ChmonosWin]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 150
    [ChmonosWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); [ChmonosWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    if (Wait-ChmonosCondition -TimeoutSeconds 3 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($w.Handle) }) {
      return "押した: 取り込み画面の「$Button」（$try 回目・($x,$y)）"
    }
  }
  "閉じられない: 取り込み画面の「$Button」（($x,$y) を3回押した）"
}

# プロジェクトの窓を最小化する／戻す（最小化したまま送る確かめに使う）
function Set-UnityMinimized {
  param([switch]$Off, $Editor)
  $p = if ($Editor) { $Editor } else { Get-UnityEditors | Select-Object -First 1 }
  if (-not $p) { return 'Unity が開いていない' }
  [void][ChmonosWin]::Bring($p.MainWindowHandle)   # 戻すのは Bring（最小化なら開く）
  if (-not $Off) { [void](& { param($h) [void][ChmonosWin]::SetCursorPos(0, 0) } $p.MainWindowHandle) }
  $p.MainWindowHandle
}
