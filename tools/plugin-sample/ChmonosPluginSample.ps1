#requires -Version 7
<#
  Chmonos のプラグインのいちばん小さな見本（plan 44・docs/spec/plugins.md・plugins-menu.md）。

  動かすと：
    1. 動いている Chmonos を探す（%LOCALAPPDATA%\Chmonos.Shared\running\*.json。2つ以上なら -Store で保存先を選ぶ）
    2. 初めてなら許可を求める（Chmonos に「APIの利用の許可」の窓が出る）。受け取ったトークンは
       %LOCALAPPDATA%\ChmonosPluginSample\tokens.json に保存先ごとに控え、次からは聞かない
    3. 商品の右クリックと複数選択の帯の「ほかのソフト ▸ Chmonos の見本」に2つの項目を足す
       （初めてと、項目を変えたときは「メニューの項目の許可」の窓が出る）
    4. 押されるのを待ち、押されたら次をして、結果を Chmonos の下の帯に返す
       - 「商品名をコピー」：選んだ商品の名前を1行ずつクリップボードへ
       - 「IDを表示」：商品 ID をこの窓に出す
  止めるときは Ctrl+C。止めて60秒たつと、Chmonos の項目は灰色になる（「応答していません」）。

  使い方： pwsh -File ChmonosPluginSample.ps1 [-Store <保存先のフォルダ>]
#>
param([string]$Store)

$ErrorActionPreference = 'Stop'
# 出力をファイルへ向けたとき、既定の文字コード（日本語の Windows では Shift_JIS）で書かれて化けたので UTF-8 に決める
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$Name = 'Chmonos の見本'
$TokenFile = Join-Path $env:LOCALAPPDATA 'ChmonosPluginSample\tokens.json'

# 宣言する項目。JSON は文字列で書く（ConvertTo-Json は空の配列を null にし、要素が1つの配列をほどくので）
$Menu = @'
{ "items": [
  { "id": "copy-names", "label": "商品名をコピー", "where": ["item", "selection"], "needs": ["name"] },
  { "id": "show-ids", "label": "IDを表示", "where": ["item", "selection"] }
]}
'@

function Find-Chmonos {
    $dir = Join-Path $env:LOCALAPPDATA 'Chmonos.Shared\running'
    # 落ちた Chmonos の札が残ることがあるので、プロセスが動いていて始まった時刻が合う物だけ使う
    $alive = Get-ChildItem (Join-Path $dir '*.json') -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json } |
        Where-Object {
            $p = Get-Process -Id $_.processId -ErrorAction SilentlyContinue
            $p -and $_.port -and [math]::Abs(($p.StartTime - ([datetimeoffset]$_.startedAt).LocalDateTime).TotalSeconds) -le 1
        }
    if ($Store) { $alive = $alive | Where-Object { $_.storeRoot -eq $Store } }
    $list = @($alive)
    if ($list.Count -gt 1) { throw "Chmonos が $($list.Count) つ動いています。-Store で保存先を選んでください：$($list.storeRoot -join ' / ')" }
    if ($list.Count -eq 0) { return $null }
    return $list[0]
}

function Send-Json([string]$Uri, [string]$Method, [hashtable]$Headers, [string]$Json) {
    Invoke-RestMethod $Uri -Method $Method -Headers $Headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($Json))
}

function Read-Tokens {
    if (Test-Path $TokenFile) { return Get-Content $TokenFile -Raw | ConvertFrom-Json -AsHashtable }
    return @{}
}

function Get-Token($Base, $StoreRoot, [switch]$Fresh) {
    $tokens = Read-Tokens
    if (-not $Fresh -and $tokens[$StoreRoot]) { return $tokens[$StoreRoot] }

    Write-Host 'Chmonos に許可を求めています。Chmonos の窓で［許可］を押してください。'
    $ask = Send-Json "$Base/v1/clients/request" Post @{} ('{"name":"' + $Name + '","scopes":[]}')
    do {
        $result = Send-Json "$Base/v1/clients/request/result" Post @{} ('{"requestId":"' + $ask.requestId + '","wait":25}')
    } while ($result.state -eq 'pending')
    if ($result.state -ne 'approved') { throw "許可されませんでした（$($result.state)）。" }

    $tokens[$StoreRoot] = $result.token
    New-Item -ItemType Directory -Force (Split-Path $TokenFile) | Out-Null
    $tokens | ConvertTo-Json | Set-Content $TokenFile -Encoding utf8
    return $result.token
}

function Invoke-Item($Event) {
    $items = @($Event.menu.items)
    switch ($Event.menu.id) {
        'copy-names' {
            Set-Clipboard -Value ($items | ForEach-Object { $_.name })
            return "$($items.Count) 件の商品名をコピーしました"
        }
        'show-ids' {
            $ids = ($items | ForEach-Object { $_.itemId }) -join ', '
            Write-Host "IDを表示：$ids"
            return "$($items.Count) 件の ID をツールの窓に出しました"
        }
        default { return "知らない項目です：$($Event.menu.id)" }
    }
}

while ($true) {
    $chmonos = Find-Chmonos
    if (-not $chmonos) {
        Write-Host 'Chmonos が見つかりません（起動して、設定の「APIを使う」をオンにしてください）。10秒後に探し直します。'
        Start-Sleep -Seconds 10
        continue
    }

    $base = "http://127.0.0.1:$($chmonos.port)"
    $token = Get-Token $base $chmonos.storeRoot
    $headers = @{ Authorization = "Bearer $token" }
    try {
        $declared = Send-Json "$base/v1/plugin/menu" Put $headers $Menu
        if ($declared.state -eq 'pending') { Write-Host 'Chmonos の「メニューの項目の許可」の窓で［許可］を押してください。' }
        Write-Host "つながりました：$($chmonos.storeRoot)（ポート $($chmonos.port)）。Ctrl+C で止めます。"

        $after = 0; $epoch = ''
        while ($true) {
            $answer = Invoke-RestMethod "$base/v1/plugin/events?after=$after&wait=30&epoch=$epoch" -Headers $headers
            # Chmonos が起動し直した：通番が数え直されたので、項目を宣言し直す（同じ内容なら聞かれない）
            if ($answer.reset -and $epoch) { break }
            foreach ($event in @($answer.events | Where-Object kind -eq 'menu')) {
                $ok = $true
                try { $message = Invoke-Item $event } catch { $ok = $false; $message = "失敗しました：$($_.Exception.Message)" }
                $result = @{ ok = $ok; message = $message; epoch = $answer.epoch } | ConvertTo-Json -Compress
                Send-Json "$base/v1/plugin/events/$($event.seq)/result" Post $headers $result | Out-Null
                Write-Host "$($event.menu.id)：$message"
            }
            $after = $answer.last; $epoch = $answer.epoch
        }
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 401) {
            # 設定で許可を取り消された：控えたトークンを捨てて求め直す
            Write-Host '許可が取り消されていました。もう一度求めます。'
            $null = Get-Token $base $chmonos.storeRoot -Fresh
        }
        else {
            # Chmonos が閉じた・API が止まった：探し直す
            Write-Host "Chmonos とつながりませんでした（$($_.Exception.Message)）。5秒後に探し直します。"
            Start-Sleep -Seconds 5
        }
    }
}
