# Chmonos の画面を確かめる道具。ツールの呼び出しごとにドットで読み込む（シェルの状態は呼び出しをまたいで残らない）：
#   . "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"
#
# CLAUDE.md の決め事 4・5 をここで守らせる：
# - 本番（%LOCALAPPDATA%\Chmonos）と friendtest（ユーザの作業用の写し）では起動しない
# - 閉じるのは、この道具で起動したアプリだけ（ユーザが開いているアプリを巻き込まない。前の道具は名前で全部落としていた）
# - 実入力は、窓が前面にあり、座標が窓の中にあるときだけ送る（要素が見つからず (0,0)＝デスクトップを押した事故がある）
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
if (-not ('ChmonosWin' -as [type])) { Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ChmonosWin {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  // 別のプロセスの窓は SetForegroundWindow だけでは前に出ない。前面の窓の入力に一時的につなぐ
  // 主の窓の上に出ている小窓（MessageBox・選ぶ窓）。主の窓を前に出すとその下に隠れるので、前に出すのはこちら
  public static IntPtr ActivePopup(IntPtr owner) {
    var popup = GetLastActivePopup(owner);
    return popup != IntPtr.Zero && IsWindow(popup) ? popup : owner;
  }
  public static bool Bring(IntPtr target) {
    var fg = GetForegroundWindow(); uint t = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
    AttachThreadInput(me, t, true); if (IsIconic(target)) ShowWindow(target, 9); BringWindowToTop(target);
    var ok = SetForegroundWindow(target); AttachThreadInput(me, t, false); return ok;
  }
}
"@ }

$ChmonosRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
# 実行ファイル名は改名の③（公開の直前）で変わる。そのときはここだけ直す
$ChmonosExe = Join-Path $ChmonosRepo 'BoothAssetManager.App\bin\Debug\net9.0-windows\BoothAssetManager.App.exe'
$ChmonosProcessName = [IO.Path]::GetFileNameWithoutExtension($ChmonosExe)
$ChmonosProduction = Join-Path $env:LOCALAPPDATA 'Chmonos'
$ChmonosForbidden = @(
  $ChmonosProduction,
  (Join-Path $env:LOCALAPPDATA 'BoothAssetManager'),            # 改名前の本番の名前。残っていたら本番の中身かもしれない
  (Join-Path $env:LOCALAPPDATA 'BoothAssetManager-friendtest')  # ユーザが普段開く写し。確かめは -friendcheck で
)
$ChmonosShotDir = Join-Path $env:TEMP 'chmonos-shots'
$ChmonosPidFile = Join-Path $env:TEMP 'chmonos-ui-check.json'
$ChmonosBaselineFile = Join-Path $env:TEMP 'chmonos-prod-baseline.json'
$ChmonosTraceFile = Join-Path $env:TEMP 'chmonos-uitrace.log'
$A_ = [System.Windows.Automation.AutomationElement]
$TS_ = [System.Windows.Automation.TreeScope]

# ---- 保存先 ----

# 'ui' のような短い名前は %LOCALAPPDATA%\BoothAssetManager-ui と読む（サンドボックスの名前は改名前のまま）
function Resolve-ChmonosStore([string]$Store) {
  $p = if ([IO.Path]::IsPathRooted($Store)) { $Store } else { Join-Path $env:LOCALAPPDATA "BoothAssetManager-$Store" }
  [IO.Path]::GetFullPath($p).TrimEnd('\')
}

function Assert-ChmonosSandbox([string]$Root) {
  if ($ChmonosForbidden -contains $Root) { throw "ここは確かめに使わない保存先: $Root（本番・friendtest。書いてよい写しは -friendcheck や -ui）" }
}

# 写しを作る。-From は短い名前・フルパス・'prod'（本番を読むだけ）
function New-ChmonosSandbox {
  param([Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$Name)
  $src = if ($From -eq 'prod') { $ChmonosProduction } else { Resolve-ChmonosStore $From }
  $dst = Resolve-ChmonosStore $Name
  Assert-ChmonosSandbox $dst
  if (-not (Test-Path $src)) { throw "写す元が無い: $src" }
  if (Test-Path $dst) { throw "もうある: $dst（上書きしない。消すならユーザに聞く）" }
  Copy-Item $src $dst -Recurse
  # 本番の location.json は friendtest を指している。写しに残すと、写しを開いたつもりで friendtest が開く
  $loc = Join-Path $dst 'location.json'
  if (Test-Path $loc) { Remove-Item $loc; "写しの location.json を外した（元の保存先へ飛ばないように）" }
  "作った: $dst（元: $src）"
}

# ---- 起動と終了 ----

function Start-ChmonosApp {
  param([Parameter(Mandatory)][string]$Store, [switch]$AllowNew, [int]$SettleSeconds = 6, [switch]$NoTrace)
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandbox $root
  if (-not $AllowNew -and -not (Test-Path $root)) { throw "保存先が無い: $root（初回の窓を見るなら -AllowNew）" }
  if (-not (Test-Path $ChmonosExe)) { throw "ビルドが無い: $ChmonosExe（dotnet build）" }
  if (Test-Path $ChmonosPidFile) { try { $old = Get-ChmonosApp; throw "前に起動したアプリがまだ開いている: pid=$($old.Id)（Stop-ChmonosApp）" } catch { if ($_.Exception.Message -like '前に起動した*') { throw } } }
  $psi = [Diagnostics.ProcessStartInfo]::new($ChmonosExe)
  $psi.UseShellExecute = $false; $psi.WorkingDirectory = Split-Path $ChmonosExe
  # 空文字を入れても子に渡ることがあるので、起動する側の環境から取り除く。
  # BAM_* と DOTNET_GC* は速さ・メモリの計測で使った変数で、残ると別の条件で動く
  foreach ($n in @($psi.Environment.Keys | Where-Object { $_ -like 'BAM_*' -or $_ -like 'DOTNET_GC*' -or $_ -eq 'BOOTH_ASSET_MANAGER_HOME' })) { [void]$psi.Environment.Remove($n) }
  $psi.Environment['CHMONOS_HOME'] = $root
  # 確かめ用の足跡（出した窓の文言・押されたボタン・実行した命令・Unity の取り込み）。
  # 文言の確かめを撮らずに済む。前の分は消しておく（今回の起動の分だけを読む）
  if (-not $NoTrace) {
    if (Test-Path $ChmonosTraceFile) { Remove-Item $ChmonosTraceFile -Force }
    $psi.Environment['CHMONOS_UITRACE'] = $ChmonosTraceFile
  }
  $p = [Diagnostics.Process]::Start($psi)
  for ($i = 0; $i -lt 120 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw "窓が出なかった（pid=$($p.Id)）" }
  @{ pid = $p.Id; startTicks = $p.StartTime.Ticks; store = $root } | ConvertTo-Json | Set-Content $ChmonosPidFile
  Start-Sleep -Seconds $SettleSeconds
  "起動した: pid=$($p.Id) 保存先=$root 窓の題=「$((Get-ChmonosRoot).Current.Name)」"
}

# この道具で起動したアプリ。pid の使い回しに備えて起動時刻も照らす
function Get-ChmonosApp {
  if (-not (Test-Path $ChmonosPidFile)) { throw 'この道具で起動したアプリが無い（Start-ChmonosApp）' }
  $info = Get-Content $ChmonosPidFile -Raw | ConvertFrom-Json
  $p = Get-Process -Id $info.pid -ErrorAction SilentlyContinue
  if (-not $p -or $p.ProcessName -ne $ChmonosProcessName -or $p.StartTime.Ticks -ne [long]$info.startTicks) {
    Remove-Item $ChmonosPidFile -ErrorAction SilentlyContinue
    throw 'この道具で起動したアプリはもう閉じている'
  }
  $p
}

function Stop-ChmonosApp {
  try { $p = Get-ChmonosApp } catch { return $_.Exception.Message }
  [void]$p.CloseMainWindow()
  if (-not $p.WaitForExit(8000)) { $p.Kill(); [void]$p.WaitForExit(3000) }
  Remove-Item $ChmonosPidFile -ErrorAction SilentlyContinue
  "閉じた: pid=$($p.Id)"
}

# ---- UI Automation ----

function Get-ChmonosRoot { $A_::FromHandle((Get-ChmonosApp).MainWindowHandle) }

# -Type は ControlType の名前（Button・Text・Edit・RadioButton・CheckBox・ListItem・DataItem・Window…）。
# GridView の一覧の行は ListItem ではなく DataItem。持ち主付きの窓（ダイアログ）は主の窓の子として出る
function Get-ChmonosElements {
  param([Parameter(Mandatory)][string]$Type, [string]$Name, [string]$Like, $Scope)
  $root = if ($Scope) { $Scope } else { Get-ChmonosRoot }
  $ct = [System.Windows.Automation.ControlType]::$Type
  if (-not $ct) { throw "ControlType に無い: $Type" }
  $cond = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, $ct)
  foreach ($e in $root.FindAll($TS_::Descendants, $cond)) {
    $n = $e.Current.Name
    if ($Name -and $n -ne $Name) { continue }
    if ($Like -and $n -notlike $Like) { continue }
    $e
  }
}

# 見えている文字（Text の名前）。「この文言が出たか」を数えるのに使う
function Get-ChmonosTexts([string]$Like = '*') { Get-ChmonosElements -Type Text -Like $Like | ForEach-Object { $_.Current.Name } }

# 要素が持つ操作で押す（ボタン＝Invoke、ラジオ・一覧の行＝選ぶ、チェック＝切り替え、畳み＝開く）
function Invoke-ChmonosElement($El) {
  $pats = @($El.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
  if ($pats -contains 'InvokePatternIdentifiers.Pattern') { $El.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return 'Invoke' }
  if ($pats -contains 'SelectionItemPatternIdentifiers.Pattern') { $El.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); return 'Select' }
  if ($pats -contains 'TogglePatternIdentifiers.Pattern') { $El.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return 'Toggle' }
  if ($pats -contains 'ExpandCollapsePatternIdentifiers.Pattern') { $El.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); return 'Expand' }
  throw "押す操作を持たない: $($El.Current.ControlType.ProgrammaticName)「$($El.Current.Name)」"
}

# 名前で探して押す。同じ名前が複数あるときは -Index（上から数えるとは限らない。木の順）
function Invoke-ChmonosByName {
  param([Parameter(Mandatory)][string]$Name, [string]$Type = 'Button', [int]$Index = 0, [double]$WaitSeconds = 2)
  $els = @(Get-ChmonosElements -Type $Type -Name $Name)
  if ($els.Count -le $Index) { return "無い: $Type「$Name」（$($els.Count) 件）" }
  $how = Invoke-ChmonosElement $els[$Index]
  Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000))
  "押した: $Type「$Name」($how・同名 $($els.Count) 件)"
}

