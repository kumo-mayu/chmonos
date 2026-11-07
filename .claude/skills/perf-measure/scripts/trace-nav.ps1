# 開いているアプリで、検索と各画面を行き来する間を dotnet-trace で取る。画面ごとに、切り替えた時刻（記録の始まりからの ms）を書き出す
param([string]$Store, [string[]]$Screens, [string]$Out, [int]$Rounds = 2)
. "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "D:\work\ClaudeCode\chmonos\.claude\skills\perf-measure\scripts\perf-kit.ps1"
Use-ChmonosStore $Store
$app = Get-ChmonosApp
# 1巡目は JIT と最初の読み込みが乗るので、記録の前に1度ずつ開いておく
foreach ($screen in $Screens) { Invoke-ChmonosById -Id "Nav.$screen" | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1 }
Invoke-ChmonosById -Id Nav.Search | Out-Null; Wait-ChmonosResponsive 30 | Out-Null
$trace = "$Out.nettrace"
Remove-Item $trace, "$Out.speedscope.json" -ErrorAction SilentlyContinue
$seconds = 5 + $Screens.Count * $Rounds * 5
$p = Start-Process dotnet-trace -ArgumentList @('collect', '-p', $app.Id, '--profile', 'dotnet-sampled-thread-time', '--duration', [TimeSpan]::FromSeconds($seconds).ToString('hh\:mm\:ss'), '-o', $trace) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
$t0 = [DateTime]::Now
$marks = @()
for ($r = 1; $r -le $Rounds; $r++) {
  foreach ($screen in $Screens) {
    $marks += "$screen@$([int]([DateTime]::Now - $t0).TotalMilliseconds)"
    Invoke-ChmonosById -Id "Nav.$screen" | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 2
    Invoke-ChmonosById -Id Nav.Search | Out-Null; Wait-ChmonosResponsive 30 | Out-Null; Start-Sleep 1
  }
}
$p.WaitForExit(180000) | Out-Null
dotnet-trace convert $trace --format Speedscope -o "$Out" | Out-Null
"切り替えた時刻（記録を始めてから約3秒後が0）: $($marks -join '  ')"
