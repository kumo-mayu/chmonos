# 画面の速さ・固まり・メモリを測る道具（ui-kit.ps1 を読んだ後にドットで読み込む）：
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
#
# 前は作業用フォルダに置いていて、別の担当が同じ名前の自分の道具で上書きした（parallel-fix スキル）。
# 固まりを外から測る見張り（probe.exe）も担当ごとに作り直していたので、ここへ寄せた（2026-09-30）。
#
# 測り方は2つ：
#   1回の呼び出しの中で操作を1つ測る … Measure-ChmonosStep（下）
#   呼び出しをまたいで長く測る（取り込みの間・画面を行き来する間） … Start-ChmonosProbe（末尾）
#
# 固まり：窓へ WM_NULL を送り続け、返りが 33ms を超えた回数・合計・最長を数える（perf-measure の決まり）
# メモリ：作業セットとプライベートを 50ms ごとに見本取りし、最大と落ち着いた値を出す
if (-not ('ChmonosPinger' -as [type])) { Add-Type @"
using System; using System.Diagnostics; using System.Threading; using System.Collections.Generic; using System.Runtime.InteropServices;
public sealed class ChmonosPinger {
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
  readonly IntPtr _h; readonly int _pid; Thread _t; volatile bool _stop;
  public readonly List<double> Starts = new List<double>(); public readonly List<double> Durations = new List<double>();
  public long PeakWs, PeakPrivate; public readonly Stopwatch Clock = Stopwatch.StartNew();
  public ChmonosPinger(IntPtr h, int pid) { _h = h; _pid = pid; }
  public void Start() {
    _t = new Thread(() => {
      var p = Process.GetProcessById(_pid); var sample = Stopwatch.StartNew();
      while (!_stop) {
        var s = Clock.Elapsed.TotalMilliseconds; IntPtr r;
        SendMessageTimeout(_h, 0, IntPtr.Zero, IntPtr.Zero, 0, 30000, out r);
        var d = Clock.Elapsed.TotalMilliseconds - s;
        lock (Durations) { Starts.Add(s); Durations.Add(d); }
        if (sample.ElapsedMilliseconds >= 50) { sample.Restart(); p.Refresh(); PeakWs = Math.Max(PeakWs, p.WorkingSet64); PeakPrivate = Math.Max(PeakPrivate, p.PrivateMemorySize64); }
        Thread.Sleep(5);
      }
    }); _t.IsBackground = true; _t.Start();
  }
  public void Stop() { _stop = true; _t.Join(); }
  public double Now() { return Clock.Elapsed.TotalMilliseconds; }
  // t0 より後で、最後に 33ms を超えた返りが終わった時刻（それが無ければ最初の返り）
  public double SettledAfter(double t0) {
    double last = -1, first = -1;
    lock (Durations) { for (int i = 0; i < Starts.Count; i++) { var end = Starts[i] + Durations[i]; if (end < t0) continue; if (first < 0) first = end; if (Durations[i] > 33) last = end; } }
    return (last >= 0 ? last : first) - t0;
  }
  public double[] Freezes(double t0) {
    int n = 0; double sum = 0, max = 0;
    lock (Durations) { for (int i = 0; i < Starts.Count; i++) { if (Starts[i] + Durations[i] < t0) continue; var d = Durations[i]; if (d > 33) { n++; sum += d; } if (d > max) max = d; } }
    return new double[] { n, sum, max };
  }
}
"@ }

function Get-ChmonosMem {
  $p = Get-ChmonosApp; $p.Refresh()
  [pscustomobject]@{ WsMB = [int]($p.WorkingSet64 / 1MB); PrivMB = [int]($p.PrivateMemorySize64 / 1MB) }
}

# 操作を1つ測る。$Action は操作（UIA の Invoke など）。返ってから、1.5 秒続けて 33ms 超えが無くなるまで待つ
function Measure-ChmonosStep {
  param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][scriptblock]$Action, [int]$QuietMs = 1500, [int]$MaxSeconds = 60)
  $app = Get-ChmonosApp
  $pg = [ChmonosPinger]::new($app.MainWindowHandle, $app.Id)
  $pg.Start(); Start-Sleep -Milliseconds 200
  $t0 = $pg.Now()
  & $Action | Out-Null
  $deadline = [DateTime]::Now.AddSeconds($MaxSeconds)
  while ([DateTime]::Now -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $now = $pg.Now(); $settled = $pg.SettledAfter($t0)
    if ($settled -ge 0 -and ($now - $t0 - $settled) -ge $QuietMs) { break }
  }
  $pg.Stop()
  $f = $pg.Freezes($t0)
  $mem = Get-ChmonosMem
  [pscustomobject]@{
    場面 = $Label; 落ち着くまでms = [int]$pg.SettledAfter($t0); 固まり回数 = [int]$f[0]; 固まり合計ms = [int]$f[1]; 最長ms = [int]$f[2]
    WS最大MB = [int]($pg.PeakWs / 1MB); Priv最大MB = [int]($pg.PeakPrivate / 1MB); WS後MB = $mem.WsMB; Priv後MB = $mem.PrivMB
  }
}