# 文字を押す。WPF の見出しやカードは、文字の親をたどった先に押せる部品がある
function Invoke-ChmonosByText {
  param([Parameter(Mandatory)][string]$Text, [int]$Index = 0, [double]$WaitSeconds = 2)
  $t = @(Get-ChmonosElements -Type Text -Name $Text)
  if ($t.Count -le $Index) { return "無い: 文字「$Text」" }
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  $el = $t[$Index]
  for ($k = 0; $k -lt 8 -and $el; $k++) {
    try { $how = Invoke-ChmonosElement $el; Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000)); return "押した: 文字「$Text」の $k 段上（$how）" } catch { }
    $el = $walker.GetParent($el)
  }
  "押せる部品が見つからない: 文字「$Text」"
}

# 入力欄に値を入れる。-Like は欄の名前（見出しや透かしの文字）
function Set-ChmonosText {
  param([Parameter(Mandatory)][string]$Like, [Parameter(Mandatory)][AllowEmptyString()][string]$Value, [double]$WaitSeconds = 1)
  $box = Get-ChmonosElements -Type Edit -Like $Like | Select-Object -First 1
  if (-not $box) { return "無い: 入力欄「$Like」" }
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
  Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000))
  "入れた: 「$($box.Current.Name)」← $Value"
}

# 一覧を送る。仮想化した一覧は画面に見えている行しか UI Automation に出ないので、送ってから数え直す
function Step-ChmonosScroll {
  param([int]$Times = 1, [int]$Index = -1, [switch]$Up)
  $scrollers = @($(Get-ChmonosRoot).FindAll($TS_::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A_::IsScrollPatternAvailableProperty, $true))))
  if ($scrollers.Count -eq 0) { return '送れる部品が無い' }
  $s = $scrollers[$Index]
  $amount = if ($Up) { [System.Windows.Automation.ScrollAmount]::LargeDecrement } else { [System.Windows.Automation.ScrollAmount]::LargeIncrement }
  for ($i = 0; $i -lt $Times; $i++) { $s.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).ScrollVertical($amount); Start-Sleep -Milliseconds 300 }
  "送った: $Times 回（送れる部品 $($scrollers.Count) 個のうち $Index 番）"
}

