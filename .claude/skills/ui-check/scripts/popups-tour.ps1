# 起動したアプリで、台（ViewShot）で描けない物を画面ごとに通しで撮る：吹き出し・プルダウンの開いた一覧・メニュー・右クリック・窓の枠（2026-11-14。plan 17）。
#
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
#   Save-ProductionBaseline; Start-ChmonosApp -Store tour-dense-ui; Use-ChmonosStore tour-dense-ui; Lock-ChmonosScreen -Minutes 40
#   & "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\popups-tour.ps1" -Theme dark      # 写しの今の色で（-Theme は置き場の名前に使うだけ）
#   Stop-ChmonosApp; Set-ChmonosTheme -Store tour-dense-ui -Theme light; Start-ChmonosApp -Store tour-dense-ui
#   & "...\popups-tour.ps1" -Theme light -Only '01-search','03-item'                                  # 画面を絞る
#   Stop-ChmonosApp; Unlock-ChmonosScreen; Test-ProductionUntouched
#
# 置き場は %TEMP%\chmonos-shots\<-Out>\（tips-<色>\・menus-<色>\・window-<色>\・tips-<色>.tsv・index.html）。
# 吹き出しは「見えている部品のうち、吹き出しの文を持つ物」に順にマウスを乗せて撮る。同じ文は1回だけ
# （tsv に書いた文は次の回も飛ばすので、途中から流し直せる。撮り直すなら tsv と画像のフォルダを消す）。
# 流した先の部品・その状態でしか出ない吹き出しは撮れない。全部の文は node tools/wording.mjs --list
# （吹き出しは 569 か所・496 通り。2026-11-14 の回で撮れたのは 13 画面で約 195 通り）。
#
# **マウスが動く（実入力）。使う前にユーザへ告げる。画面を使う（並行で使えない）。**
# BOOTH へ問い合わせる画面は開かない：ショップ・ショップの一覧（開くと足りないアイコンを取りに行く）。未確定の画面は乗せるだけで押さない。
# 写しは架空のデータの物だけを使う（撮った画像に商品名が写る）。tour-dense-ui は tour-dense の写し（New-ChmonosSandbox -From tour-dense -Name tour-dense-ui）。
# フォルダの画面・未確定の画面には手元のパス（ユーザ名を含む）が写るので、人に渡す前に見る
param([string]$Theme = 'light', [string]$Out = ('popups-' + (Get-Date -Format 'yyyyMMdd')), [string[]]$Only, [string]$Store = 'tour-dense-ui')
. (Join-Path $PSScriptRoot 'ui-kit.ps1')
. (Join-Path $PSScriptRoot 'ui-ops.ps1')
. (Join-Path $PSScriptRoot 'ui-popups.ps1')
Use-ChmonosStore $Store | Out-Null
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$tipCond = New-Object System.Windows.Automation.PropertyCondition $AE::ControlTypeProperty, ([System.Windows.Automation.ControlType]::ToolTip)
$dir = Join-Path $ChmonosShotDir $Out
New-Item -ItemType Directory -Force $dir | Out-Null
$log = Join-Path $dir "tips-$Theme.tsv"
if (-not (Test-Path $log)) { "画面`t出た`tID`t名前`t吹き出しの文`t画像" | Set-Content $log -Encoding utf8 }
$seen = @{}
Get-Content $log -Encoding utf8 | Select-Object -Skip 1 | ForEach-Object { $seen[($_ -split "`t")[4]] = 1 }

# 画面の座標の四角を切り取る（吹き出しの周りだけ）
function Save-Rect($name, [int]$x, [int]$y, [int]$w, [int]$h) {
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $x = [Math]::Max($vs.Left, $x); $y = [Math]::Max($vs.Top, $y)
  $w = [Math]::Min($vs.Right - $x, $w); $h = [Math]::Min($vs.Bottom - $y, $h)
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h)); $g.Dispose()
  $p = Join-Path $dir "$name.png"; New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null
  $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $p
}

function Find-Tip {
  # 吹き出しは別の窓に描かれるが、UI Automation では主の窓の下に出る（デスクトップの直下には無かった。2026-11-14 に確かめた）
  (Get-ChmonosRoot).FindFirst($TS::Descendants, $tipCond)
}

