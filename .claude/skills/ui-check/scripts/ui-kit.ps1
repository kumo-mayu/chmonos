# Chmonos の画面を確かめる道具。ツールの呼び出しごとにドットで読み込む（シェルの状態は呼び出しをまたいで残らない）：
#   . "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"
#
# CLAUDE.md の決め事 4・5 をここで守らせる：
# - 本番（%LOCALAPPDATA%\Chmonos）と friendtest（ユーザの作業用の写し）では起動しない
# - 閉じるのは、この道具で起動したアプリだけ（ユーザが開いているアプリを巻き込まない。前の道具は名前で全部落としていた）
# - 実入力は、窓が前面にあり、座標が窓の中にあるときだけ送る（要素が見つからず (0,0)＝デスクトップを押した事故がある）
# - アプリは写しごとに控える。複数開いているときは、相手を決めた呼び出し（Use-ChmonosStore・-Store）だけを通す
# - 並行で起動するのは、裏の取得を切った写しだけ（BOOTH への問い合わせは、アプリを何本開いても合わせて1本ずつ）
#
# 関数の一覧は ../tools.md。よく使う操作は ui-ops.ps1、作り物のファイルは fixtures.ps1
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
  // 主の窓の上に出ている小窓（知らせの窓・選ぶ窓）。主の窓を前に出すとその下に隠れるので、前に出すのはこちら
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
# 起動したアプリの控え。**写しごとに1つ**（chmonos-ui-check\<写しの名前>.json）。
# 前は1つのファイルに pid を1つだけ控えていて、2人が起動すると互いのアプリを閉じ合った（2026-09-30）。
# $ChmonosPidFile は「最後に起動した物」の写しとして残す（前の版の道具と、このファイルを直に読む台本のため）
$ChmonosStateDir = Join-Path $env:TEMP 'chmonos-ui-check'
$ChmonosPidFile = Join-Path $env:TEMP 'chmonos-ui-check.json'
$ChmonosBaselineFile = Join-Path $env:TEMP 'chmonos-prod-baseline.json'
# 足跡も写しごとに分ける（2本が同じファイルに書くと行が混ざる）。この変数は「今の相手の足跡」を指す。
# Start-ChmonosApp・Use-ChmonosStore・Get-ChmonosApp が相手を決めたときに書き換える。
# 既定の値は、前の版の道具で起動したアプリの足跡の場所
$global:ChmonosTraceFile = Join-Path $env:TEMP 'chmonos-uitrace.log'
$ChmonosTraceHistory = Join-Path $env:TEMP 'chmonos-uitrace-history'
$ChmonosScreenLock = Join-Path $ChmonosStateDir 'screen.lock'
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

# 写しを作る。どちらかを指定する：
#   -From   … 今ある保存先を写す。短い名前・フルパス・'prod'（本番を読むだけ）
#   -Recipe … 作り物のデータで、台本から組み立てる（sandbox-recipes.ps1。台本の一覧は Get-ChmonosRecipes）。
#             -Items は商品の件数、-Full は大きなファイルと多い件数（時間とディスクを食う）
# どちらも、既にあれば断る
function New-ChmonosSandbox {
  [CmdletBinding(DefaultParameterSetName = 'Copy')]
  param(
    [Parameter(Mandatory, ParameterSetName = 'Copy')][string]$From,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory, ParameterSetName = 'Recipe')][string]$Recipe,
    [Parameter(ParameterSetName = 'Recipe')][int]$Items = 0,
    [Parameter(ParameterSetName = 'Recipe')][switch]$Full)
  $dst = Resolve-ChmonosStore $Name
  Assert-ChmonosSandbox $dst
  if (Test-Path $dst) { throw "もうある: $dst（上書きしない。消すならユーザに聞く）" }
  if ($Recipe) {
    # 作り物のファイルの関数は、読み込んでいなければここで読む（読み込み済みなら、置き場の変数を上書きしないように読まない）
    if (-not (Get-Command New-ChmonosFixtureZip -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'fixtures.ps1') }
    . (Join-Path $PSScriptRoot 'sandbox-recipes.ps1')
    try { $log = Invoke-ChmonosRecipe -Recipe $Recipe -Root $dst -Set (Get-ChmonosStoreKey $dst) -Items $Items -Full:$Full }
    catch {
      # 途中まで作った写しを残すと、次に「もうある」で断られ、半端な状態が使われる
      if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force -ErrorAction SilentlyContinue }
      throw
    }
    $log
    return "作った: $dst（台本: $Recipe）"
  }
  $src = if ($From -eq 'prod') { $ChmonosProduction } else { Resolve-ChmonosStore $From }
  if (-not (Test-Path $src)) { throw "写す元が無い: $src" }
  Copy-Item $src $dst -Recurse
  # 本番の location.json は friendtest を指している。写しに残すと、写しを開いたつもりで friendtest が開く
  $loc = Join-Path $dst 'location.json'
  if (Test-Path $loc) { Remove-Item $loc; "写しの location.json を外した（元の保存先へ飛ばないように）" }
  "作った: $dst（元: $src）"
}

# 台本の一覧（名前と、何が入るか）
function Get-ChmonosRecipes {
  . (Join-Path $PSScriptRoot 'sandbox-recipes.ps1')
  foreach ($k in $ChmonosRecipes.Keys) { [pscustomobject]@{ Recipe = $k; 中身 = $ChmonosRecipes[$k] } }
}

# ---- 写しを控える／戻す ----
#
# 確かめで写しに書き込む前に丸ごと控え、終わったら戻して、一致を確かめる。
# 担当ごとに作業用フォルダへ robocopy していて、戻し忘れ・戻したつもりが残っていた（sandboxes.md の説明と中身がずれた。2026-09-30）。
# 控えの置き場はリポジトリの外（友人のデータの写しも控えるので、リポジトリにも作業用フォルダにも置かない）

$ChmonosBackupHome = Join-Path $env:LOCALAPPDATA 'Chmonos-sandbox-backups'

function Get-ChmonosBackupDir([string]$Root, [string]$Name) {
  if ($Name -notmatch '^[\w\-.]+$' -or $Name -match '^\.+$') { throw "控えの名前に使えない字がある: $Name（英数字・_・-・. だけ）" }
  Join-Path (Join-Path $ChmonosBackupHome (Split-Path $Root -Leaf)) $Name
}

# 控える・戻す前の守り。書きかけを写さないように、アプリがその写しを開いていたら断る。
# この道具で起動した物は控えで分かる。人が自分で開いた物は、アプリが握る app.lock（開いている間だけ在る）で分かる
function Assert-ChmonosSandboxIdle([string]$Root) {
  Assert-ChmonosSandbox $Root
  if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "保存先が無い: $Root" }
  $mine = @(Get-ChmonosRunning | Where-Object { $_.Store -ieq $Root })
  if ($mine.Count) { throw "アプリがこの写しを開いている: pid=$($mine[0].Pid)（先に Stop-ChmonosApp -Store）" }
  if (Test-Path -LiteralPath (Join-Path $Root 'app.lock')) { throw "アプリがこの写しを開いている（app.lock がある）: $Root" }
}