# 要素の中心（画面の座標）。画面の外・空の四角は投げる。実入力の座標は必ずここを通す
function Get-ChmonosCenter($El) {
  $r = $El.Current.BoundingRectangle
  if ($r.IsEmpty -or [double]::IsInfinity($r.X) -or [double]::IsInfinity($r.Y) -or $r.Width -le 0 -or $r.Height -le 0 -or $El.Current.IsOffscreen) {
    throw "位置が取れない（画面の外か空）: 「$($El.Current.Name)」"
  }
  [pscustomobject]@{ X = [int]($r.X + $r.Width / 2); Y = [int]($r.Y + $r.Height / 2) }
}

# ---- 描画を見る ----

# 窓を撮る（前面でなくても撮れる）。-Element でダイアログの窓、-Region で窓の中の一部（x,y,幅,高さ。窓の左上から）。
# 小さく切ると、読むときの文脈も節約できる。戻りは保存したパス（Read で開いて見る）
function Save-ChmonosShot {
  param([Parameter(Mandatory)][string]$Name, $Element, [int[]]$Region)
  $h = if ($Element) { [IntPtr]$Element.Current.NativeWindowHandle } else { (Get-ChmonosApp).MainWindowHandle }
  if ($h -eq [IntPtr]::Zero) { throw '窓の取っ手が無い（-Element には Window の要素を渡す）' }
  $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$r)
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  if ($w -le 0 -or $hh -le 0) { throw '窓の大きさが 0（最小化している？）' }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][ChmonosWin]::PrintWindow($h, $dc, 2)   # 2 = PW_RENDERFULLCONTENT（WPF の描画を撮るのに要る）
  $g.ReleaseHdc($dc); $g.Dispose()
  if ($Region) {
    # 要素の位置から枠を決めると窓の外へはみ出すことがある。はみ出すと Clone が落ちて何も撮れないので、窓の中に収める
    $x = [Math]::Max(0, [Math]::Min($Region[0], $w - 1)); $y = [Math]::Max(0, [Math]::Min($Region[1], $hh - 1))
    $cw = [Math]::Max(1, [Math]::Min($Region[2], $w - $x)); $ch = [Math]::Max(1, [Math]::Min($Region[3], $hh - $y))
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $x, $y, $cw, $ch), $bmp.PixelFormat); $bmp.Dispose(); $bmp = $crop
  }
  New-Item -ItemType Directory -Force $ChmonosShotDir | Out-Null
  $path = Join-Path $ChmonosShotDir "$Name.png"
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $path
}

