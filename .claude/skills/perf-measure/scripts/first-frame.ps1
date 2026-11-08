# 検索のカードを押してから、画面の色が初めて変わる（商品ページが描かれる）までを、画面を連続で撮って測る。版ごとに起動し直す。
# dotnet-trace の記録からは「最初の絵」を切り出しにくい（後から作る仕事が、いちばん長い仕事として数えられる）ので、こちらで見る
param([string]$Exe, [string]$Label, [string]$Id, [string]$Other, [string]$Store = 'friendcheck', [int]$Rounds = 6, [int]$IntervalMs = 10)
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-ops.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
Stop-ChmonosApp -ErrorAction SilentlyContinue | Out-Null
Start-ChmonosApp -Store $Store -Exe $Exe | Out-Null
Use-ChmonosStore $Store | Out-Null
Wait-ChmonosResponsive 90 | Out-Null; Start-Sleep 4
Open-ChmonosItem -Id $Id | Out-Null; Wait-ChmonosResponsive 30 | Out-Null
Open-ChmonosItem -Id $Other | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1
Initialize-ChmonosFramesType
Show-ChmonosFront | Out-Null
$target = Resolve-ChmonosTarget $Store
$results = @()
for ($r = 1; $r -le $Rounds; $r++) {
  # 検索へ戻り、目当ての商品のカードだけを出しておく（測る外で）
  [void](Invoke-ChmonosByName -Name '検索' -Type Button -WaitSeconds 0)
  $box = Wait-ChmonosById -Id QueryBox -TimeoutSeconds 10
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("id:$Id")
  $want = Get-ChmonosItemName -Id $Id
  $card = Wait-ChmonosCondition -TimeoutSeconds 6 -PollMs 300 -Until { @(Get-ChmonosItemCards) | Where-Object { $_.Current.Name -eq $want } | Select-Object -First 1 }
  Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep -Milliseconds 1500
  $cap = [ChmonosFrames]::new($ChmonosProcessName, [int[]]@($target.Pid), $false, $IntervalMs, 24)
  $cap.Start()
  Start-Sleep -Milliseconds 200
  $origin = $cap.Now()
  $card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  while (($cap.Now() - $origin) -lt 1500) { Start-Sleep -Milliseconds 50 }
  $cap.Stop()
  $fr = @($cap.Frames.ToArray() | Group-Object Hwnd | Sort-Object { $_.Group[0].W * $_.Group[0].H } -Descending | Select-Object -First 1 -ExpandProperty Group)
  $base = $fr | Where-Object { $_.Ms -le $origin } | Select-Object -Last 1
  $first = $fr | Where-Object { $_.Ms -gt $origin -and ([Math]::Abs($_.R - $base.R) + [Math]::Abs($_.G - $base.G) + [Math]::Abs($_.B - $base.B)) -gt 3 } | Select-Object -First 1
  $ms = if ($first) { [int]($first.Ms - $origin) } else { $null }
  $results += $ms
  "$Label round$r 最初に変わったms=$ms（コマ $($fr.Count)）"
  Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1
}
"$Label 一覧: $($results -join ',')"