function Shoot-Tips($screen) {
  $win = Get-ChmonosRoot
  $wr = $win.Current.BoundingRectangle
  # 吹き出しの文は UI Automation の HelpText に出る（文字の吹き出しのとき。WPF の既定）
  $els = @($win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
      $c = $_.Current
      $c.HelpText -and -not $c.IsOffscreen -and -not $c.BoundingRectangle.IsEmpty -and
      $c.BoundingRectangle.Y -ge $wr.Y -and $c.BoundingRectangle.Bottom -le $wr.Bottom -and $c.BoundingRectangle.Width -gt 2 })
  $n = 0; $shown = 0
  foreach ($el in $els) {
    $text = $el.Current.HelpText -replace "`r?`n", ' '
    if ($seen.ContainsKey($text)) { continue }
    $seen[$text] = 1; $n++
    $r = Show-ChmonosHover -Element $el -UserWasTold -WaitMs 1100
    if ($r -notlike '乗せた*') { "$screen`t乗せられない`t$($el.Current.AutomationId)`t$($el.Current.Name)`t$text`t$r" | Add-Content $log -Encoding utf8; continue }
    $tip = Find-Tip
    $er = $el.Current.BoundingRectangle
    if ($tip) {
      $tr = $tip.Current.BoundingRectangle; $shown++
      $L = [Math]::Min($er.Left, $tr.Left) - 16; $T = [Math]::Min($er.Top, $tr.Top) - 16
      $R = [Math]::Max($er.Right, $tr.Right) + 16; $B = [Math]::Max($er.Bottom, $tr.Bottom) + 16
      # 部品が大きい（一覧の行・本文）ときは、吹き出しの周りだけにする
      if (($R - $L) -gt 900 -or ($B - $T) -gt 500) { $L = $tr.Left - 120; $T = $tr.Top - 60; $R = $tr.Right + 120; $B = $tr.Bottom + 16 }
      $file = Save-Rect ("tips-$Theme\{0}-{1:000}" -f $screen, $n) $L $T ($R - $L) ($B - $T)
      "$screen`t出た`t$($el.Current.AutomationId)`t$($el.Current.Name)`t$text`t$(Split-Path $file -Leaf)" | Add-Content $log -Encoding utf8
    } else {
      # 押せない部品の吹き出し（ShowOnDisabled が無い）や、乗せた直後に中身が変わる部品は出ない
      "$screen`t出ない`t$($el.Current.AutomationId)`t$($el.Current.Name)`t$text`t" | Add-Content $log -Encoding utf8
    }
  }
  Clear-ChmonosHover
  "${screen}: 吹き出しの文あり $($els.Count)・新しい文 $n・出た $shown"
}

function Shoot-DropDowns($screen) {
  $win = Get-ChmonosRoot
  $ecCond = New-Object System.Windows.Automation.PropertyCondition $AE::IsExpandCollapsePatternAvailableProperty, $true
  $els = @($win.FindAll($TS::Descendants, $ecCond) | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox })
  $i = 0
  foreach ($el in $els) {
    $i++
    try { Open-ChmonosDropDown -Element $el | Out-Null } catch { continue }
    $f = Save-ChmonosScreenArea -Name ("$Out\menus-$Theme\$screen-combo-{0:00}" -f $i)
    try { Open-ChmonosDropDown -Element $el -Close | Out-Null } catch {}
    "  プルダウン「$($el.Current.Name)」→ $(Split-Path $f -Leaf)"
  }
}

# 押すと小さなメニューが開くボタン。開いたら撮って Esc
function Shoot-MenuButton($screen, $id) {
  $el = Get-ChmonosById $id
  if (-not $el -or $el.Current.IsOffscreen) { return "  メニュー $id：見えていない" }
  Show-ChmonosFront | Out-Null
  # 「▾」の付くボタンは MenuItem（開閉）、「…」は普通のボタン（押すと ContextMenu）、表示順はプルダウン
  $ok = $false
  foreach ($pat in [System.Windows.Automation.ExpandCollapsePattern]::Pattern, [System.Windows.Automation.InvokePattern]::Pattern, [System.Windows.Automation.TogglePattern]::Pattern) {
    try {
      $o = $el.GetCurrentPattern($pat)
      if ($o -is [System.Windows.Automation.ExpandCollapsePattern]) { $o.Expand() } elseif ($o -is [System.Windows.Automation.InvokePattern]) { $o.Invoke() } else { $o.Toggle() }
      $ok = $true; break
    } catch {}
  }
  if (-not $ok) { return "  メニュー $id：押せない" }
  Start-Sleep -Milliseconds 600
  $f = Save-ChmonosScreenArea -Name "$Out\menus-$Theme\$screen-$id"
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  "  メニュー $id → $(Split-Path $f -Leaf)"
}

function Shoot-RightClick($screen, $el, $label) {
  if (-not $el) { return "  右クリック ${label}：見つからない" }
  $c = Get-ChmonosCenter $el
  $r = Invoke-ChmonosRealClick -X $c.X -Y $c.Y -Right -UserWasTold
  $f = Save-ChmonosScreenArea -Name "$Out\menus-$Theme\$screen-right-$label"
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  "  右クリック $label（$r）→ $(Split-Path $f -Leaf)"
}