# ---- 実入力（UI Automation で届かない所だけ。使う前にユーザへ告げる） ----

function Show-ChmonosFront { $ok = [ChmonosWin]::Bring((Get-ChmonosApp).MainWindowHandle); Start-Sleep -Milliseconds 600; "前面に出した: $ok" }

# X・Y は画面の座標（Get-ChmonosCenter の戻り）。-UserWasTold はユーザへ告げたことの確認で、付けないと動かない
function Invoke-ChmonosRealClick {
  param([Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y, [Parameter(Mandatory)][switch]$UserWasTold, [switch]$Right)
  if (-not $UserWasTold) { throw '実入力の前にユーザへ告げる（CLAUDE.md「確かめ方」）' }
  $h = (Get-ChmonosApp).MainWindowHandle
  # 前に出すのは、開いていれば小窓の方（主の窓を前に出すと MessageBox がその下に隠れ、押しても届かなかった。2026-09-19）
  [void][ChmonosWin]::Bring([ChmonosWin]::ActivePopup($h)); Start-Sleep -Milliseconds 500
  $fgRoot = [ChmonosWin]::GetAncestor([ChmonosWin]::GetForegroundWindow(), 3)   # 3 = GA_ROOTOWNER（ダイアログなら主の窓）
  if ($fgRoot -ne $h) { return '実入力をやめた：アプリが前面に無い' }
  # 窓の中かを見るのは、いま前に出ている窓（小窓なら小窓の四角）で見る
  $r = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect([ChmonosWin]::ActivePopup($h), [ref]$r)
  if ($X -le $r.L -or $X -ge $r.R -or $Y -le $r.T -or $Y -ge $r.B) { return "実入力をやめた：($X,$Y) は窓（$($r.L),$($r.T)〜$($r.R),$($r.B)）の外" }
  $pt = New-Object ChmonosWin+POINT; $pt.X = $X; $pt.Y = $Y
  if ([ChmonosWin]::GetAncestor([ChmonosWin]::WindowFromPoint($pt), 3) -ne $h) { return "実入力をやめた：($X,$Y) の上に別の窓がある" }
  [void][ChmonosWin]::SetCursorPos($X, $Y); Start-Sleep -Milliseconds 100
  if ($Right) { [ChmonosWin]::mouse_event(0x0008, 0, 0, 0, [IntPtr]::Zero); [ChmonosWin]::mouse_event(0x0010, 0, 0, 0, [IntPtr]::Zero) }
  else { [ChmonosWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); [ChmonosWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 600
  "実クリック: ($X,$Y)"
}

# ---- 待つ（固定の Start-Sleep をやめる） ----
#
# 秒数を決め打ちすると、早すぎて取りこぼす（まだ出ていない文言を「無い」と読む）か、遅すぎて待ち損になる。
# ここは 300ms ごとに見に行き、**出た瞬間に返す**。見つからなければ $null を返す（投げない。呼ぶ側で言い分けたいので）

function Wait-ChmonosCondition {
  param([Parameter(Mandatory)][scriptblock]$Until, [double]$TimeoutSeconds = 15, [int]$PollMs = 300)
  $end = (Get-Date).AddSeconds($TimeoutSeconds)
  while ($true) {
    $got = & $Until
    if ($got) { return $got }
    if ((Get-Date) -gt $end) { return $null }
    Start-Sleep -Milliseconds $PollMs
  }
}

# 文言が出るまで待つ（「1/2：…」「既に全部入っています」など）。戻りは最初に合った文字
function Wait-ChmonosText {
  param([Parameter(Mandatory)][string]$Like, [double]$TimeoutSeconds = 15)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-ChmonosTexts -Like $Like | Select-Object -First 1 }
}

# 要素が出るまで待つ。戻りは要素
function Wait-ChmonosElement {
  param([Parameter(Mandatory)][string]$Type, [string]$Name, [string]$Like, [double]$TimeoutSeconds = 15)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until {
    Get-ChmonosElements -Type $Type -Name $Name -Like $Like | Select-Object -First 1
  }
}

# 持ち主付きの小窓（MessageBox・ListChoice の窓）。主の窓の子として出るので、デスクトップからは探さない
function Get-ChmonosDialog {
  param([string]$Like = '*')
  $cond = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
  foreach ($w in $(Get-ChmonosRoot).FindAll($TS_::Children, $cond)) {
    if ($w.Current.Name -like $Like) { $w }
  }
}

function Wait-ChmonosDialog {
  param([string]$Like = '*', [double]$TimeoutSeconds = 15)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-ChmonosDialog -Like $Like | Select-Object -First 1 }
}

# 小窓の中の文字（知らせの文言を確かめる）。-Like で絞る
function Get-ChmonosDialogText {
  param($Dialog, [string]$Like = '*')
  if (-not $Dialog) { return }
  foreach ($e in $Dialog.FindAll($TS_::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $n = $e.Current.Name
    if ($n -and $n -like $Like -and $n.Length -gt 1) { $n }
  }
}

# ---- 名前で押す（座標を目分量で決めない） ----

# 名前・文字・要素のどれかで押す。UI Automation の Invoke が効かない部品（コマンドをクリックで呼ぶ切り替えボタン・
# ItemsControl の中の部品・小窓のボタン）でも、中心を実入力で押せる。-UserWasTold は実入力の決まり
function Invoke-ChmonosClick {
  param(
    [string]$Name, [string]$Like, [string]$Type = 'Button', $Element, $Scope,
    [int]$Index = 0, [Parameter(Mandatory)][switch]$UserWasTold, [switch]$Right, [double]$TimeoutSeconds = 10)

  $el = $Element
  if (-not $el) {
    $el = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until {
      $found = @(Get-ChmonosElements -Type $Type -Name $Name -Like $Like -Scope $Scope)
      if ($found.Count -gt $Index) { $found[$Index] } else { $null }
    }
  }
  if (-not $el) { return "無い: $Type「$Name$Like」" }

  $c = Get-ChmonosCenter $el
  $what = if ($el.Current.Name) { $el.Current.Name } else { $el.Current.ControlType.ProgrammaticName }
  "$(Invoke-ChmonosRealClick -X $c.X -Y $c.Y -UserWasTold:$UserWasTold -Right:$Right)（「$what」）"
}

# メニューの項目（右クリックのメニュー・「開く ▾」）。ポップアップは窓の外の別の窓に出るので、主の窓からは探せない。
# 自分のアプリの物だけを拾う（Unity や Windows のメニューを掴まない）
function Get-ChmonosMenuItem {
  param([string]$Name, [string]$Like = '*')
  $pid_ = (Get-ChmonosApp).Id
  $cond = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
  foreach ($e in $A_::RootElement.FindAll($TS_::Descendants, $cond)) {
    if ($e.Current.ProcessId -ne $pid_) { continue }
    $n = $e.Current.Name
    if ($Name -and $n -ne $Name) { continue }
    if ($n -notlike $Like) { continue }
    $e
  }
}

# メニューの項目を押す。-Expand なら下の段を開くだけ（「開く ▸」「Unity ▸」）
function Invoke-ChmonosMenuItem {
  param([string]$Name, [string]$Like = '*', [switch]$Expand, [Parameter(Mandatory)][switch]$UserWasTold, [double]$TimeoutSeconds = 10)
  $item = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-ChmonosMenuItem -Name $Name -Like $Like | Select-Object -First 1 }
  if (-not $item) { return "メニューに無い: 「$Name$Like」" }
  if ($Expand) {
    $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 400
    return "開いた: メニュー「$($item.Current.Name)」"
  }
  Invoke-ChmonosClick -Element $item -UserWasTold:$UserWasTold
}

# 小窓を、その中のボタンを押して閉じる。**閉じたことを確かめ**、閉じなければもう一度押す
# （1回目のクリックが窓を選ぶだけに使われることがある）。それでも閉じなければ、既定のボタンを Enter で押す
function Close-ChmonosDialog {
  param([string]$Button = 'OK', [string]$Like = '*', [Parameter(Mandatory)][switch]$UserWasTold, [double]$TimeoutSeconds = 10)
  $dialog = Wait-ChmonosDialog -Like $Like -TimeoutSeconds $TimeoutSeconds
  if (-not $dialog) { return "小窓が出ていない（$Like）" }

  $handle = [IntPtr]$dialog.Current.NativeWindowHandle
  $title = $dialog.Current.Name
  $target = $dialog.FindFirst($TS_::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A_::NameProperty, $Button)))
  if (-not $target) { return "「$title」に「$Button」が無い" }

  for ($try = 1; $try -le 2; $try++) {
    $c = Get-ChmonosCenter $target
    [void](Invoke-ChmonosRealClick -X $c.X -Y $c.Y -UserWasTold:$UserWasTold)
    if (-not (Wait-ChmonosCondition -TimeoutSeconds 3 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($handle) })) { continue }
    return "閉じた: 「$title」の「$Button」（$try 回目）"
  }

  # 最後の手。Enter は既定のボタン（OK・これを送る）を押す
  [void][ChmonosWin]::Bring($handle); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  if (Wait-ChmonosCondition -TimeoutSeconds 3 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($handle) }) {
    return "閉じた: 「$title」（Enter・クリックでは閉じなかった）"
  }

  "閉じられない: 「$title」の「$Button」"
}