function Invoke-ChmonosMirror([string]$From, [string]$To) {
  # /MIR は写す先の余分も消す。/COPY:DAT /DCOPY:DAT で日時も写す（-Quick の照らしが日時で比べるため）
  robocopy $From $To /MIR /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
  if ($LASTEXITCODE -ge 8) { throw "写せなかった（robocopy $LASTEXITCODE）: $From → $To" }
}

# 写しを控える。-Name は控えの名前（既定 before）。同じ名前の控えが既にあれば断る
# （確かめで変えた後の状態で、前の控えを上書きしないように。作り直すなら -Force）
function Backup-ChmonosSandbox {
  param([Parameter(Mandatory)][string]$Store, [string]$Name = 'before', [switch]$Force)
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandboxIdle $root
  $dst = Get-ChmonosBackupDir $root $Name
  if ((Test-Path -LiteralPath $dst) -and -not $Force) { throw "もう控えがある: $dst（上書きしない。戻すなら Restore-ChmonosSandbox、作り直すなら -Force）" }
  [IO.Directory]::CreateDirectory($dst) | Out-Null
  Invoke-ChmonosMirror $root $dst
  # どの保存先の控えかを控えの隣に書く（戻す先を取り違えないように）。ConvertTo-Json は使わない（決め事）
  $files = @(Get-ChildItem -LiteralPath $dst -Recurse -File -Force).Count
  $mark = "{`n  `"source`": `"$($root.Replace('\', '\\'))`",`n  `"savedAt`": `"$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))`",`n  `"files`": $files`n}`n"
  [IO.File]::WriteAllText("$dst.json", $mark, [Text.UTF8Encoding]::new($false))
  $cmp = Compare-ChmonosSandbox -Store $Store -Name $Name
  if (-not $cmp.Same) { throw "控えたが一致しない: $($cmp.Text)" }
  "控えた: $root → $dst（$files ファイル）"
}

# 写しと控えを照らす。既定は中身（SHA-256）まで比べる。-Quick は大きさと更新日時だけ（大きい写し向け）。
# 違いは数と上のフォルダごとの内訳で返す。**ファイル名は既定では出さない**
# （友人のデータの写しでは、ファイル名が商品 ID。報告や記録に写さないため）。見たいときだけ -ShowPaths
function Compare-ChmonosSandbox {
  param([Parameter(Mandatory)][string]$Store, [string]$Name = 'before', [switch]$Quick, [switch]$ShowPaths)
  $root = Resolve-ChmonosStore $Store
  $bak = Get-ChmonosBackupDir $root $Name
  if (-not (Test-Path -LiteralPath $bak)) { throw "控えが無い: $bak（Backup-ChmonosSandbox）" }
  $list = {
    param($base)
    $map = @{}
    foreach ($f in Get-ChildItem -LiteralPath $base -Recurse -File -Force) {
      $stamp = if ($Quick) { "$($f.Length)|$($f.LastWriteTimeUtc.Ticks)" } else { "$($f.Length)|$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)" }
      $map[$f.FullName.Substring($base.Length + 1)] = $stamp
    }
    $map
  }
  $a = & $list $root; $b = & $list $bak
  $onlyStore = @($a.Keys | Where-Object { -not $b.ContainsKey($_) })
  $onlyBackup = @($b.Keys | Where-Object { -not $a.ContainsKey($_) })
  $changed = @($a.Keys | Where-Object { $b.ContainsKey($_) -and $a[$_] -ne $b[$_] })
  $same = ($onlyStore.Count + $onlyBackup.Count + $changed.Count) -eq 0
  $top = { param($paths) (@($paths | Group-Object { if ($_.Contains('\')) { $_.Split('\')[0] + '\' } else { '(直下)' } } | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" }) -join '・') }
  $text = if ($same) { "一致（$($a.Count) ファイル・$(if ($Quick) { '大きさと日時' } else { '中身' })で照らした）" }
  else {
    $parts = @()
    if ($changed.Count) { $parts += "中身が違う $($changed.Count)（$(& $top $changed)）" }
    if ($onlyStore.Count) { $parts += "写しにだけある $($onlyStore.Count)（$(& $top $onlyStore)）" }
    if ($onlyBackup.Count) { $parts += "控えにだけある $($onlyBackup.Count)（$(& $top $onlyBackup)）" }
    '違う: ' + ($parts -join ' / ')
  }
  $result = [pscustomobject]@{ Same = $same; Text = $text; Changed = $changed.Count; OnlyInStore = $onlyStore.Count; OnlyInBackup = $onlyBackup.Count; Paths = $null }
  if ($ShowPaths) { $result.Paths = @($changed | ForEach-Object { "違う  $_" }) + @($onlyStore | ForEach-Object { "写しだけ  $_" }) + @($onlyBackup | ForEach-Object { "控えだけ  $_" }) }
  $result
}

# 写しを控えた状態へ戻し、一致を確かめる。確かめで足したファイルは消える。
# 回ごとに同じ所から始めたいときは、そのまま何度でも呼ぶ。最後の回は -Done で控えも消す
function Restore-ChmonosSandbox {
  param([Parameter(Mandatory)][string]$Store, [string]$Name = 'before', [switch]$Done)
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandboxIdle $root
  $bak = Get-ChmonosBackupDir $root $Name
  if (-not (Test-Path -LiteralPath $bak) -or -not (Test-Path -LiteralPath "$bak.json")) { throw "控えが無い: $bak（Backup-ChmonosSandbox）" }
  $mark = Get-Content -LiteralPath "$bak.json" -Raw | ConvertFrom-Json
  if ("$($mark.source)".TrimEnd('\') -ine $root) { throw "この控えは別の保存先の物: $($mark.source)（戻す先 $root）" }
  Invoke-ChmonosMirror $bak $root
  $cmp = Compare-ChmonosSandbox -Store $Store -Name $Name
  if (-not $cmp.Same) { throw "戻したが一致しない（控えは残した）: $($cmp.Text)" }
  if ($Done) {
    Remove-Item -LiteralPath $bak -Recurse -Force; Remove-Item -LiteralPath "$bak.json" -Force
    $parent = Split-Path $bak
    if (-not @(Get-ChildItem -LiteralPath $parent -Force).Count) { Remove-Item -LiteralPath $parent -Force }
    return "戻した: $root（$($cmp.Text)。控えは消した）"
  }
  "戻した: $root（$($cmp.Text)。控えは残してある: $bak）"
}

# 置いてある控えの一覧（戻し忘れ・消し忘れを見つける）
function Get-ChmonosSandboxBackups {
  if (-not (Test-Path -LiteralPath $ChmonosBackupHome)) { return }
  foreach ($m in Get-ChildItem -LiteralPath $ChmonosBackupHome -Recurse -Filter *.json -File -Depth 1) {
    $j = Get-Content -LiteralPath $m.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{ Store = Split-Path $m.DirectoryName -Leaf; Name = $m.BaseName; SavedAt = $j.savedAt; Files = $j.files; Source = $j.source }
  }
}

# ---- 起動と終了 ----

# 控えのファイルの名前に使う、写しの短い名前。%LOCALAPPDATA%\BoothAssetManager-<名前> なら <名前>。
# ほかの場所の保存先は、同じ末尾の名前がぶつからないように、パスから出した8桁を足す
function Get-ChmonosStoreKey([string]$Root) {
  $leaf = Split-Path $Root -Leaf
  if ((Split-Path $Root -Parent) -ieq $env:LOCALAPPDATA.TrimEnd('\') -and $leaf -like 'BoothAssetManager-*') { return $leaf.Substring('BoothAssetManager-'.Length) }
  $sha = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Root.ToLowerInvariant()))
  ($leaf -replace '[^\w\-.]', '_') + '-' + [Convert]::ToHexString($sha).Substring(0, 8).ToLowerInvariant()
}

# この道具で起動して、今も開いているアプリの一覧（写しごと）。閉じた物の控えは、見つけたときに片付ける。
# pid の使い回しに備えて起動時刻も照らす。前の版の道具で起動した物（$ChmonosPidFile だけにある）も拾う
function Get-ChmonosRunning {
  $files = @()
  if (Test-Path -LiteralPath $ChmonosStateDir) { $files += @(Get-ChildItem -LiteralPath $ChmonosStateDir -Filter *.json -File) }
  if (Test-Path -LiteralPath $ChmonosPidFile) { $files += Get-Item -LiteralPath $ChmonosPidFile }
  $seen = @{}
  foreach ($f in $files) {
    try { $info = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json } catch { continue }
    if (-not $info.pid) { continue }
    $p = Get-Process -Id $info.pid -ErrorAction SilentlyContinue
    # 実行ファイルの名前は版（Debug・Release・別のフォルダ）で変わらない
    if (-not $p -or $p.ProcessName -ne $ChmonosProcessName -or $p.StartTime.Ticks -ne [long]$info.startTicks) {
      Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
      continue
    }
    if ($seen.ContainsKey([int]$info.pid)) { continue }
    $seen[[int]$info.pid] = $true
    $root = "$($info.store)".TrimEnd('\')
    [pscustomobject]@{
      Key = Get-ChmonosStoreKey $root; Store = $root; Pid = [int]$info.pid; Process = $p
      # 前の版の道具で起動した物は、足跡の場所を控えていない（1つのファイルに書いていた）
      Trace = if ($info.trace) { "$($info.trace)" } elseif ($info.PSObject.Properties['trace']) { $null } else { Join-Path $env:TEMP 'chmonos-uitrace.log' }
      Temp = if ($info.temp) { "$($info.temp)" } else { $null }
      StartTicks = [long]$info.startTicks
    }
  }
}

# 相手にするアプリを決める。順に：-Store で指した写し → Use-ChmonosStore で決めた写し（このシェルで Start-ChmonosApp した写し）
# → 環境変数 CHMONOS_UI_STORE → 開いているのが1つならそれ。
# **複数開いていて、どれかを指していないときは断る**（黙って最後の物を相手にすると、別の担当のアプリを操作する・閉じる）
function Resolve-ChmonosTarget([string]$Store) {
  $running = @(Get-ChmonosRunning)
  $want = if ($Store) { $Store } elseif ($global:ChmonosCurrentStore) { $global:ChmonosCurrentStore } elseif ($env:CHMONOS_UI_STORE) { $env:CHMONOS_UI_STORE } else { $null }
  if ($want) {
    $root = Resolve-ChmonosStore $want
    $hit = @($running | Where-Object { $_.Store -ieq $root })
    if (-not $hit.Count) { throw "この道具で起動したアプリが無い: $root（Start-ChmonosApp -Store）" }
    $target = $hit[0]
  }
  elseif ($running.Count -eq 1) { $target = $running[0] }
  elseif ($running.Count -eq 0) { throw 'この道具で起動したアプリが無い（Start-ChmonosApp）' }
  else { throw "アプリが複数開いている（$(($running | ForEach-Object { $_.Key }) -join '・')）。どれを相手にするかを Use-ChmonosStore <写し> か -Store で指す" }
  if ($target.Trace) { $global:ChmonosTraceFile = $target.Trace }
  $target
}

# このシェルで相手にする写しを決める（並行で確かめるとき、道具を読み込んだ直後に1回呼ぶ）
function Use-ChmonosStore {
  param([Parameter(Mandatory)][string]$Store)
  $global:ChmonosCurrentStore = Resolve-ChmonosStore $Store
  $hit = @(Get-ChmonosRunning | Where-Object { $_.Store -ieq $global:ChmonosCurrentStore })
  if ($hit.Count -and $hit[0].Trace) { $global:ChmonosTraceFile = $hit[0].Trace }
  "相手: $global:ChmonosCurrentStore$(if ($hit.Count) { "（pid=$($hit[0].Pid)）" } else { '（まだ起動していない）' })"
}

# 起動すると BOOTH へ問い合わせ得る設定か。問い合わせの門（1本ずつ・1.5秒）はアプリ1本ごとなので、
# 2本が同時に問い合わせると、合わせて1本ずつにならない。並行で起動する写しは、裏の取得を切ってあることを求める
function Get-ChmonosStoreNetworkRisk([string]$Root) {
  $settings = Join-Path $Root 'settings.json'
  if (-not (Test-Path -LiteralPath $settings)) { return '設定がまだ無い（既定では、使っていない間の取得が入る）' }
  $text = [IO.File]::ReadAllText($settings)
  $risk = @()
  if ($text -notmatch '"resumeFetchInBackground"\s*:\s*false') { $risk += '使っていない間の取得（resumeFetchInBackground）が切れていない' }
  if ($text -match '"startImportOnLaunch"\s*:\s*true') { $risk += '起動時の取り込み（startImportOnLaunch）が入っている' }
  $risk -join '・'
}

# 前の足跡を控えへ移す（起動のたびに消えていた。後から「さっきの回で何が出たか」を読めるように）。
# 控えは新しい 40 本だけ残す（1本は数KB〜数百KB。確かめ1日分の起動はこれで足りる）
function Move-ChmonosTraceToHistory([string]$TraceFile, [string]$Key) {
  if (-not (Test-Path -LiteralPath $TraceFile)) { return $null }
  if ((Get-Item -LiteralPath $TraceFile).Length -eq 0) { Remove-Item -LiteralPath $TraceFile -Force; return $null }
  [IO.Directory]::CreateDirectory($ChmonosTraceHistory) | Out-Null
  $stamp = (Get-Item -LiteralPath $TraceFile).LastWriteTime.ToString('yyyyMMdd-HHmmss')
  $dest = Join-Path $ChmonosTraceHistory "$stamp-$Key.log"
  Move-Item -LiteralPath $TraceFile -Destination $dest -Force
  Get-ChildItem -LiteralPath $ChmonosTraceHistory -Filter *.log -File | Sort-Object LastWriteTime -Descending | Select-Object -Skip 40 | Remove-Item -Force
  $dest
}

# -Exe は別の版（Release・脇へビルドした物・前の版）で起動するとき。省くとこの道具のあるリポジトリ（worktree）の Debug。
# -IsolateTemp はアプリの一時フォルダを写しごとに分ける（ほかのアプリが開いているときは、付けなくても分ける）
function Start-ChmonosApp {
  param([Parameter(Mandatory)][string]$Store, [switch]$AllowNew, [int]$SettleSeconds = 6, [switch]$NoTrace, [string]$Exe, [switch]$IsolateTemp)
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandbox $root
  if (-not $AllowNew -and -not (Test-Path $root)) { throw "保存先が無い: $root（初回の窓を見るなら -AllowNew）" }
  $exePath = if ($Exe) { $Exe } else { $ChmonosExe }
  if (-not (Test-Path $exePath)) { throw "ビルドが無い: $exePath（dotnet build）" }
  $key = Get-ChmonosStoreKey $root
  $running = @(Get-ChmonosRunning)
  $same = @($running | Where-Object { $_.Store -ieq $root })
  # 同じ写しを2本は開けない（アプリが保存先ごとに二重起動を止める。「既に起動しています」の窓を見るなら Start-ChmonosSecond）
  if ($same.Count) { throw "前に起動したアプリがまだ開いている: pid=$($same[0].Pid)（Stop-ChmonosApp -Store $key）" }
  if ($running.Count) {
    # 並行で起動する。BOOTH への問い合わせは、アプリを何本開いても合わせて1本ずつ（CLAUDE.md の決め事 1）
    foreach ($r in @($root) + @($running | ForEach-Object { $_.Store })) {
      $risk = Get-ChmonosStoreNetworkRisk $r
      if ($risk) { throw "並行で起動できない: $r は起動すると BOOTH へ問い合わせ得る（$risk）。ほかのアプリ（$(($running | ForEach-Object { $_.Key }) -join '・')）を閉じるか、写しの設定で切ってから" }
    }
  }
  [IO.Directory]::CreateDirectory($ChmonosStateDir) | Out-Null
  $psi = [Diagnostics.ProcessStartInfo]::new($exePath)
  $psi.UseShellExecute = $false; $psi.WorkingDirectory = Split-Path $exePath
  # 空文字を入れても子に渡ることがあるので、起動する側の環境から取り除く。
  # BAM_* と DOTNET_GC* は速さ・メモリの計測で使った変数で、残ると別の条件で動く
  foreach ($n in @($psi.Environment.Keys | Where-Object { $_ -like 'BAM_*' -or $_ -like 'DOTNET_GC*' -or $_ -eq 'BOOTH_ASSET_MANAGER_HOME' })) { [void]$psi.Environment.Remove($n) }
  $psi.Environment['CHMONOS_HOME'] = $root
  # 確かめ用の足跡（出した窓の文言・押されたボタン・実行した命令・Unity の取り込み）。
  # 文言の確かめを撮らずに済む。今回の起動の分だけを読めるように、前の分は控えへ移す（Get-ChmonosTrace -Saved で読める）
  $trace = $null
  if (-not $NoTrace) {
    $trace = Join-Path $ChmonosStateDir "$key.uitrace.log"
    [void](Move-ChmonosTraceToHistory $trace $key)
    $psi.Environment['CHMONOS_UITRACE'] = $trace
    $global:ChmonosTraceFile = $trace
  }
  # アプリは起動のたびに %TEMP%\Chmonos\unpacked（一時的に展開した物）を片付ける。二重起動かどうかは保存先ごとに見るので、
  # 別の写しを開いた2本目が、1本目の展開した物を消してしまう。並行のときは一時フォルダを写しごとに分ける
  $temp = $null
  if ($IsolateTemp -or $running.Count) {
    $temp = Join-Path $ChmonosStateDir "$key.temp"
    [IO.Directory]::CreateDirectory($temp) | Out-Null
    $psi.Environment['TEMP'] = $temp; $psi.Environment['TMP'] = $temp
  }
  $p = [Diagnostics.Process]::Start($psi)
  for ($i = 0; $i -lt 120 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
  if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw "窓が出なかった（pid=$($p.Id)）" }
  $state = @{ pid = $p.Id; startTicks = $p.StartTime.Ticks; store = $root; trace = $trace; temp = $temp } | ConvertTo-Json
  Set-Content -LiteralPath (Join-Path $ChmonosStateDir "$key.json") -Value $state
  Set-Content -LiteralPath $ChmonosPidFile -Value $state
  $global:ChmonosCurrentStore = $root
  Start-Sleep -Seconds $SettleSeconds
  "起動した: pid=$($p.Id) 保存先=$root 窓の題=「$((Get-ChmonosRoot).Current.Name)」"
}

# この道具で起動したアプリ（プロセス）。-Store を省くと今の相手（Resolve-ChmonosTarget の順）
function Get-ChmonosApp {
  param([string]$Store)
  (Resolve-ChmonosTarget $Store).Process
}

# アプリの一時フォルダ（一時的に展開した物の置き場 Chmonos\unpacked の親）。並行で起動したアプリは写しごとに分かれている
function Get-ChmonosAppTemp {
  param([string]$Store)
  $t = (Resolve-ChmonosTarget $Store).Temp
  if ($t) { $t } else { $env:TEMP }
}

# 閉じる。-Store を省くと今の相手。複数開いていて相手を決めていないときは、閉じずにそう返す
# （別の担当のアプリを閉じないように）。自分で起動した物を全部閉じるなら、写しごとに -Store で呼ぶ
function Stop-ChmonosApp {
  param([string]$Store)
  try { $target = Resolve-ChmonosTarget $Store } catch { return $_.Exception.Message }
  $p = $target.Process
  [void]$p.CloseMainWindow()
  if (-not $p.WaitForExit(8000)) { $p.Kill(); [void]$p.WaitForExit(3000) }
  Remove-Item -LiteralPath (Join-Path $ChmonosStateDir "$($target.Key).json") -Force -ErrorAction SilentlyContinue
  if (Test-Path -LiteralPath $ChmonosPidFile) {
    try { if ([int](Get-Content -LiteralPath $ChmonosPidFile -Raw | ConvertFrom-Json).pid -eq $target.Pid) { Remove-Item -LiteralPath $ChmonosPidFile -Force } } catch { }
  }
  # 画面を取っていたら返す（閉じたアプリが画面を握ったままにならないように）
  [void](Unlock-ChmonosScreen -Store $target.Store)
  "閉じた: pid=$($p.Id)"
}

# ---- 画面（実入力と、画面から直に撮る部品）を1人で使う ----
#
# 実際のマウス・キー・前面の窓・画面の絵は PC に1つしか無い。2人が同時に使うと、相手の窓を押す・メニューが閉じる・別の窓が写る。
# アプリが複数開いている間は、画面を取った人（Lock-ChmonosScreen）だけが実入力の部品を使える。
# 1本だけのときは、取らなくても今までどおり動く

function Get-ChmonosScreenLockInfo {
  if (-not (Test-Path -LiteralPath $ChmonosScreenLock)) { return $null }
  try { $j = Get-Content -LiteralPath $ChmonosScreenLock -Raw | ConvertFrom-Json } catch { return $null }
  # 期限を過ぎた物は無い物として扱う（返し忘れで、ずっと使えなくならないように）
  if ([datetime]::ParseExact("$($j.until)", 'yyyy-MM-dd HH:mm:ss', $null) -lt (Get-Date)) { return $null }
  $j
}

# 画面を使う人（＝写し）を決める。-Store で指した写し → このシェルの相手 → 開いている1つ。
# 起動の瞬間を撮るときは、まだ起動していない写しで画面を取るので、開いているかは問わない
function Resolve-ChmonosScreenUser([string]$Store) {
  if ($Store) { return Resolve-ChmonosStore $Store }
  if ($global:ChmonosCurrentStore) { return $global:ChmonosCurrentStore }
  if ($env:CHMONOS_UI_STORE) { return Resolve-ChmonosStore $env:CHMONOS_UI_STORE }
  (Resolve-ChmonosTarget).Store
}

# 画面を取る。-Minutes は期限（既定 10 分。過ぎると自動で外れる。長い確かめは取り直す）。ほかの人が取っていたら断る
function Lock-ChmonosScreen {
  param([string]$Store, [int]$Minutes = 10)
  $root = Resolve-ChmonosScreenUser $Store
  $key = Get-ChmonosStoreKey $root
  $held = Get-ChmonosScreenLockInfo
  if ($held -and "$($held.store)" -ine $root) { throw "画面は $($held.key) の確かめが使っている（$($held.until) まで）" }
  [IO.Directory]::CreateDirectory($ChmonosStateDir) | Out-Null
  $until = (Get-Date).AddMinutes($Minutes).ToString('yyyy-MM-dd HH:mm:ss')
  @{ store = $root; key = $key; until = $until } | ConvertTo-Json | Set-Content -LiteralPath $ChmonosScreenLock
  "画面を取った: $key（$until まで）"
}

# 画面を返す。ほかの人が取っている物は返さない（-Store を省くと、このシェルの相手の分）
function Unlock-ChmonosScreen {
  param([string]$Store)
  $held = Get-ChmonosScreenLockInfo
  if (-not $held) { return '画面は誰も取っていない' }
  try { $root = Resolve-ChmonosScreenUser $Store } catch { $root = $null }
  if (-not $root -or "$($held.store)" -ine $root) { return "画面は $($held.key) が取っている（返さなかった）" }
  Remove-Item -LiteralPath $ChmonosScreenLock -Force
  "画面を返した: $($held.key)"
}

# 実入力を送ってよいか・画面から直に撮ってよいか。だめなら理由を返す（よければ $null）
function Get-ChmonosScreenDenial {
  param([string]$Store)
  $held = Get-ChmonosScreenLockInfo
  # 自分が誰か（どの写しの確かめか）が決まらないとき：複数開いていれば、相手を決めてからにする。1本も開いていなければ、取っている人がいるかだけを見る
  try { $root = Resolve-ChmonosScreenUser $Store }
  catch {
    if (@(Get-ChmonosRunning).Count) { return $_.Exception.Message }
    $root = $null
  }
  if ($held) {
    if ($root -and "$($held.store)" -ieq $root) { return $null }
    return "画面は $($held.key) の確かめが使っている（$($held.until) まで）"
  }
  # 取っている人がいなくても、自分のほかにアプリが開いていれば、取ってからにする
  $others = @(Get-ChmonosRunning | Where-Object { $_.Store -ine $root })
  if ($others.Count) { return "ほかのアプリが開いている（$(($others | ForEach-Object { $_.Key }) -join '・')）。実入力と画面から撮る部品は、Lock-ChmonosScreen で画面を取ってから" }
  $null
}

# ---- UI Automation ----

function Get-ChmonosRoot {
  param([string]$Store)
  $A_::FromHandle((Get-ChmonosApp -Store $Store).MainWindowHandle)
}

# -Type は ControlType の名前（Button・Text・Edit・RadioButton・CheckBox・ListItem・DataItem・Window…）。
# GridView の一覧の行は ListItem ではなく DataItem。持ち主付きの窓（ダイアログ）は主の窓の子として出る
function Get-ChmonosElements {
  param([Parameter(Mandatory)][string]$Type, [string]$Name, [string]$Like, $Scope, [string]$Store)
  $root = if ($Scope) { $Scope } else { Get-ChmonosRoot -Store $Store }
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

# ---- AutomationId で探す・押す ----
#
# 名前（画面の文言）や並びの順で探すと、文言を直しただけ・欄を1つ足しただけで確かめが壊れる。
# 操作できる部品には AutomationId が付いている（2026-09-30。一覧は docs/spec/ui-input.md の「読み上げの名前」、
# XAML を AutomationProperties.AutomationId=" で引く）。**ID がある部品は、まず ID で探す。**
# 行ごとに繰り返す部品（カード・星・行のボタン）は同じ ID が並ぶので、-Name / -Like（その行の名前）か -Index で1つに絞る

# アプリの窓の全部（主の窓と、その外に出る別の窓＝メニュー・ポップアップ・持ち主の無い知らせ）。
# メニューの項目は主の窓の下には出ないので、ID で探すときはここを全部見る
function Get-ChmonosWindows {
  param([string]$Store)
  $app = Get-ChmonosApp -Store $Store
  $main = $A_::FromHandle($app.MainWindowHandle)
  $main
  $mine = New-Object System.Windows.Automation.PropertyCondition($A_::ProcessIdProperty, $app.Id)
  foreach ($w in $A_::RootElement.FindAll($TS_::Children, $mine)) {
    if ([IntPtr]$w.Current.NativeWindowHandle -ne $app.MainWindowHandle) { $w }
  }
}

# AutomationId で探す。-Id は '*' '?' を使える（'SearchModule.*.Remove'。そのときは全部の要素をなめるので遅い）。
# -Name / -Like は要素の名前で絞る。-Scope を省くと、アプリの窓の全部（メニューの中も）から探す
function Get-ChmonosById {
  param([Parameter(Mandatory)][string]$Id, [string]$Name, [string]$Like, $Scope, [string]$Store)
  $roots = if ($Scope) { @($Scope) } else { @(Get-ChmonosWindows -Store $Store) }
  $wild = $Id -match '[*?]'
  $cond = if ($wild) { [System.Windows.Automation.Condition]::TrueCondition } else { New-Object System.Windows.Automation.PropertyCondition($A_::AutomationIdProperty, $Id) }
  $seen = @{}
  foreach ($r in $roots) {
    try { $found = $r.FindAll($TS_::Subtree, $cond) } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    foreach ($e in $found) {
      try { $aid = $e.Current.AutomationId; $n = $e.Current.Name; $key = ($e.GetRuntimeId() -join '.') } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
      if ($wild -and ($aid -notlike $Id)) { continue }
      if ($Name -and $n -ne $Name) { continue }
      if ($Like -and $n -notlike $Like) { continue }
      # 持ち主付きの窓は、主の窓の下とデスクトップの直下の両方から見えることがある
      if ($seen.ContainsKey($key)) { continue }
      $seen[$key] = $true
      $e
    }
  }
}

# 今の画面に出ている ID の一覧（ID・型・名前・押せるか・個数）。「この画面で何を ID で探せるか」を、XAML を読まずに知る。
# WPF の部品が最初から持つ ID（スクロールバーの PageUp・LineDown・Thumb など）は除く
function Get-ChmonosIds {
  param([string]$Like = '*', $Scope, [string]$Store)
  $builtin = 'PageUp', 'PageDown', 'LineUp', 'LineDown', 'Thumb', 'VerticalScrollBar', 'HorizontalScrollBar', 'DecreaseLarge', 'IncreaseLarge', 'PART_EditableTextBox', 'Minimize', 'Maximize', 'Close', 'Restore', 'TitleBar', 'SmallDecrement', 'SmallIncrement', 'LargeDecrement', 'LargeIncrement'
  Get-ChmonosById -Id '*' -Scope $Scope -Store $Store | ForEach-Object {
    try { $c = $_.Current; if (-not $c.AutomationId -or $builtin -contains $c.AutomationId -or $c.AutomationId -notlike $Like) { return }
      [pscustomobject]@{ Id = $c.AutomationId; Type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''; Name = $c.Name; Enabled = $c.IsEnabled } } catch { }
  } | Group-Object Id, Type | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject]@{ Id = $first.Id; Type = $first.Type; Count = $_.Count; Enabled = $first.Enabled; Name = if ($_.Count -gt 1) { "$($first.Name) …" } else { $first.Name } }
  }
}

# ID の部品が出るまで待つ。戻りは要素（出なければ $null）
function Wait-ChmonosById {
  param([Parameter(Mandatory)][string]$Id, [string]$Name, [string]$Like, $Scope, [double]$TimeoutSeconds = 15)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-ChmonosById -Id $Id -Name $Name -Like $Like -Scope $Scope | Select-Object -First 1 }
}

# ID の部品の名前（＝読み上げる文。文言の部品なら、画面に出ている文そのもの）を読む
function Get-ChmonosTextById {
  param([Parameter(Mandatory)][string]$Id, $Scope)
  Get-ChmonosById -Id $Id -Scope $Scope | ForEach-Object { $_.Current.Name }
}

# ID で探して押す（持っている操作で。Invoke-ChmonosElement）。出るまで -TimeoutSeconds 待つ。
# 戻りは「押した: …」か「無い: …」か「押せない: …」。同じ ID が並ぶ物は -Name / -Like / -Index で絞る
function Invoke-ChmonosById {
  param([Parameter(Mandatory)][string]$Id, [string]$Name, [string]$Like, [int]$Index = 0, $Scope, [double]$WaitSeconds = 0.5, [double]$TimeoutSeconds = 5)
  $state = @{ count = 0 }
  $el = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until {
    $found = @(Get-ChmonosById -Id $Id -Name $Name -Like $Like -Scope $Scope)
    $state.count = $found.Count
    if ($found.Count -gt $Index) { $found[$Index] } else { $null }
  }
  if (-not $el) { return "無い: $Id$(if ($Name -or $Like) { "「$Name$Like」" })（$($state.count) 件）" }
  $label = $el.Current.Name
  if (-not $el.Current.IsEnabled) { return "押せない: $Id「$label」（無効）" }
  try { $how = Invoke-ChmonosElement $el } catch { return "押せない: $Id「$label」（$($_.Exception.Message)）" }
  Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000))
  "押した: $Id「$label」($how・同じ ID $($state.count) 件)"
}

# ID の欄に値を入れる。候補付きの入力欄（SuggestBox）は外側が Custom なので、中の入力欄に入れる
function Set-ChmonosValueById {
  param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][AllowEmptyString()][string]$Value, [int]$Index = 0, $Scope, [double]$WaitSeconds = 0.5, [double]$TimeoutSeconds = 5)
  $el = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { $f = @(Get-ChmonosById -Id $Id -Scope $Scope); if ($f.Count -gt $Index) { $f[$Index] } else { $null } }
  if (-not $el) { return "無い: $Id" }
  $vp = [System.Windows.Automation.ValuePattern]::Pattern
  $box = $el
  $has = { param($e) @($e.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }) -contains 'ValuePatternIdentifiers.Pattern' }
  if (-not (& $has $box)) {
    $edit = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $box = $el.FindFirst($TS_::Descendants, $edit)
    if (-not $box -or -not (& $has $box)) { return "入れられない: $Id（値を持つ欄が無い）" }
  }
  $box.GetCurrentPattern($vp).SetValue($Value)
  Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000))
  "入れた: $Id「$($el.Current.Name)」← $Value"
}

# メニュー（「＋ 条件を追加」・行の［開く ▾］）を開き、中の項目を ID で押す。どちらも UI Automation（実入力を使わない）。
# 下の段は窓の外の別の窓に出るが、Get-ChmonosById はアプリの窓の全部を見る。
#   -Menu      … 開くメニューの要素か ID。同じ ID のメニューが行ごとに並ぶときは -MenuName / -MenuLike / -MenuIndex で絞る
#   -Item      … 項目の ID（'*' を使える）。-ItemName / -ItemLike は項目の名前で絞る
#   -OpenOnly  … 押さずに、開いたままの項目の要素を返す（並び・押せるかを見る）
# 戻りは「押した: …」「項目が無い: …（在る項目: …）」「押せない: …（無効）」「メニューが無い: …」
function Invoke-ChmonosMenuById {
  param([Parameter(Mandatory)]$Menu, [Parameter(Mandatory)][string]$Item, [string]$MenuName, [string]$MenuLike, [int]$MenuIndex = 0,
    [string]$ItemName, [string]$ItemLike, [double]$WaitSeconds = 0.5, [double]$TimeoutSeconds = 5, [switch]$OpenOnly)
  $menuEl = if ($Menu -is [string]) {
    Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { $f = @(Get-ChmonosById -Id $Menu -Name $MenuName -Like $MenuLike); if ($f.Count -gt $MenuIndex) { $f[$MenuIndex] } else { $null } }
  } else { $Menu }
  if (-not $menuEl) { return "メニューが無い: $Menu$(if ($MenuName -or $MenuLike) { "「$MenuName$MenuLike」" })" }
  $ec = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
  try { $menuEl.GetCurrentPattern($ec).Expand() } catch { return "メニューが開けない: $($menuEl.Current.AutomationId)「$($menuEl.Current.Name)」（$($_.Exception.Message)）" }
  $find = { Get-ChmonosById -Id $Item -Name $ItemName -Like $ItemLike | Select-Object -First 1 }
  $mi = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
  $isGroup = { param($e) try { "$($e.GetCurrentPattern($ec).Current.ExpandCollapseState)" -ne 'LeafNode' } catch { $false } }
  # 見つからなかったときに並べる、在った項目（ID の打ち間違い・その行には出ない項目にすぐ気付けるように）
  $seen = [System.Collections.Generic.List[string]]::new()
  $note = {
    foreach ($e in $menuEl.FindAll($TS_::Descendants, $mi)) {
      if (& $isGroup $e) { continue }
      $s = if ($e.Current.AutomationId) { $e.Current.AutomationId } else { "「$($e.Current.Name)」" }
      if (-not $seen.Contains($s)) { $seen.Add($s) }
    }
  }
  $target = Wait-ChmonosCondition -TimeoutSeconds 1 -PollMs 150 -Until $find
  if (-not $target) {
    # 見出しの下に項目がぶら下がるメニュー（検索の「＋ 条件を追加」は「BOOTHの情報」「商品の情報」…の下に条件が出る）。
    # 下の段は開くまで UI Automation に出ないので、見出しを1つずつ開いて探す
    $groups = @(Wait-ChmonosCondition -TimeoutSeconds 2 -PollMs 150 -Until {
        $g = @($menuEl.FindAll($TS_::Descendants, $mi) | Where-Object { & $isGroup $_ })
        if ($g.Count) { , $g } else { $null }
      })
    & $note
    foreach ($g in $groups) {
      try { $g.GetCurrentPattern($ec).Expand() } catch { continue }
      $target = Wait-ChmonosCondition -TimeoutSeconds 0.8 -PollMs 100 -Until $find
      if ($target) { break }
      & $note
      try { $g.GetCurrentPattern($ec).Collapse() } catch { }
    }
  }
  if (-not $target) {
    try { $menuEl.GetCurrentPattern($ec).Collapse() } catch { }
    return "項目が無い: $Item$(if ($ItemName -or $ItemLike) { "「$ItemName$ItemLike」" })（在る項目: $($seen -join '・')）"
  }
  if ($OpenOnly) { return $target }
  $label = $target.Current.Name
  if (-not $target.Current.IsEnabled) {
    try { $menuEl.GetCurrentPattern($ec).Collapse() } catch { }
    return "押せない: $($target.Current.AutomationId)「$label」（無効）"
  }
  $id = $target.Current.AutomationId
  try { $how = Invoke-ChmonosElement $target } catch { return "押せない: $id「$label」（$($_.Exception.Message)）" }
  Start-Sleep -Milliseconds ([int]($WaitSeconds * 1000))
  "押した: $id「$label」($how)"
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
  param([Parameter(Mandatory)][string]$Name, $Element, [int[]]$Region, [string]$Store)
  $h = if ($Element) { [IntPtr]$Element.Current.NativeWindowHandle } else { (Get-ChmonosApp -Store $Store).MainWindowHandle }
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
  # -Name に「担当の名前\絵の名前」のように下のフォルダを付けられる（並行の確かめで、同じ名前の絵を上書きし合わないように）
  $path = Join-Path $ChmonosShotDir "$Name.png"
  New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $path
}

# 要素の周りだけを撮る（要素の四角は画面の座標なので、窓の中の座標に直して切る）。
# -Pad は周りの余白、-Width・-Height は要素より広く撮りたいとき（見出しから下の欄まで、など）
function Save-ChmonosShotAround {
  param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][string]$Name, [int]$Pad = 20, [int]$Width = 0, [int]$Height = 0, [string]$Store)
  $h = (Get-ChmonosApp -Store $Store).MainWindowHandle
  $wr = New-Object ChmonosWin+RECT; [void][ChmonosWin]::GetWindowRect($h, [ref]$wr)
  $r = $Element.Current.BoundingRectangle
  if ($r.IsEmpty -or [double]::IsInfinity($r.X)) { throw "位置が取れない（画面の外か空）: 「$($Element.Current.Name)」" }
  $w = if ($Width) { $Width } else { [int]$r.Width + 2 * $Pad }
  $hh = if ($Height) { $Height } else { [int]$r.Height + 2 * $Pad }
  Save-ChmonosShot -Name $Name -Store $Store -Region ([int]($r.X - $wr.L - $Pad)), ([int]($r.Y - $wr.T - $Pad)), $w, $hh
}

# ---- 実入力（UI Automation で届かない所だけ。使う前にユーザへ告げる） ----

# 前面に出す。前面は PC に1つなので、アプリが複数開いている間は画面を取ってから（Lock-ChmonosScreen）
function Show-ChmonosFront {
  $deny = Get-ChmonosScreenDenial
  if ($deny) { return "前面に出すのをやめた：$deny" }
  $ok = [ChmonosWin]::Bring((Get-ChmonosApp).MainWindowHandle); Start-Sleep -Milliseconds 600; "前面に出した: $ok"
}

# X・Y は画面の座標（Get-ChmonosCenter の戻り）。-UserWasTold はユーザへ告げたことの確認で、付けないと動かない
function Invoke-ChmonosRealClick {
  param([Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y, [Parameter(Mandatory)][switch]$UserWasTold, [switch]$Right)
  if (-not $UserWasTold) { throw '実入力の前にユーザへ告げる（CLAUDE.md「確かめ方」）' }
  $deny = Get-ChmonosScreenDenial
  if ($deny) { return "実入力をやめた：$deny" }
  $h = (Get-ChmonosApp).MainWindowHandle
  # 前に出すのは、開いていれば小窓の方（主の窓を前に出すと知らせの窓がその下に隠れ、押しても届かなかった。2026-09-19。当時は MessageBox）
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

# 小窓（知らせの窓・ListChoice の窓・ShowDialog の窓）。どれも WPF の窓で、中の文字もボタンも UI Automation に出る。
# 持ち主付きは主の窓の子として出る。持ち主の無い知らせ（主の窓より前・アプリが後ろにいたとき）はデスクトップの直下に出るので、
# このアプリのプロセスの窓のうち、主の窓でない物と知らせの窓（AutomationId が ChmonosNotice）も拾う
function Get-ChmonosDialog {
  param([string]$Like = '*', [string]$Store)
  $app = Get-ChmonosApp -Store $Store
  $windowType = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
  $seen = @{}

  if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
    foreach ($w in $A_::FromHandle($app.MainWindowHandle).FindAll($TS_::Children, $windowType)) {
      $seen[$w.Current.NativeWindowHandle] = $true
      if ($w.Current.Name -like $Like) { $w }
    }
  }

  $mine = New-Object System.Windows.Automation.AndCondition(
    $windowType,
    (New-Object System.Windows.Automation.PropertyCondition($A_::ProcessIdProperty, $app.Id)))
  foreach ($w in $A_::RootElement.FindAll($TS_::Children, $mine)) {
    $h = $w.Current.NativeWindowHandle
    if ($seen.ContainsKey($h)) { continue }
    # 主の窓より前は、Windows が知らせの窓を「主の窓」と答えることがある。印（AutomationId）で見分ける
    if ([IntPtr]$h -eq $app.MainWindowHandle -and $w.Current.AutomationId -ne 'ChmonosNotice') { continue }
    if ($w.Current.Name -like $Like) { $w }
  }
}

function Wait-ChmonosDialog {
  param([string]$Like = '*', [double]$TimeoutSeconds = 15)
  Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -Until { Get-ChmonosDialog -Like $Like | Select-Object -First 1 }
}

# 小窓の中の文字（知らせの文言を確かめる）。-Like で絞る。知らせの窓の本文は入力欄の名前に丸ごと出る（改行も含む）
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

# 小窓を、その中のボタンを押して閉じる。ボタンは UI Automation の Invoke で押す（実入力を使わないので、
# 窓が後ろにあっても・ほかの担当が画面を使っていても押せる）。**閉じたことを確かめて**返す。
#
# 前は中心を実クリックし、閉じなければ Enter で閉じていた。Enter は既定のボタンを押すので、
# 「いいえ」を頼んだのに「はい」で閉じる・閉じた理由が分からない、になっていた（2026-09-30）。
# 今は、押せない・閉じないときは **「閉じられない:」で始まる文を返し、警告も出す**（Enter では閉じない）。
# 戻りが「閉じた:」で始まるかを呼ぶ側で見る。-UserWasTold は前の呼び方のために受けるだけ（実入力を使わないので要らない）。
# Invoke を持たない部品を押すときだけ -RealClick（実入力。こちらは -UserWasTold が要る）
function Close-ChmonosDialog {
  param([string]$Button = 'OK', [string]$Like = '*', [switch]$UserWasTold, [double]$TimeoutSeconds = 10, [switch]$RealClick)
  $dialog = Wait-ChmonosDialog -Like $Like -TimeoutSeconds $TimeoutSeconds
  if (-not $dialog) { return "小窓が出ていない（$Like）" }

  $handle = [IntPtr]$dialog.Current.NativeWindowHandle
  $title = $dialog.Current.Name
  $fail = { param($why) $m = "閉じられない: 「$title」の「$Button」（$why）"; Write-Warning $m; $m }
  # ボタンを名前で探す（知らせの窓は本文も名前に出すので、本文の中の「はい」などを掴まないように型で絞る）
  $isButton = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $asButton = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A_::NameProperty, $Button)), $isButton)
  $target = $dialog.FindFirst($TS_::Descendants, $asButton)
  if (-not $target) {
    # 題の帯の「閉じる」などを除いて、押せるボタンの名前を並べる（名前の打ち間違いにすぐ気付けるように）
    $names = @($dialog.FindAll($TS_::Descendants, $isButton) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join '・'
    return (& $fail "そのボタンが無い。在るボタン: $names")
  }

  for ($try = 1; $try -le 2; $try++) {
    try {
      if ($RealClick) {
        if (-not $UserWasTold) { throw '実入力の前にユーザへ告げる（-UserWasTold）' }
        $c = Get-ChmonosCenter $target
        $r = Invoke-ChmonosRealClick -X $c.X -Y $c.Y -UserWasTold
        if ($r -like '実入力をやめた*') { return (& $fail $r) }
      }
      else {
        if (-not $target.Current.IsEnabled) { return (& $fail '押せない状態（無効）') }
        $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      }
    }
    catch [System.Windows.Automation.ElementNotAvailableException] { }   # 1回目で閉じていて、部品がもう無い
    catch { return (& $fail $_.Exception.Message) }
    if (Wait-ChmonosCondition -TimeoutSeconds 4 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($handle) }) {
      return "閉じた: 「$title」の「$Button」（$try 回目）"
    }
  }

  & $fail '押したが閉じない'
}

# ---- 確かめ用の足跡（Start-ChmonosApp が付ける。出した窓の文言・押されたボタン・命令・Unity） ----

# 足跡を読む。-Kind で種類を絞る（知らせ・選ぶ・命令・Unity）、-Last で末尾だけ。
# 「この文言が出たか」は、撮って読むより速くて確かに分かる
#
# 足跡は写しごとのファイルに書かれ、次に同じ写しで起動するときに控え（%TEMP%\chmonos-uitrace-history）へ移る。
# -Saved で控えた足跡を読む（Save-ChmonosTrace の戻りのパスか、付けた名前。Get-ChmonosTraceHistory で一覧）
function Get-ChmonosTrace {
  param([string]$Kind = '*', [string]$Like = '*', [int]$Last = 40, [string]$Saved, [string]$Store)
  if ($Saved) {
    $file = if (Test-Path -LiteralPath $Saved) { $Saved } else {
      # 名前で指したときは、その名前で終わる控えのうち新しい物
      Get-ChildItem -LiteralPath $ChmonosTraceHistory -Filter "*-$Saved.log" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $file) { return "控えた足跡が無い: $Saved" }
  }
  else {
    # 相手が決まれば、その写しの足跡を読む
    try { [void](Resolve-ChmonosTarget $Store) }
    catch {
      $want = if ($Store) { $Store } elseif ($global:ChmonosCurrentStore) { $global:ChmonosCurrentStore } elseif ($env:CHMONOS_UI_STORE) { $env:CHMONOS_UI_STORE } else { $null }
      if ($want) {
        # 指した写しのアプリはもう閉じている。足跡は次の起動まで残っているので、それを読む（閉じた後に「何が出たか」を読む）
        $global:ChmonosTraceFile = Join-Path $ChmonosStateDir "$(Get-ChmonosStoreKey (Resolve-ChmonosStore $want)).uitrace.log"
      }
      elseif (@(Get-ChmonosRunning).Count -gt 1) {
        # 前は、黙って前の版の足跡の置き場（何時間も前の物）を読んでいた（2026-09-30）
        Write-Warning "足跡を読まなかった：$($_.Exception.Message)"
        return "足跡が無い（$($_.Exception.Message)）"
      }
      elseif ($global:ChmonosTraceFile -eq (Join-Path $env:TEMP 'chmonos-uitrace.log')) {
        # アプリが1本も開いていず、このシェルでは相手も決めていない（新しいシェルで、閉じた後に読む）。いちばん新しい足跡を読む
        # （既定の置き場は前の版の道具の物で、今の道具は書かない）
        $latest = Get-ChildItem -LiteralPath $ChmonosStateDir -Filter *.uitrace.log -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) { $global:ChmonosTraceFile = $latest.FullName }
      }
    }
    $file = $global:ChmonosTraceFile
  }
  if (-not (Test-Path -LiteralPath $file)) { return '足跡が無い（-NoTrace で起動した？）' }
  Get-Content -LiteralPath $file | Where-Object {
    $parts = $_ -split "`t", 3
    $parts.Count -ge 3 -and $parts[1] -like $Kind -and $parts[2] -like $Like
  } | Select-Object -Last $Last
}

