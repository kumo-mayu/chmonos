# 時々しか落ちない試験を、負荷を掛けて落ちるまで繰り返す（2026-11-13 に、揺れる試験2件の原因を追うために作った）
#
# 使い方:
#   pwsh -File tools/flaky-stress.ps1 -Project Chmonos.Core.Tests -Filter "FullyQualifiedName~JsonStoreTests" -Label f2 [-Rounds 50] [-Burn 16] [-Parallel 8] [-KeepGoing] [-Repo <リポジトリ>] [-Out <置き場>]
#   -Project  … 試験のプロジェクト（先に dotnet build しておく。ここでは --no-build で走らせる）
#   -Filter   … dotnet test の --filter
#   -Burn     … CPU を回し続けるプロセスの数（既定 16。裏の仕事・Task.Run を遅らせ、負荷の時だけ入れ替わる順序を起こす）
#   -Parallel … 同じ試験を同時に走らせるプロセスの数（既定 1。ディスクと CPU の取り合い）
#   -KeepGoing… 落ちても止めずに数える（既定は最初に落ちた回で止める）
#   -Repo     … 走らせる所（既定はこのリポジトリ。直す前の版は git worktree で別に組んで渡す）
#   -Out      … 記録の置き場（既定 %TEMP%\chmonos-flaky）。<Label>.log に回ごとの結果、落ちた回の出力は <Label>-fail-<回>-<本>.txt
#
# 例（2026-11-13）：直す前の版の Unity の確かめの試験は -Burn 16 -Parallel 4 で1回目に落ち、直した後は 40 回通った。
# 置き換えの試験は JsonStoreTests の一式を -Parallel 8 で 25 回目に落とせた（1件だけでは 100 回落ちなかった）。
# 長く回す前に、見立てを直接ためす小さな実験を書く方が早いことが多い（docs/dev/app-tests.md「時々落ちる試験を追う」）。
#
# 負荷のプロセスは終わりに止める（Ctrl+C で止めたときも finally で止める）。ほかのセッションが速さを測っている間は使わない
param(
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][string]$Label,
    [int]$Rounds = 50, [int]$Burn = 16, [int]$Parallel = 1, [switch]$KeepGoing,
    [string]$Repo = (Split-Path $PSScriptRoot),
    [string]$Out = (Join-Path $env:TEMP 'chmonos-flaky'))

New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $Out "$Label.log"
"始め $(Get-Date -Format 'HH:mm:ss') 回 $Rounds・負荷 $Burn・同時 $Parallel・$Project・$Filter・$Repo" | Set-Content $log
$burners = @(1..$Burn | Where-Object { $Burn -gt 0 } | ForEach-Object {
        Start-Process pwsh -ArgumentList '-NoProfile', '-Command', 'while ($true) { [math]::Sqrt(2) | Out-Null }' -WindowStyle Hidden -PassThru })
try {
    for ($r = 1; $r -le $Rounds; $r++) {
        $jobs = 1..$Parallel | ForEach-Object {
            Start-Job -ArgumentList $Repo, $Project, $Filter -ScriptBlock {
                param($repo, $project, $filter)
                Set-Location $repo
                # ジョブの出力は既定の符号で化け、合格の数が読めなかった
                [Console]::OutputEncoding = [Text.Encoding]::UTF8
                $text = dotnet test $project --no-build --nologo --filter $filter 2>&1 | Out-String
                [pscustomobject]@{ Code = $LASTEXITCODE; Text = $text }
            }
        }
        $results = $jobs | Receive-Job -Wait -AutoRemoveJob
        $failed = @($results | Where-Object { $_.Code -ne 0 })
        $passed = ($results | ForEach-Object { if ($_.Text -match '合格:\s+(\d+)') { $Matches[1] } else { '?' } }) -join ','
        "$(Get-Date -Format 'HH:mm:ss') 回 $r：失敗 $($failed.Count)/$Parallel（合格 $passed）" | Add-Content $log
        if ($failed.Count) {
            $n = 0
            foreach ($f in $failed) { $n++; $f.Text | Set-Content (Join-Path $Out "$Label-fail-$r-$n.txt") }
            "落ちた：回 $r" | Add-Content $log
            if (-not $KeepGoing) { break }
        }
    }
}
finally {
    $burners | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    "終わり $(Get-Date -Format 'HH:mm:ss')" | Add-Content $log
    Get-Content $log | Select-Object -Last 3
}