# ショップの2画面は開かない（上の注意）。商品ページは tour-dense の「ワンピース」（ふつうの衣装の商品）
$screens = [ordered]@{
  '01-search'        = { Invoke-ChmonosById Nav.Search | Out-Null }
  '03-item'          = { Open-ChmonosItem -Id 90000026 | Out-Null }
  '04-edit'          = { Invoke-ChmonosById Nav.Edit | Out-Null }
  '05-import'        = { Invoke-ChmonosById Nav.Import | Out-Null }
  '06-resolve'       = { Invoke-ChmonosById Nav.Resolve | Out-Null }
  '08-folder'        = { Invoke-ChmonosById Nav.Folders | Out-Null }
  '09-avatars'       = { Invoke-ChmonosById Nav.Avatars | Out-Null }
  '10-modifications' = { Invoke-ChmonosById Nav.Modifications | Out-Null }
  '11-tags'          = { Invoke-ChmonosById Nav.TagManage | Out-Null }
  '12-attributes'    = { Invoke-ChmonosById Nav.AttributeManage | Out-Null }
  '13-inbox'         = { Invoke-ChmonosById Nav.Inbox | Out-Null }
  '14-stats'         = { Invoke-ChmonosById Nav.Stats | Out-Null }
  '15-settings'      = { Invoke-ChmonosById Nav.Settings | Out-Null }
}

Show-ChmonosFront | Out-Null
# 窓の既定（1440×900。MainWindow.xaml）。WPF の窓の大きさは枠と題の帯を含むので、中身は少し小さい
Set-ChmonosWindowSize -Width 1440 -Height 900
foreach ($key in $screens.Keys) {
  if ($Only -and $key -notin $Only) { continue }
  & $screens[$key]; Start-Sleep -Milliseconds 1500
  Show-ChmonosFront | Out-Null; Clear-ChmonosHover
  Save-ChmonosScreenArea -Name "$Out\window-$Theme\$key" | Out-Null
  Shoot-Tips $key
  Shoot-DropDowns $key
  switch ($key) {
    '01-search' {
      foreach ($id in 'SearchSortField', 'SearchFilterMenu', 'SearchAddModule', 'SavedSearchesButton', 'SearchTargets', 'SearchOptions') { Shoot-MenuButton $key $id }
      $card = (Get-ChmonosRoot).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition $AE::AutomationIdProperty, 'ItemCard'))
      Shoot-RightClick $key $card 'card'
    }
    '03-item' {
      foreach ($id in 'ItemPluginMenu', 'ItemChangeTarget') { Shoot-MenuButton $key $id }
      # ファイルの行は画面の下の方にあるので、流してから
      Set-ChmonosScrollPercent 60 | Out-Null; Start-Sleep -Milliseconds 600
      Shoot-MenuButton $key 'ItemFileOpenMenu'
      Shoot-Tips '03-item-lower'
    }
  }
}
Clear-ChmonosHover

# 一覧のページ（色ごとの吹き出しの表と、窓・メニューの画像）。両方の色を流した後なら、両方が載る
$enc = { param($t) [System.Net.WebUtility]::HtmlEncode("$t") }
$html = New-Object System.Text.StringBuilder
[void]$html.Append('<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>吹き出しとメニュー</title><style>body{font-family:"Yu Gothic UI",sans-serif;margin:16px}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 6px;vertical-align:top;font-size:13px}img{max-width:560px}.no{color:#a33}.grid img{width:420px;margin:4px;border:1px solid #ccc}</style></head><body><h1>吹き出しとメニュー（起動したアプリで撮影）</h1>')
foreach ($t in 'light', 'dark') {
  $tsv = Join-Path $dir "tips-$t.tsv"
  if (-not (Test-Path $tsv)) { continue }
  $rows = @(Import-Csv $tsv -Delimiter "`t" -Encoding utf8)
  [void]$html.Append("<h2>吹き出し（$t）出た $(@($rows | Where-Object 出た -eq '出た').Count) / 文 $($rows.Count)</h2><table><tr><th>画面</th><th>部品</th><th>文</th><th>画像</th></tr>")
  foreach ($r in $rows) {
    $img = if ($r.出た -eq '出た') { "<img loading=lazy src=`"tips-$t/$($r.画像)`">" } else { "<span class=no>$(& $enc "$($r.出た) $($r.画像)")</span>" }
    [void]$html.Append("<tr><td>$($r.画面)</td><td>$(& $enc "$($r.名前) $($r.ID)")</td><td>$(& $enc $r.吹き出しの文)</td><td>$img</td></tr>")
  }
  [void]$html.Append('</table>')
  foreach ($kind in 'menus', 'window') {
    $folder = Join-Path $dir "$kind-$t"
    if (-not (Test-Path $folder)) { continue }
    [void]$html.Append("<h2>$(if ($kind -eq 'menus') { 'メニュー・プルダウン' } else { '窓（枠を含む）' })（$t）</h2><div class=grid>")
    Get-ChildItem $folder -Filter *.png | Sort-Object Name | ForEach-Object { [void]$html.Append("<a href=`"$kind-$t/$($_.Name)`" title=`"$($_.BaseName)`"><img loading=lazy src=`"$kind-$t/$($_.Name)`"></a>") }
    [void]$html.Append('</div>')
  }
}
[void]$html.Append('</body></html>')
[System.IO.File]::WriteAllText((Join-Path $dir 'index.html'), $html.ToString(), (New-Object System.Text.UTF8Encoding $false))
"終わり：$log・索引 $(Join-Path $dir 'index.html')"