# ---- 確かめ用の足跡（Start-ChmonosApp が付ける。出した窓の文言・押されたボタン・命令・Unity） ----

# 足跡を読む。-Kind で種類を絞る（知らせ・選ぶ・命令・Unity）、-Last で末尾だけ。
# 「この文言が出たか」は、撮って読むより速くて確かに分かる
function Get-ChmonosTrace {
  param([string]$Kind = '*', [string]$Like = '*', [int]$Last = 40)
  if (-not (Test-Path $ChmonosTraceFile)) { return '足跡が無い（-NoTrace で起動した？）' }
  Get-Content $ChmonosTraceFile | Where-Object {
    $parts = $_ -split "`t", 3
    $parts.Count -ge 3 -and $parts[1] -like $Kind -and $parts[2] -like $Like
  } | Select-Object -Last $Last
}

# 足跡に文言が出るまで待つ（窓を撮らずに「出たか」を確かめる）
function Wait-ChmonosTrace {
  param([Parameter(Mandatory)][string]$Like, [string]$Kind = '*', [double]$TimeoutSeconds = 30)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until {
    $hit = @(Get-ChmonosTrace -Kind $Kind -Like $Like -Last 200)
    if ($hit.Count -gt 0 -and $hit[0] -notlike '足跡が無い*') { $hit[-1] } else { $null }
  }
}

