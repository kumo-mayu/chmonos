# 編集の画面で、上の並び（キュー）の札を押して別の商品へ移ってから、画面の色が初めて変わるまでを、画面を連続で撮って測る。版ごとに起動し直す。
# first-frame.ps1（検索のカードから商品ページを開く）の編集の画面版。押すのは並びの1枚目と2枚目を交互に（保存しない移り方）。
param([string]$Exe, [string]$Label, [string]$Store = 'editperf', [int]$Rounds = 6, [int]$IntervalMs = 10)
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-ops.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
Stop-ChmonosApp -ErrorAction SilentlyContinue | Out-Null
Start-ChmonosApp -Store $Store -Exe $Exe | Out-Null
Use-ChmonosStore $Store | Out-Null
Wait-ChmonosResponsive 90 | Out-Null; Start-Sleep 4
[void](Invoke-ChmonosByName -Name '編集' -Type Button -WaitSeconds 5)
Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 2
Initialize-ChmonosFramesType
Show-ChmonosFront | Out-Null
$target = Resolve-ChmonosTarget $Store
$results = @()
# 1回目は温まっていないので数えない（両方の商品を一度開いておく）
for ($r = 0; $r -le $Rounds * 2; $r++) {
  $tiles = @(Get-ChmonosById -Id EditQueueTile)
  $tile = $tiles[$r % 2]
  Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep -Milliseconds 1500
  $cap = [ChmonosFrames]::new($ChmonosProcessName, [int[]]@($target.Pid), $false, $IntervalMs, 24)
  $cap.Start()
  Start-Sleep -Milliseconds 200
  $origin = $cap.Now()
  $tile.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  while (($cap.Now() - $origin) -lt 1500) { Start-Sleep -Milliseconds 50 }
  $cap.Stop()
  $fr = @($cap.Frames.ToArray() | Group-Object Hwnd | Sort-Object { $_.Group[0].W * $_.Group[0].H } -Descending | Select-Object -First 1 -ExpandProperty Group)
  $base = $fr | Where-Object { $_.Ms -le $origin } | Select-Object -Last 1
  $first = $fr | Where-Object { $_.Ms -gt $origin -and ([Math]::Abs($_.R - $base.R) + [Math]::Abs($_.G - $base.G) + [Math]::Abs($_.B - $base.B)) -gt 3 } | Select-Object -First 1
  $ms = if ($first) { [int]($first.Ms - $origin) } else { $null }
  if ($r -ge 2) { $results += $ms; "$Label round$r 札$($r % 2 + 1) 最初に変わったms=$ms（コマ $($fr.Count)）" }
}
"$Label 一覧: $($results -join ',')"
Stop-ChmonosApp | Out-Null
