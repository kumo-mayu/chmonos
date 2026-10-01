# 起動して落ち着いた後の、1回目の検索・1回目の商品ページ・メモリを、版を交互に測る（docs/research/startup-dotnet10-2026-10-01.md「.NET 10 で事前翻訳を測った」）。
# -Variants は 'ラベル=実行ファイル' の並び。写しは台本 many（商品 90000001 がある）を想定。
# 起動の画面はもう検索なので「1回目の検索」は最初の絞り込み（id: を入れてから件数の文が替わるまで。入力の待ち 200ms を含む）。
# 1回目の商品ページは、カードを押してから最後の 33ms 超の止まりが終わるまで（perf-kit の Measure-ChmonosStep）
param([Parameter(Mandatory)][string[]]$Variants, [int]$Rounds = 10, [Parameter(Mandatory)][string]$Out, [string]$Store = 'bootA', [string]$ItemId = '90000001')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. "$repo\.claude\skills\ui-check\scripts\ui-kit.ps1"
. "$repo\.claude\skills\ui-check\scripts\ui-ops.ps1"
. "$repo\.claude\skills\perf-measure\scripts\perf-kit.ps1"
for ($r = 1; $r -le $Rounds; $r++) {
  foreach ($vv in $Variants) {
    $v, $exe = $vv -split '=', 2
    # 速さの数字は、ほかのアプリが動いていると動く。重なったら測らずに止める
    if (@(Get-Process Chmonos.App -ErrorAction SilentlyContinue).Count) { "ほかのアプリが開いている。止める"; return }
    [void](Start-ChmonosApp -Store $Store -Exe $exe -NoTrace -IsolateTemp -SettleSeconds 3)
    Use-ChmonosStore $Store
    $p = Get-ChmonosApp
    # 起動の読み込み・裏の最適化し直しが済んでから測る（1回目の操作だけを見たい）
    while (((Get-Date) - $p.StartTime).TotalSeconds -lt 15) { Start-Sleep -Milliseconds 200 }
    $idle = Get-ChmonosMem
    # 探すのは計測の外で（木をなめる間は画面のスレッドが止まる）
    $box = Get-ChmonosById -Id QueryBox
    $sumEl = Get-ChmonosById -Id SearchResultSummary
    $before = $sumEl.Current.Name; $st = @{ ms = -1 }
    $s1 = Measure-ChmonosStep -Label "$v#$r 検索" -Action {
      $sw = [Diagnostics.Stopwatch]::StartNew()
      $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("id:$ItemId")
      while ($sw.ElapsedMilliseconds -lt 10000) { if ($sumEl.Current.Name -ne $before) { $st.ms = $sw.ElapsedMilliseconds; break }; Start-Sleep -Milliseconds 10 } }
    $card = Wait-ChmonosCondition -TimeoutSeconds 5 -PollMs 300 -Until { @(Get-ChmonosItemCards) | Select-Object -First 1 }
    Start-Sleep -Seconds 2
    $s2 = Measure-ChmonosStep -Label "$v#$r 商品" -Action { Invoke-ChmonosElement $card }
    $ok = [bool](Wait-ChmonosById -Id ItemEdit -TimeoutSeconds 5)
    Start-Sleep -Seconds 3
    $after = Get-ChmonosMem
    # 欄の名前に item を使うと、PowerShell の .item（配列の添字の関数）とぶつかって集計で読めない
    $rec = [ordered]@{ v = $v; r = $r; idleWs = $idle.WsMB; idlePriv = $idle.PrivMB
      search = $st.ms; searchSettle = $s1.'落ち着くまでms'; searchFreeze = $s1.'固まり合計ms'; searchMax = $s1.'最長ms'
      page = $s2.'落ち着くまでms'; pageFreeze = $s2.'固まり合計ms'; pageMax = $s2.'最長ms'; pageOk = $ok; afterWs = $after.WsMB; afterPriv = $after.PrivMB }
    ($rec | ConvertTo-Json -Compress) | Add-Content -Encoding utf8 $Out
    "$v#$r 15秒 $($idle.WsMB)/$($idle.PrivMB)MB 検索 $($st.ms) 商品 $($s2.'落ち着くまでms') ok=$ok"
    [void](Stop-ChmonosApp -Store $Store)
    Start-Sleep -Seconds 2
  }
}