# ---- 本番が変わっていないか（確かめの前に控え、後で照らす） ----

function Get-ProductionState {
  $settings = Join-Path $ChmonosProduction 'settings.json'
  $loc = Join-Path $ChmonosProduction 'location.json'
  [pscustomobject]@{
    # 日時は文字で持つ（ConvertFrom-Json が日時に読み替えて比べられなくなる）
    SettingsWrite = if (Test-Path $settings) { (Get-Item $settings).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') } else { '(無い)' }
    # items/ には説明の HTML も同じ数だけあるので *.json で数える
    ItemJson      = @(Get-ChildItem (Join-Path $ChmonosProduction 'items') -Filter *.json -File -ErrorAction SilentlyContinue).Count
    LocationJson  = if (Test-Path $loc) { (Get-Content $loc -Raw).Trim() } else { '(無い)' }
  }
}

function Save-ProductionBaseline { $s = Get-ProductionState; $s | ConvertTo-Json | Set-Content $ChmonosBaselineFile; "本番を控えた: settings.json $($s.SettingsWrite)・items $($s.ItemJson) 件" }

function Test-ProductionUntouched {
  $now = Get-ProductionState
  if (-not (Test-Path $ChmonosBaselineFile)) { return "控えが無い（Save-ProductionBaseline を先に）。今: settings.json $($now.SettingsWrite)・items $($now.ItemJson) 件" }
  $was = Get-Content $ChmonosBaselineFile -Raw | ConvertFrom-Json
  $diff = @()
  foreach ($k in 'SettingsWrite', 'ItemJson', 'LocationJson') { if ("$($was.$k)" -ne "$($now.$k)") { $diff += "$k：$($was.$k) → $($now.$k)" } }
  if ($diff.Count -eq 0) { "本番は変わっていない: settings.json $($now.SettingsWrite)・items $($now.ItemJson) 件" }
  else { "本番が変わった！ユーザに知らせる: " + ($diff -join ' / ') }
}
