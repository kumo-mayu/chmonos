# 商品ページ（か編集画面）を開く間だけ dotnet-trace で画面のスレッドのスタックを取り、speedscope の JSON にする
param([string]$Exe, [string]$Store, [string]$Id, [string]$Other, [string]$Out, [int]$Rounds = 3, [switch]$Edit)
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-ops.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
Stop-ChmonosApp -ErrorAction SilentlyContinue | Out-Null
Start-ChmonosApp -Store $Store -Exe $Exe | Out-Null
Use-ChmonosStore $Store
Wait-ChmonosResponsive 90 | Out-Null
Start-Sleep -Seconds 4
# 1回目は JIT が乗るので、測る前に1度開いておく
Open-ChmonosItem -Id $Id | Out-Null; Wait-ChmonosResponsive 30 | Out-Null
if ($Edit) { Invoke-ChmonosById -Id ItemEdit | Out-Null; Start-Sleep 3 }
Open-ChmonosItem -Id $Other | Out-Null; Wait-ChmonosResponsive 30 | Out-Null
$app = Get-ChmonosApp
$trace = "$Out.nettrace"
Remove-Item $trace, "$Out.speedscope.json" -ErrorAction SilentlyContinue
$dur = [TimeSpan]::FromSeconds(6 + 7 * $Rounds).ToString('hh\:mm\:ss')
$p = Start-Process dotnet-trace -ArgumentList @('collect', '-p', $app.Id, '--profile', 'dotnet-sampled-thread-time', '--duration', $dur, '-o', $trace) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4
for ($r = 1; $r -le $Rounds; $r++) {
  Open-ChmonosItem -Id $Other | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1
  if ($Edit) {
    Open-ChmonosItem -Id $Id | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1
    Invoke-ChmonosById -Id ItemEdit | Out-Null
  } else {
    Open-ChmonosItem -Id $Id | Out-Null
  }
  Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 2
}
$p.WaitForExit(120000) | Out-Null
dotnet-trace convert $trace --format Speedscope -o "$Out" | Out-Null
Stop-ChmonosApp | Out-Null
Get-Item "$Out.speedscope.json" | ForEach-Object { "$($_.FullName) $([int]($_.Length/1KB)) KB" }