# 今の足跡を控える（写す。アプリは開いたままでよい）。回ごとの足跡を後で比べたいとき・閉じる前に取っておきたいときに呼ぶ。
# -Name を付けると、後で Get-ChmonosTrace -Saved <名前> で読める。戻りは控えのパス
function Save-ChmonosTrace {
  param([string]$Name, [string]$Store)
  try { $target = Resolve-ChmonosTarget $Store; $key = $target.Key } catch { $key = 'app' }
  $file = $global:ChmonosTraceFile
  if (-not (Test-Path -LiteralPath $file)) { throw '足跡が無い（-NoTrace で起動した？）' }
  if ($Name -and $Name -notmatch '^[\w\-.]+$') { throw "名前に使えない字がある: $Name（英数字・_・-・. だけ）" }
  [IO.Directory]::CreateDirectory($ChmonosTraceHistory) | Out-Null
  $dest = Join-Path $ChmonosTraceHistory ("{0}-{1}{2}.log" -f (Get-Date).ToString('yyyyMMdd-HHmmss'), $key, $(if ($Name) { "-$Name" } else { '' }))
  Copy-Item -LiteralPath $file -Destination $dest -Force
  $dest
}

# 控えた足跡の一覧（新しい順）
function Get-ChmonosTraceHistory {
  if (-not (Test-Path -LiteralPath $ChmonosTraceHistory)) { return }
  Get-ChildItem -LiteralPath $ChmonosTraceHistory -Filter *.log -File | Sort-Object LastWriteTime -Descending |
    ForEach-Object { [pscustomobject]@{ Name = $_.BaseName; At = $_.LastWriteTime.ToString('MM-dd HH:mm:ss'); KB = [math]::Round($_.Length / 1KB, 1); Path = $_.FullName } }
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