# UIA で名前から探して押す（計測の外で探し、計測の中では押すだけにする）
function Find-ChmonosOne {
  param([string]$Name, [string]$Type = 'Button', [int]$Index = 0)
  $els = @(Get-ChmonosElements -Type $Type -Name $Name)
  if ($els.Count -le $Index) { throw "見つからない: $Type「$Name」" }
  $els[$Index]
}
function Invoke-ChmonosEl($El) {
  $pats = $El.GetSupportedPatterns()
  foreach ($p in $pats) {
    if ($p.ProgrammaticName -eq 'InvokePatternIdentifiers.Pattern') { $El.GetCurrentPattern($p).Invoke(); return }
    if ($p.ProgrammaticName -eq 'SelectionItemPatternIdentifiers.Pattern') { $El.GetCurrentPattern($p).Select(); return }
    if ($p.ProgrammaticName -eq 'TogglePatternIdentifiers.Pattern') { $El.GetCurrentPattern($p).Toggle(); return }
  }
  throw "押せない: $($El.Current.Name)"
}

# 2枚の撮った画像を画素で比べる。違う画素の数と、違う所を囲む四角を返す（-Region x,y,w,h で比べる範囲を絞る）
if (-not ('ChmonosDiff' -as [type])) { Add-Type @"
using System;
public static class ChmonosDiff {
  public static int[] Compare(byte[] a, int sa, byte[] b, int sb, int w, int h, int rx, int ry, int rw, int rh) {
    if (rw <= 0) { rx = 0; ry = 0; rw = w; rh = h; }
    int n = 0, minx = int.MaxValue, miny = int.MaxValue, maxx = -1, maxy = -1;
    for (int y = ry; y < Math.Min(h, ry + rh); y++) for (int x = rx; x < Math.Min(w, rx + rw); x++) {
      int ia = y * sa + x * 4, ib = y * sb + x * 4;
      if (a[ia] != b[ib] || a[ia+1] != b[ib+1] || a[ia+2] != b[ib+2]) { n++; if (x < minx) minx = x; if (y < miny) miny = y; if (x > maxx) maxx = x; if (y > maxy) maxy = y; }
    }
    return new int[] { n, minx, miny, maxx, maxy };
  }
}
"@ }
function Read-ChmonosPixels([string]$Path) {
  $bmp = [System.Drawing.Bitmap]::new($Path)
  $rect = [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
  $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $bytes = [byte[]]::new($data.Stride * $bmp.Height)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
  $info = [pscustomobject]@{ Bytes = $bytes; Stride = $data.Stride; W = $bmp.Width; H = $bmp.Height }
  $bmp.UnlockBits($data); $bmp.Dispose(); $info
}
function Compare-ChmonosShots {
  param([Parameter(Mandatory)][string]$A, [Parameter(Mandatory)][string]$B, [int[]]$Region = @(0,0,0,0))
  $pa = if (Test-Path $A) { $A } else { Join-Path $ChmonosShotDir "$A.png" }
  $pb = if (Test-Path $B) { $B } else { Join-Path $ChmonosShotDir "$B.png" }
  $x = Read-ChmonosPixels $pa; $y = Read-ChmonosPixels $pb
  $w = [Math]::Min($x.W, $y.W); $h = [Math]::Min($x.H, $y.H)
  $r = [ChmonosDiff]::Compare($x.Bytes, $x.Stride, $y.Bytes, $y.Stride, $w, $h, $Region[0], $Region[1], $Region[2], $Region[3])
  if ($r[0] -eq 0) { "同じ（$($x.W)x$($x.H) / $($y.W)x$($y.H)）" } else { "違う画素 $($r[0])：($($r[1]),$($r[2]))～($($r[3]),$($r[4]))（$($x.W)x$($x.H) / $($y.W)x$($y.H)）" }
}

# 画面のスレッドが返事をするまで待つ（固まっている間に撮らない）。続けて3回、50ms 以内に返ったら戻る
if (-not ('ChmonosPing' -as [type])) { Add-Type @"
using System; using System.Diagnostics; using System.Runtime.InteropServices;
public static class ChmonosPing {
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
  public static double Once(IntPtr h) { var sw = Stopwatch.StartNew(); IntPtr r; SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 0, 60000, out r); return sw.Elapsed.TotalMilliseconds; }
}
"@ }
function Wait-ChmonosResponsive([int]$TimeoutSeconds = 60) {
  $h = (Get-ChmonosApp).MainWindowHandle; $ok = 0; $end = [DateTime]::Now.AddSeconds($TimeoutSeconds)
  while ($ok -lt 3 -and [DateTime]::Now -lt $end) { if ([ChmonosPing]::Once($h) -lt 50) { $ok++ } else { $ok = 0 }; Start-Sleep -Milliseconds 100 }
}

# 違いの中身：最大の差（0〜255）と、違う画素のある行・列の範囲（字の縁の揺れか、物の位置のずれかを見分ける）
if (-not ('ChmonosDiff2' -as [type])) { Add-Type @"
using System; using System.Collections.Generic; using System.Linq;
public static class ChmonosDiff2 {
  public static string Describe(byte[] a, int sa, byte[] b, int sb, int w, int h) {
    int max = 0; var rows = new SortedDictionary<int,int>(); int big = 0;
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
      int ia = y * sa + x * 4, ib = y * sb + x * 4; int m = 0;
      for (int c = 0; c < 3; c++) m = Math.Max(m, Math.Abs(a[ia+c] - b[ib+c]));
      if (m > 0) { if (m > max) max = m; int n; rows.TryGetValue(y, out n); rows[y] = n + 1; if (m > 60) big++; }
    }
    var spans = new List<string>(); int start = -1, prev = -2;
    foreach (var y in rows.Keys) { if (y != prev + 1) { if (start >= 0) spans.Add(start == prev ? start.ToString() : start + "-" + prev); start = y; } prev = y; }
    if (start >= 0) spans.Add(start == prev ? start.ToString() : start + "-" + prev);
    return "最大の差 " + max + "・差が60を超える画素 " + big + "・行 " + string.Join(",", spans.Take(40));
  }
}
"@ }
function Show-ChmonosDiff([string]$A, [string]$B) {
  $pa = if (Test-Path $A) { $A } else { Join-Path $ChmonosShotDir "$A.png" }
  $pb = if (Test-Path $B) { $B } else { Join-Path $ChmonosShotDir "$B.png" }
  $x = Read-ChmonosPixels $pa; $y = Read-ChmonosPixels $pb
  [ChmonosDiff2]::Describe($x.Bytes, $x.Stride, $y.Bytes, $y.Stride, [Math]::Min($x.W, $y.W), [Math]::Min($x.H, $y.H))
}

# ---- 呼び出しをまたいで測る（見張り） ----
#
# シェルは呼び出しごとに新しくなるので、Measure-ChmonosStep は1回の呼び出しの中でしか測れない。
# 取り込みの間ずっと・画面を何周も行き来する間、のように長く測るときは、見張りを別のプロセスで走らせておき、
# 区切りに印（Add-ChmonosProbeMark）を置いて、後から区間ごとに固まりとメモリを出す。
#
#   Start-ChmonosProbe -Run r1                  # 見張りを始める
#   Add-ChmonosProbeMark -Run r1 -Label import-start
#   …操作…
#   Add-ChmonosProbeMark -Run r1 -Label import-end
#   Stop-ChmonosProbe -Run r1
#   Get-ChmonosProbeSummary -Run r1 -From import-start -To import-end
#
# 見張りは、アプリが通信した相手（TCP の相手先）も控える。確かめの間に BOOTH へ問い合わせたかを、後から見られる

$ChmonosProbeDir = Join-Path $env:TEMP 'chmonos-probe'
$ChmonosProbeScript = Join-Path $PSScriptRoot 'perf-probe.ps1'
$ChmonosProbeTimeFormat = 'yyyy-MM-dd HH:mm:ss.fff'

function Get-ChmonosProbeRunDir([string]$Run) {
  if ($Run -notmatch '^[\w\-.]+$') { throw "名前に使えない字がある: $Run（英数字・_・-・. だけ）" }
  Join-Path $ChmonosProbeDir $Run
}

# 見張りを始める。-ProcessId を省くと、今の相手のアプリ。同じ名前の前の記録は消す
function Start-ChmonosProbe {
  param([Parameter(Mandatory)][string]$Run, [int]$ProcessId, [string]$Store)
  if (-not $ProcessId) { $ProcessId = (Get-ChmonosApp -Store $Store).Id }
  $out = Get-ChmonosProbeRunDir $Run
  if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
  [IO.Directory]::CreateDirectory($out) | Out-Null
  $shell = (Get-Process -Id $PID).Path
  Start-Process -FilePath $shell -ArgumentList @('-NoProfile', '-File', "`"$ChmonosProbeScript`"", '-ProcessId', $ProcessId, '-OutDir', "`"$out`"") -WindowStyle Hidden | Out-Null
  # 見張りが動き出す（ready が置かれる）のを待つ。型を組むのに1〜2秒掛かる
  $end = [DateTime]::Now.AddSeconds(20)
  while (-not (Test-Path -LiteralPath (Join-Path $out 'ready')) -and [DateTime]::Now -lt $end) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path -LiteralPath (Join-Path $out 'ready'))) { throw "見張りが始まらない: $out（$(if (Test-Path -LiteralPath (Join-Path $out 'error.txt')) { Get-Content -LiteralPath (Join-Path $out 'error.txt') -Raw })）" }
  Add-ChmonosProbeMark -Run $Run -Label 'probe-start'
  "見張りを始めた: $out（pid=$ProcessId）"
}

# 区切りの印を置く。同じ名前を2回置くと、後の方が使われる
function Add-ChmonosProbeMark {
  param([Parameter(Mandatory)][string]$Run, [Parameter(Mandatory)][string]$Label)
  Add-Content -LiteralPath (Join-Path (Get-ChmonosProbeRunDir $Run) 'marks.csv') -Value ("{0},{1}" -f (Get-Date).ToString($ChmonosProbeTimeFormat), $Label)
}

# 見張りを止める（相手のアプリが閉じたときは、見張りの方で勝手に止まる）
function Stop-ChmonosProbe {
  param([Parameter(Mandatory)][string]$Run)
  $out = Get-ChmonosProbeRunDir $Run
  Add-ChmonosProbeMark -Run $Run -Label 'probe-stop'
  Set-Content -LiteralPath (Join-Path $out 'stop') -Value 'x'
  $end = [DateTime]::Now.AddSeconds(10)
  while (-not (Test-Path -LiteralPath (Join-Path $out 'done')) -and [DateTime]::Now -lt $end) { Start-Sleep -Milliseconds 100 }
  "見張りを止めた: $out"
}

function Get-ChmonosProbeMarks([string]$Run) {
  $marks = [ordered]@{}
  $file = Join-Path (Get-ChmonosProbeRunDir $Run) 'marks.csv'
  if (Test-Path -LiteralPath $file) { foreach ($l in Get-Content -LiteralPath $file) { $a = $l.Split(',', 2); if ($a.Count -eq 2) { $marks[$a[1]] = [datetime]::ParseExact($a[0], $ChmonosProbeTimeFormat, $null) } } }
  $marks
}

# 区間の固まりとメモリ。-From・-To は印の名前（省くと見張りの始めから終わりまで）
function Get-ChmonosProbeSummary {
  param([Parameter(Mandatory)][string]$Run, [string]$From = 'probe-start', [string]$To = 'probe-stop')
  $out = Get-ChmonosProbeRunDir $Run
  $marks = Get-ChmonosProbeMarks $Run
  if (-not $marks.Contains($From)) { throw "印が無い: $From（在る印: $($marks.Keys -join '・')）" }
  $f = $marks[$From]
  # 止める前に途中の数字を見るときは、今までを区間にする
  $t = if ($marks.Contains($To)) { $marks[$To] } elseif ($To -eq 'probe-stop') { Get-Date } else { throw "印が無い: $To（在る印: $($marks.Keys -join '・')）" }
  $inRange = { param($row) $x = [datetime]::ParseExact($row.time, $ChmonosProbeTimeFormat, $null); $x -ge $f -and $x -le $t }
  $stalls = @(Import-Csv -LiteralPath (Join-Path $out 'stalls.csv') | Where-Object { & $inRange $_ } | ForEach-Object { [double]$_.ms })
  $mem = @(Import-Csv -LiteralPath (Join-Path $out 'mem.csv') | Where-Object { & $inRange $_ })
  $ws = @($mem | ForEach-Object { [double]$_.ws_mb }); $pv = @($mem | ForEach-Object { [double]$_.priv_mb })
  $max = { param($v) if ($v.Count) { [int](($v | Measure-Object -Maximum).Maximum) } else { $null } }
  [pscustomobject]@{
    区間 = "$From → $To"; 秒 = [math]::Round(($t - $f).TotalSeconds, 1)
    固まり回数 = $stalls.Count; 固まり合計ms = if ($stalls.Count) { [int](($stalls | Measure-Object -Sum).Sum) } else { 0 }; 最長ms = if ($stalls.Count) { & $max $stalls } else { 0 }
    WS最大MB = & $max $ws; Priv最大MB = & $max $pv
    WS終わりMB = if ($mem.Count) { [int][double]$mem[-1].ws_mb } else { $null }; Priv終わりMB = if ($mem.Count) { [int][double]$mem[-1].priv_mb } else { $null }
  }
}

# 長い固まりの一覧（いつ・何ms）。どの操作の直後かを印と照らして見る
function Get-ChmonosProbeStalls {
  param([Parameter(Mandatory)][string]$Run, [int]$MinMs = 200)
  Import-Csv -LiteralPath (Join-Path (Get-ChmonosProbeRunDir $Run) 'stalls.csv') | Where-Object { [double]$_.ms -ge $MinMs }
}

# 見張っている間にアプリが通信した相手（新しく見えた接続だけ）。BOOTH へ問い合わせたかを見る。
# 相手は番地と口の番号で出る（名前ではない）。1件も無ければ、通信していない
function Get-ChmonosProbeConnections {
  param([Parameter(Mandatory)][string]$Run)
  Import-Csv -LiteralPath (Join-Path (Get-ChmonosProbeRunDir $Run) 'tcp.csv')
}