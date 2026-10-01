# 確かめでよく使う操作（ui-kit.ps1 を読んだ後にドットで読み込む）：
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-kit.ps1"
#   . "D:\work\ClaudeCode\chmonos\.claude\skills\ui-check\scripts\ui-ops.ps1"
#
# なぜ要るか：商品を開く・取り込んで待つ・結果を読む・色を変える、を担当ごとに作業用フォルダへ書き直していた（2026-09-30）。
# どれも UI Automation と窓へのメッセージだけで動く（実入力を使わない）ので、ほかの担当と並行で使える。
# 並行で使えないのは、末尾の「連続で撮る」だけ（画面から直に撮る）。
#
# 部品の名前に頼っている所は、各関数の頭に書いてある。画面の文言やボタンの名前を変えたら、ここも直す

# ---- 画面の中を動かす ----

# 縦に流せる大きな入れ物（画面の本体）を、割合で流す。0＝いちばん上、100＝いちばん下。
# -MinHeight より低い入れ物（一覧の中の小さな枠）は動かさない
function Set-ChmonosScrollPercent {
  param([Parameter(Mandatory)][double]$Percent, [int]$MinHeight = 500)
  $cond = New-Object System.Windows.Automation.PropertyCondition($A_::IsScrollPatternAvailableProperty, $true)
  $moved = 0
  foreach ($s in @((Get-ChmonosRoot).FindAll($TS_::Descendants, $cond))) {
    $p = $s.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($p.Current.VerticallyScrollable -and $s.Current.BoundingRectangle.Height -ge $MinHeight) { $p.SetScrollPercent(-1, $Percent); $moved++ }
  }
  "流した: $Percent %（$moved 個）"
}

# 小窓（知らせ・選ぶ窓）が開いたままか。開いていれば、その題とボタンを言う文を返す（無ければ $null）。
# UI Automation の「押す」は、小窓が開いていても後ろの主の窓に届く。人には押せない状態で操作が進み、
# 小窓の後ろで待っている処理（「商品を残しますか」の答えを待つ「外す」）は走らないまま、確かめだけが先へ行く
# （2026-09-30 に踏んだ。外したつもりで外れていなかった）。画面を移る部品は、先にここを見て止まる
function Get-ChmonosOpenDialogNote {
  $d = Get-ChmonosDialog | Select-Object -First 1
  if (-not $d) { return $null }
  $isButton = New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $names = @($d.FindAll($TS_::Descendants, $isButton) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join '・'
  "小窓が開いたまま: 「$($d.Current.Name)」（ボタン: $names）。先に Close-ChmonosDialog -Button で答える"
}

# ナビのボタンで画面を移り、その画面にしか無い文字か部品が出るまで待つ。
# 頼っている名前：ナビのボタンの名前（検索・取り込み・未確定・設定…）
function Show-ChmonosScreen {
  param([Parameter(Mandatory)][string]$Nav, [string]$WaitText, [double]$TimeoutSeconds = 15)
  $open = Get-ChmonosOpenDialogNote; if ($open) { Write-Warning $open; return $open }
  $r = Invoke-ChmonosByName -Name $Nav -Type Button -WaitSeconds 0
  if ($r -like '無い*') { return $r }
  if ($WaitText) {
    if (-not (Wait-ChmonosText -Like $WaitText -TimeoutSeconds $TimeoutSeconds)) { return "移ったが「$WaitText」が出ない: $Nav" }
  }
  else { Start-Sleep -Milliseconds 700 }
  "移った: $Nav"
}

# ---- 商品ページを開く ----

# 写しの item から、表示の名前を読む（画面のカードの名前と照らすため）。読めなければ $null
function Get-ChmonosItemName {
  param([Parameter(Mandatory)][string]$Id, [string]$Store)
  $root = (Resolve-ChmonosTarget $Store).Store
  $file = Join-Path $root "items\$Id.json"
  if (-not (Test-Path -LiteralPath $file)) { return $null }
  $j = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
  if ($j.local.displayName) { "$($j.local.displayName)" } elseif ($j.booth.name) { "$($j.booth.name)" } else { $null }
}

# 検索の結果に出ているカード（カード表示。AutomationId＝ItemCard）と行（リスト表示。行には ID が無いので型で探す）
function Get-ChmonosItemCards {
  $cards = @(Get-ChmonosById -Id ItemCard -Scope (Get-ChmonosRoot))
  if ($cards.Count) { return $cards }
  @(Get-ChmonosElements -Type DataItem)
}

# 商品 ID か名前を指定して、商品ページを開く。検索欄に「id:<ID>」を入れ、出たカード（リスト表示なら行）を「押す」。
# 戻りは「開いた: …」か、開けなかった理由。**検索の履歴に1件積まれる**（写しに書き込む）。
# 左の条件で絞られていてカードが出ないときは、条件をクリアして探し直す（-KeepFilters で止める）。
# 頼っている名前：ナビの「検索」。頼っている ID：検索欄 QueryBox・カード ItemCard（名前＝商品の名前）・商品ページの ItemEdit。
# 商品ページの側には、どの商品を開いているかを示す名前が無い（「この商品を編集」のボタンが出たことで「開いた」と見ている）
function Open-ChmonosItem {
  param([string]$Id, [string]$Name, [double]$TimeoutSeconds = 15, [switch]$KeepFilters)
  if (-not $Id -and -not $Name) { throw '-Id か -Name を指定する' }
  $open = Get-ChmonosOpenDialogNote; if ($open) { Write-Warning $open; return $open }
  [void](Invoke-ChmonosByName -Name '検索' -Type Button -WaitSeconds 0)
  $box = Wait-ChmonosById -Id QueryBox -TimeoutSeconds $TimeoutSeconds
  if (-not $box) { return '検索欄（QueryBox）が無い' }
  $want = if ($Name) { $Name } else { Get-ChmonosItemName -Id $Id }
  $query = if ($Id) { "id:$Id" } else { $Name }
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($query)
  # 絞り込みは入力の 200ms 後に裏で走る。名前が分かっていれば、その名前のカードが出るまで待つ。
  # 分からなければ、カードの数が2回続けて同じになるまで待つ（絞る前の一覧を掴まないように）
  $state = @{ last = -1 }
  $findCard = {
    $cards = @(Get-ChmonosItemCards)
    if ($want) { return ($cards | Where-Object { $_.Current.Name -eq $want } | Select-Object -First 1) }
    if ($cards.Count -gt 0 -and $cards.Count -eq $state.last) { return $cards[0] }
    $state.last = $cards.Count; $null
  }
  $cleared = ''
  $card = Wait-ChmonosCondition -TimeoutSeconds ([Math]::Min(4, $TimeoutSeconds)) -PollMs 400 -Until $findCard
  if (-not $card -and -not $KeepFilters) {
    # 左の条件（前の確かめで足した「壊れたzip：ある」など）で絞られていて出ないことがある（2026-09-30 に踏んだ）。
    # 条件をクリアして、もう一度探す。クリアは写しの検索の状態に残るので、戻りに書く
    $r = Invoke-ChmonosById -Id SearchClearFilters -WaitSeconds 0.6 -TimeoutSeconds 1
    if ($r -like '押した*') {
      $cleared = '（左の条件をクリアした）'
      $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($query)
      $state.last = -1
      $card = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -PollMs 400 -Until $findCard
    }
  }
  if (-not $card) { return "カードが出ない: $query$(if ($want) { "（名前「$want」）" })$cleared" }
  $label = $card.Current.Name
  $how = Invoke-ChmonosElement $card
  if (-not (Wait-ChmonosById -Id ItemEdit -TimeoutSeconds $TimeoutSeconds)) { return "押したが商品ページへ移らない: $query（$how）" }
  # ボタンが出た直後は、まだ中身を読み込んでいる。ここで返すと、次に押した「ローカルファイルを開く」が読み込みで畳み直された
  # （2026-09-30）。部品の数が2回続けて同じになるまで待つ（長くても 3 秒）
  $settle = @{ last = -1 }
  [void](Wait-ChmonosCondition -TimeoutSeconds 3 -PollMs 250 -Until {
      $n = (Get-ChmonosRoot).FindAll($TS_::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count
      if ($n -eq $settle.last) { return $true }
      $settle.last = $n; $null
    })
  "開いた: $query「$label」$cleared"
}

# ---- 表示の色 ----

# 写しの設定の表示の色を書き換える（light＝明るい・dark＝暗い・system＝Windows に合わせる）。
# **アプリを閉じているときだけ**（開いている間に書くと、アプリが閉じるときに上書きする）。
# 最初に呼んだときの値を控えるので、終わったら Restore-ChmonosTheme で戻す。
# 設定の画面から変える道もある（「表示の色」のコンボ）が、起動の瞬間の色を見る確かめでは、起動の前に決まっている必要がある
function Set-ChmonosTheme {
  param([Parameter(Mandatory)][string]$Store, [Parameter(Mandatory)][ValidateSet('light', 'dark', 'system')][string]$Theme)
  $root = Resolve-ChmonosStore $Store
  Assert-ChmonosSandboxIdle $root
  $file = Join-Path $root 'settings.json'
  if (-not (Test-Path -LiteralPath $file)) { throw "設定が無い: $file" }
  $text = [IO.File]::ReadAllText($file)
  $key = [regex]'("colorTheme"\s*:\s*")(\w+)(")'
  $m = $key.Match($text)
  $now = if ($m.Success) { $m.Groups[2].Value } else { 'system' }
  [IO.Directory]::CreateDirectory($ChmonosStateDir) | Out-Null
  $memo = Join-Path $ChmonosStateDir "$(Get-ChmonosStoreKey $root).theme"
  # 鍵が無かったことも控える（戻すときに「system」と書き足すと、写しが元と1行違ってしまう）
  if (-not (Test-Path -LiteralPath $memo)) { [IO.File]::WriteAllText($memo, $(if ($m.Success) { $now } else { '(none)' })) }
  if ($m.Success) {
    # 在る行の値だけを替える（行を消して先頭に足すと、戻しても並びが元と違い、写しの控えと一致しなくなる）
    $text = $key.Replace($text, "`${1}$Theme`${3}", 1)
  }
  else {
    # 無ければ先頭に足す（アプリは、読めない並びでも鍵の名前で読む）
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $text = [regex]::Replace($text, '^\s*\{\s*\r?\n', "{$nl  `"colorTheme`": `"$Theme`",$nl", 1)
    if ($text -notmatch '"colorTheme"') { throw "設定の形が読めない（先頭が「{」で始まっていない）: $file" }
  }
  [IO.File]::WriteAllText($file, $text, [Text.UTF8Encoding]::new($false))
  "表示の色: $now → $Theme（$root）"
}

function Restore-ChmonosTheme {
  param([Parameter(Mandatory)][string]$Store)
  $root = Resolve-ChmonosStore $Store
  $memo = Join-Path $ChmonosStateDir "$(Get-ChmonosStoreKey $root).theme"
  if (-not (Test-Path -LiteralPath $memo)) { return '控えが無い（Set-ChmonosTheme で変えていない）' }
  $was = [IO.File]::ReadAllText($memo).Trim()
  if ($was -eq '(none)') {
    # 元は鍵が無かった。足した行を取り除いて、元の形に戻す
    Assert-ChmonosSandboxIdle $root
    $file = Join-Path $root 'settings.json'
    $text = [regex]::Replace([IO.File]::ReadAllText($file), '[ \t]*"colorTheme"\s*:\s*"\w+"\s*,?[ \t]*\r?\n', '')
    [IO.File]::WriteAllText($file, $text, [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath $memo -Force
    return '戻した: 表示の色の行を外した（元は書かれていなかった）'
  }
  $r = Set-ChmonosTheme -Store $Store -Theme $was
  Remove-Item -LiteralPath $memo -Force
  "戻した: $r"
}

# ---- 検索の条件 ----

# 検索の「＋ 条件を追加」から条件を足す。メニューを開くのも項目を選ぶのも UI Automation（実入力を使わない）。
#   -Kind … 条件の種類（SearchModuleKind の名前：Path・BrokenZip・Price・Owned…）。**こちらを使う**（文言を変えても壊れない）
#   -Like … 条件の名前（例 '*ファイルの場所*'）。種類が分からないとき
# 条件は見出し（BOOTHの情報・商品の情報…）の下にぶら下がっている。見出しを1つずつ開いて探す（Invoke-ChmonosMenuById）。
# 頼っている ID：メニュー SearchAddModule・項目 SearchAddModule.<種類>。足した条件の部品は SearchModule.<種類>.<部品>
function Add-ChmonosSearchCondition {
  param([string]$Kind, [string]$Like, [double]$TimeoutSeconds = 10)
  if (-not $Kind -and -not $Like) { throw '-Kind か -Like を指定する' }
  $item = if ($Kind) { "SearchAddModule.$Kind" } else { 'SearchAddModule.*' }
  $r = Invoke-ChmonosMenuById -Menu SearchAddModule -Item $item -ItemLike $Like -TimeoutSeconds $TimeoutSeconds
  if ($r -like 'メニューが無い*') { return '「条件を追加」が無い（検索の画面を開いてから）' }
  if ($r -like '押せない*（無効）') { return "もう足してある（選べない）: $($r -replace '^押せない: ', '' -replace '（無効）$', '')" }
  if ($r -like '項目が無い*') { return ($r -replace '^項目が無い: ', '条件が無い: ') }
  if ($r -notlike '押した*') { return $r }
  $added = $r -replace '^押した: ', '' -replace '\(Invoke\)$', ''
  # 足した条件の欄が出るまで待つ（出る前に中の部品を探すと「無い」になる）
  $kindName = ($added -split '「')[0] -replace '^SearchAddModule\.', ''
  [void](Wait-ChmonosById -Id "SearchModule.$kindName.Remove" -TimeoutSeconds 3)
  "条件を足した: $added"
}

# 候補付きの入力欄（SuggestBox）に字を入れ、出た候補の1つを選んで決める。**実入力（実クリック）を使う**：
# 候補の行は UI Automation では「選ぶ」しか持たず、選んでも色が付くだけで決まらない（決まるのは Enter かクリック。2026-09-30 に確かめた）。
# アプリの側で候補の行に「押す」が付いたら、実入力をやめてそれを使う。
#   -Id   … 欄の ID（SearchModule.Category.Input・EditTagInput…）
#   -Text … 欄に入れる字（候補を絞る）。-Pick … 選ぶ候補の名前（省くと先頭）
# 頼っている ID：候補の一覧 Candidates
function Select-ChmonosSuggestion {
  param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][AllowEmptyString()][string]$Text, [string]$Pick, [Parameter(Mandatory)][switch]$UserWasTold, [double]$TimeoutSeconds = 4)
  $set = Set-ChmonosValueById -Id $Id -Value $Text -WaitSeconds 0.2
  if ($set -notlike '入れた*') { return $set }
  $list = Wait-ChmonosById -Id Candidates -TimeoutSeconds $TimeoutSeconds
  if (-not $list) { return "候補が出ない: $Id ← $Text" }
  $items = @(Get-ChmonosElements -Type ListItem -Scope $list)
  $item = if ($Pick) { $items | Where-Object { $_.Current.Name -eq $Pick } | Select-Object -First 1 } else { $items | Select-Object -First 1 }
  if (-not $item) { return "候補に無い: $Pick（在る候補: $(($items | ForEach-Object { $_.Current.Name }) -join '・')）" }
  $name = $item.Current.Name
  $r = Invoke-ChmonosClick -Element $item -UserWasTold:$UserWasTold
  if ($r -notlike '実クリック*') { return $r }
  "選んだ: $Id ← 「$name」"
}

# ---- 取り込み ----

# Windows のフォルダを選ぶ窓に、窓へのメッセージでパスを入れて「フォルダーの選択」を押す（実入力を使わない）。
# この窓だけは Windows が描くので、UI Automation の Invoke では届かない所がある
if (-not ('ChmonosPicker' -as [type])) { Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ChmonosPicker {
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string title);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  // 1152 は、フォルダ名を入れる欄（コンボ）の番号。中の Edit に入れる
  public static IntPtr FindEdit(IntPtr dlg) {
    var combo = GetDlgItem(dlg, 1152);
    if (combo == IntPtr.Zero) return IntPtr.Zero;
    var inner = FindWindowEx(combo, IntPtr.Zero, "ComboBox", null);
    var edit = FindWindowEx(inner == IntPtr.Zero ? combo : inner, IntPtr.Zero, "Edit", null);
    return edit == IntPtr.Zero ? combo : edit;
  }
}
"@ }

function Select-ChmonosFolder {
  param([Parameter(Mandatory)][string]$Path, [double]$TimeoutSeconds = 10)
  if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "フォルダが無い: $Path" }
  # Windows の窓はクラス名が #32770
  $d = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -PollMs 250 -Until { Get-ChmonosDialog | Where-Object { $_.Current.ClassName -eq '#32770' } | Select-Object -First 1 }
  if (-not $d) { return 'フォルダを選ぶ窓が出ていない' }
  $h = [IntPtr]$d.Current.NativeWindowHandle
  $edit = [ChmonosPicker]::FindEdit($h)
  if ($edit -eq [IntPtr]::Zero) { return 'フォルダを選ぶ窓に、名前を入れる欄が無い' }
  [void][ChmonosPicker]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $Path)                                   # WM_SETTEXT
  Start-Sleep -Milliseconds 300
  [void][ChmonosPicker]::PostMessage([ChmonosPicker]::GetDlgItem($h, 1), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)  # BM_CLICK（1＝既定のボタン）
  if (-not (Wait-ChmonosCondition -TimeoutSeconds 5 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($h) })) { return "選んだが窓が閉じない: $Path" }
  "選んだ: $Path"
}

# Windows の保存の窓（ファイル名を入れる窓。設定の「バックアップを書き出す」など）に、フルパスを入れて「保存」を押す（実入力を使わない）。
# ファイル名の欄はコンボの中の Edit。UI Automation では Edit の ID が 1001（窓のメッセージで探す番号ではない）。
# フルパスを入れれば、窓が今どのフォルダを見ていても、その場所に保存される。同じ名前があると上書きを聞く窓が出るので、無い名前にする
function Select-ChmonosSaveFile {
  param([Parameter(Mandatory)][string]$Path, [double]$TimeoutSeconds = 10)
  if (-not [IO.Path]::IsPathRooted($Path)) { throw "フルパスで渡す: $Path" }
  $folder = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "保存先のフォルダが無い: $folder" }
  if (Test-Path -LiteralPath $Path) { throw "同じ名前のファイルが既にある（上書きの確認が出る）: $Path" }

  $d = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -PollMs 250 -Until { Get-ChmonosDialog | Where-Object { $_.Current.ClassName -eq '#32770' } | Select-Object -First 1 }
  if (-not $d) { return '保存の窓が出ていない' }
  $h = [IntPtr]$d.Current.NativeWindowHandle

  $nameEdit = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A_::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)),
    (New-Object System.Windows.Automation.PropertyCondition($A_::AutomationIdProperty, '1001')))
  $edit = Wait-ChmonosCondition -TimeoutSeconds 5 -PollMs 200 -Until { $d.FindFirst($TS_::Descendants, $nameEdit) }
  if (-not $edit) { return '保存の窓に、ファイル名の欄が無い（フォルダを選ぶ窓なら Select-ChmonosFolder）' }

  $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Path)
  Start-Sleep -Milliseconds 300
  [void][ChmonosPicker]::PostMessage([ChmonosPicker]::GetDlgItem($h, 1), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)  # BM_CLICK（1＝「保存」）
  if (-not (Wait-ChmonosCondition -TimeoutSeconds 5 -PollMs 200 -Until { -not [ChmonosWin]::IsWindow($h) })) { return "押したが窓が閉じない（名前が使えない・確認が出た）: $Path" }
  "保存先に入れた: $Path"
}

# 取り込みの画面で、フォルダを今回の対象に足す。監視するかを聞く窓が出たら、-Watch なら「はい」、付けなければ「いいえ」。
# 頼っている ID：ボタン ImportChooseFolder。頼っている名前：窓の題「監視…」・ボタン「はい」「いいえ」
function Add-ChmonosImportFolder {
  param([Parameter(Mandatory)][string]$Path, [switch]$Watch)
  $r = Invoke-ChmonosById -Id ImportChooseFolder -WaitSeconds 0 -TimeoutSeconds 3
  if ($r -notlike '押した*') { return "「フォルダを選択」が押せない（取り込みの画面を開いてから）: $r" }
  $picked = Select-ChmonosFolder -Path $Path
  if ($picked -notlike '選んだ: *') { return $picked }
  # 監視を聞く窓は、まだ監視していないフォルダのときだけ出る
  $asked = Wait-ChmonosDialog -Like '監視*' -TimeoutSeconds 4
  if (-not $asked) { return "対象に足した: $Path（監視は聞かれなかった）" }
  $answer = if ($Watch) { 'はい' } else { 'いいえ' }
  $closed = Close-ChmonosDialog -Button $answer -Like '監視*'
  if ($closed -notlike '閉じた:*') { return $closed }
  "対象に足した: $Path（監視: $answer）"
}

# 取り込みの対象を全部外す（前の確かめの対象が残っていると、一緒に取り込んでしまう）。
# 頼っている ID：行ごとのボタン ImportFolderRemove（名前は「<フォルダ>を対象から外す」）。
# 前は名前「対象から外す」で探していて、名前に行のフォルダが入ってからは1つも外せていなかった（2026-09-30。
# 前の回の対象が残ったまま取り込み、「フォルダだけ」の確かめにファイルの分が混ざった）。外すと行が消えるので、無くなるまで先頭を押す
function Clear-ChmonosImportTargets {
  $n = 0
  for ($i = 0; $i -lt 200; $i++) {
    $btn = Get-ChmonosById -Id ImportFolderRemove -Scope (Get-ChmonosRoot) | Select-Object -First 1
    if (-not $btn) { break }
    try { [void](Invoke-ChmonosElement $btn); $n++ } catch { break }
    Start-Sleep -Milliseconds 150
  }
  $left = @(Get-ChmonosById -Id ImportFolderRemove -Scope (Get-ChmonosRoot)).Count
  "対象から外した: $n 件$(if ($left) { "（まだ $left 件残っている）" })"
}

# 取り込みの結果を読む。部品の ID で読む（前は「結果」の見出しより後ろの文字を並びの順で拾っていた）。
#   Trace    … 足跡の結果の行（命令 ScanFolders）
#   Messages … 結果の文（ID → 文）。読めなかった物（ImportUnreadableLine）・オンラインのみ（ImportOnlineOnlyLine）・壊れた zip（未確定は ImportBrokenZipLine、商品は ImportBrokenZipOnItemsLine）・対応アバター・見つからない・失敗 など。
#               同じ ID が複数あるときは配列
#   Summary  … 数（ImportSummary.<名前> → 数。FilesScanned・FilesHashed・UnresolvedFiles・ItemsAdded…）
#   Buttons  … 結果の欄に出ているボタンの ID（ImportOpenResolve・ImportShowBrokenZip・ImportShowAdded）
#   Lines    … 上の文を並べた物（前の呼び方のため）
function Get-ChmonosImportResult {
  $trace = @(Get-ChmonosTrace -Kind 命令 -Like 'ScanFolders*' -Last 1)
  $root = Get-ChmonosRoot
  $messages = [ordered]@{}; $summary = [ordered]@{}; $buttons = @(); $lines = @()
  foreach ($e in Get-ChmonosById -Id 'Import*' -Scope $root) {
    $id = $e.Current.AutomationId; $name = $e.Current.Name; $type = $e.Current.ControlType.ProgrammaticName
    if ($id -like 'ImportSummary.*') { $summary[$id.Substring('ImportSummary.'.Length)] = $name; continue }
    if ($type -eq 'ControlType.Text' -and $name) {
      if ($messages.Contains($id)) { $messages[$id] = @($messages[$id]) + $name } else { $messages[$id] = $name }
      $lines += $name
    }
    elseif ($type -eq 'ControlType.Button' -and $id -in 'ImportOpenResolve', 'ImportShowBrokenZip', 'ImportShowAdded') { $buttons += $id }
  }
  [pscustomobject]@{
    Trace = if ($trace.Count -and $trace[0] -notlike '足跡が無い*') { $trace[0] } else { $null }
    Messages = $messages; Summary = $summary; Buttons = $buttons; Lines = $lines
  }
}

# 「取り込みを開始」を押して、終わるまで待つ。戻りは掛かった秒と結果（Get-ChmonosImportResult の中身）。
# 終わりは足跡で見る（命令 ScanFolders の結果が1行増える）。足跡を切って起動したときは、「中断」が押せなくなるのを待つ。
# -During は待っている間に繰り返し呼ぶ処理（取り込み中に別の画面を開く確かめなど。引数は経った秒）。
# 頼っている ID：ボタン ImportStart・ImportCancel
function Start-ChmonosImport {
  param([double]$TimeoutSeconds = 600, [scriptblock]$During)
  $count = { $t = @(Get-ChmonosTrace -Kind 命令 -Like 'ScanFolders*' -Last 100000); if ($t.Count -and $t[0] -like '足跡が無い*') { -1 } else { $t.Count } }
  $before = & $count
  $r = Invoke-ChmonosById -Id ImportStart -WaitSeconds 0 -TimeoutSeconds 3
  if ($r -notlike '押した*') { return "「取り込みを開始」が押せない（取り込みの画面を開いて、対象を足してから）: $r" }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $done = $false
  while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if ($During) { & $During $sw.Elapsed.TotalSeconds } else { Start-Sleep -Milliseconds 300 }
    if ($before -ge 0) { if ((& $count) -gt $before) { $done = $true; break } }
    # 「中断」は走っている間だけ押せる。押した直後はまだ切り替わっていないので、1秒は待つ
    elseif ($sw.Elapsed.TotalSeconds -gt 1) {
      $cancel = Get-ChmonosById -Id ImportCancel -Scope (Get-ChmonosRoot) | Select-Object -First 1
      if (-not $cancel -or -not $cancel.Current.IsEnabled) { $done = $true; break }
    }
  }
  $seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
  if (-not $done) { return [pscustomobject]@{ Done = $false; Seconds = $seconds; Trace = $null; Messages = @{}; Summary = @{}; Buttons = @(); Lines = @("時間切れ（$TimeoutSeconds 秒）") } }
  # 結果の欄が描かれるのを待つ（数の欄が出るまで）
  [void](Wait-ChmonosById -Id 'ImportSummary.FilesScanned' -TimeoutSeconds 5)
  $result = Get-ChmonosImportResult
  [pscustomobject]@{ Done = $true; Seconds = $seconds; Trace = $result.Trace; Messages = $result.Messages; Summary = $result.Summary; Buttons = $result.Buttons; Lines = $result.Lines }
}

# 取り込みの画面へ移り、フォルダを対象に足して取り込み、終わるまで待って結果を返す（上の部品をつないだ物）。
# **BOOTH へ問い合わせ得る**：ダウンロード元の記録や zip の中の URL から商品が分かるファイルは、BOOTH から取得する。
# 手掛かりの無い作り物（fixtures.ps1 で作り、記録を付けていない物）は、問い合わせずに未確定へ入る
function Invoke-ChmonosImport {
  param([Parameter(Mandatory)][string[]]$Path, [switch]$Watch, [double]$TimeoutSeconds = 600, [switch]$KeepTargets)
  $moved = Show-ChmonosScreen -Nav '取り込み'
  if (-not (Wait-ChmonosById -Id ImportChooseFolder -TimeoutSeconds 10)) { return "取り込みの画面へ移れない（$moved）" }
  if (-not $KeepTargets) { [void](Clear-ChmonosImportTargets) }
  foreach ($p in $Path) {
    $added = Add-ChmonosImportFolder -Path $p -Watch:$Watch
    if ($added -notlike '対象に足した:*') { return $added }
  }
  Start-ChmonosImport -TimeoutSeconds $TimeoutSeconds
}

# ---- 同じ写しで2本目を起動する（「既に起動しています」の窓を見る） ----

# 1本目が開いている写しで、もう1本起動する。アプリは保存先ごとに二重起動を止め、知らせの窓を出して終わる。
# 戻りは2本目のプロセスと、その知らせの窓（撮る・文を読む・OK を押すのは呼ぶ側で）。控えには書かない（1本目の相手のまま）
function Start-ChmonosSecond {
  param([string]$Store, [string]$Exe, [double]$TimeoutSeconds = 15)
  $first = Resolve-ChmonosTarget $Store
  $exePath = if ($Exe) { $Exe } else { $first.Process.MainModule.FileName }
  $psi = [Diagnostics.ProcessStartInfo]::new($exePath)
  $psi.UseShellExecute = $false; $psi.WorkingDirectory = Split-Path $exePath
  $psi.Environment['CHMONOS_HOME'] = $first.Store
  [void]$psi.Environment.Remove('CHMONOS_UITRACE')
  $p = [Diagnostics.Process]::Start($psi)
  $window = Wait-ChmonosCondition -TimeoutSeconds $TimeoutSeconds -PollMs 200 -Until {
    $p.Refresh(); if ($p.HasExited -or $p.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
    $A_::FromHandle($p.MainWindowHandle)
  }
  [pscustomobject]@{ Process = $p; Window = $window; Text = if ($window) { @(Get-ChmonosDialogText $window) } else { @() } }
}

# ---- 連続で撮る（起動の瞬間・短い間だけ出る帯） ----
#
# **画面から直に撮る**（PrintWindow は「今の中身」を描かせるので、実際に見えた白いコマが写らない）。
# 窓が画面に見えている必要があり、上に別の窓が重なるとそれが写る。ほかの担当と並行では使えない
# （ほかのアプリが開いているときは、Lock-ChmonosScreen で画面を取ってから）。
#
# 撮った全部を持つと数百MBになる（1440x900 で1枚 5MB・6秒で約170枚）。数字は全部のコマから出し、
# 絵は「色が変わったコマ」と最後のコマだけを残す（起動の移り変わりは数枚で足りる）

function Initialize-ChmonosFramesType {
  if ('ChmonosFrames' -as [type]) { return }
  Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.Runtime.InteropServices; using System.Text; using System.Threading;
public sealed class ChmonosFrames {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
  [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int size, width, height; public short planes, bitCount; public int compression, sizeImage, xppm, yppm, clrUsed, clrImportant; }

  public sealed class Frame {
    public double Ms; public long Hwnd; public int Pid; public string Title; public int X, Y, W, H;
    public double WhitePct, BrightPct; public int R, G, B;
    public byte[] Pixels;   // 残したコマだけ（BGRA・上から下）
  }
  public readonly List<Frame> Frames = new List<Frame>();
  readonly string _name; readonly HashSet<uint> _known; readonly bool _onlyNew; readonly int _interval, _keepMax;
  readonly Dictionary<uint, bool> _wanted = new Dictionary<uint, bool>();
  readonly Dictionary<long, Frame> _lastKept = new Dictionary<long, Frame>();
  readonly Stopwatch _clock = Stopwatch.StartNew(); Thread _t; volatile bool _stop; int _kept;
  public string Error;

  // onlyNew＝true：knownPids に無い、名前が processName のプロセスの窓（これから起動する物）
  // onlyNew＝false：knownPids のプロセスの窓（もう開いている物）
  public ChmonosFrames(string processName, int[] knownPids, bool onlyNew, int intervalMs, int keepMax) {
    _name = processName; _known = new HashSet<uint>(); foreach (var p in knownPids) _known.Add((uint)p);
    _onlyNew = onlyNew; _interval = intervalMs; _keepMax = keepMax;
  }
  public double Now() { return _clock.Elapsed.TotalMilliseconds; }
  public int Count { get { lock (Frames) return Frames.Count; } }
  public void Start() { _t = new Thread(Run); _t.IsBackground = true; _t.Start(); }
  public void Stop() { _stop = true; _t.Join(); }

  bool Wanted(uint pid) {
    bool w;
    if (_wanted.TryGetValue(pid, out w)) return w;
    if (_onlyNew) { w = false; if (!_known.Contains(pid)) { try { w = Process.GetProcessById((int)pid).ProcessName == _name; } catch { } } }
    else w = _known.Contains(pid);
    _wanted[pid] = w; return w;
  }

  void Run() {
    // 画面の実際の画素で測る（倍率を掛けた座標だと、窓の四角と撮る範囲がずれる）。このスレッドだけに効く
    try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
    var screen = GetDC(IntPtr.Zero);
    try {
      while (!_stop) {
        var t0 = _clock.Elapsed.TotalMilliseconds;
        var targets = new List<KeyValuePair<IntPtr, uint>>();
        EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (IsWindowVisible(h) && Wanted(pid)) targets.Add(new KeyValuePair<IntPtr, uint>(h, pid)); return true; }, IntPtr.Zero);
        foreach (var t in targets) Capture(screen, t.Key, t.Value, t0);
        var rest = _interval - (_clock.Elapsed.TotalMilliseconds - t0);
        if (rest > 0) Thread.Sleep((int)rest);
      }
    }
    catch (Exception e) { Error = e.Message; }
    finally { ReleaseDC(IntPtr.Zero, screen); }
  }

  void Capture(IntPtr screen, IntPtr h, uint pid, double t0) {
    RECT r;
    // 影を含まない、見えている四角（9 = DWMWA_EXTENDED_FRAME_BOUNDS）
    if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
    int w = r.R - r.L, hh = r.B - r.T; if (w <= 0 || hh <= 0) return;
    var mem = CreateCompatibleDC(screen); var bmp = CreateCompatibleBitmap(screen, w, hh); var old = SelectObject(mem, bmp);
    var bytes = new byte[w * hh * 4];
    try {
      BitBlt(mem, 0, 0, w, hh, screen, r.L, r.T, 0x00CC0020);   // SRCCOPY
      SelectObject(mem, old);
      var info = new BITMAPINFOHEADER { size = 40, width = w, height = -hh, planes = 1, bitCount = 32 };
      GetDIBits(screen, bmp, 0, (uint)hh, bytes, ref info, 0);
    }
    finally { DeleteObject(bmp); DeleteDC(mem); }
    // 題の帯の下（上から 40 画素より下）を、縦横1つおきに見る。白に近い画素の割合と、明るい画素の割合と、平均の色
    long white = 0, bright = 0, tot = 0, sr = 0, sg = 0, sb = 0; int stride = w * 4;
    for (int y = Math.Min(40, hh - 1); y < hh; y += 2) for (int x = 0; x < w; x += 2) {
      int i = y * stride + x * 4; int b = bytes[i], g = bytes[i + 1], rr = bytes[i + 2];
      tot++; sr += rr; sg += g; sb += b;
      if (rr >= 235 && g >= 235 && b >= 235) white++;
      if (rr + g + b >= 540) bright++;
    }
    var title = new StringBuilder(256); GetWindowText(h, title, 256);
    var f = new Frame { Ms = t0, Hwnd = h.ToInt64(), Pid = (int)pid, Title = title.ToString(), X = r.L, Y = r.T, W = w, H = hh,
      WhitePct = 100.0 * white / tot, BrightPct = 100.0 * bright / tot, R = (int)(sr / tot), G = (int)(sg / tot), B = (int)(sb / tot) };
    // 絵を残すのは、その窓の最初のコマと、前に残したコマから色が動いたコマ（どの色も 6 を超えて違う・白の割合が 10 を超えて違う）
    Frame last; var keep = !_lastKept.TryGetValue(f.Hwnd, out last)
      || Math.Abs(f.R - last.R) > 6 || Math.Abs(f.G - last.G) > 6 || Math.Abs(f.B - last.B) > 6 || Math.Abs(f.WhitePct - last.WhitePct) > 10;
    if (keep && _kept < _keepMax) { f.Pixels = bytes; _lastKept[f.Hwnd] = f; _kept++; }
    lock (Frames) Frames.Add(f);
  }
}
'@
}

# コマ（ChmonosFrames.Frame）の並びから数字を出す。主の窓＝いちばん大きい窓のコマだけを数える。
#   白いコマ   … 白に近い画素が半分以上（暗い色の設定で、起動の瞬間に白い窓が見えた、を数える）
#   明るいコマ … 明るい画素が半分以上
#   出揃った   … 平均の色が最後のコマと同じ（どの色も差が 3 以内）になり、その後ずっと動かなくなった最初のコマ
# -OriginMs は時刻の 0 にする時点（起動を始めた時。コマの Ms は撮り始めからの時間）。-Name を付けると残した絵を書き出す
function Get-ChmonosFrameSummary {
  param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Frames, [double]$OriginMs = 0, [string]$Name)
  if (-not $Frames.Count) { return [pscustomobject]@{ コマ = 0; 白いコマ = 0; 明るいコマ = 0; 窓が出たms = $null; 出揃ったms = $null; 出てから出揃うまでms = $null; 間隔ms = $null; 色の移り = ''; ほかの窓 = ''; 絵 = @() } }
  $groups = @($Frames | Group-Object Hwnd | Sort-Object { $_.Group[0].W * $_.Group[0].H } -Descending)
  $fr = @($groups[0].Group)
  $last = $fr[-1]
  $settled = $fr.Count - 1
  for ($i = $fr.Count - 1; $i -ge 0; $i--) {
    $f = $fr[$i]
    if ([Math]::Abs($f.R - $last.R) -gt 3 -or [Math]::Abs($f.G - $last.G) -gt 3 -or [Math]::Abs($f.B - $last.B) -gt 3) { break }
    $settled = $i
  }
  $colors = [System.Collections.Generic.List[string]]::new()
  foreach ($f in $fr) { $c = "$($f.R)/$($f.G)/$($f.B)"; if (-not $colors.Count -or $colors[$colors.Count - 1] -ne $c) { $colors.Add($c) } }
  $gaps = @(for ($i = 1; $i -lt $fr.Count; $i++) { $fr[$i].Ms - $fr[$i - 1].Ms })
  $shots = @()
  if ($Name) {
    $dir = Join-Path $ChmonosShotDir $Name
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    [IO.Directory]::CreateDirectory($dir) | Out-Null
    $n = 0
    # 残っているのは色が動いたコマ。最後に残ったコマが、出揃った後の姿
    foreach ($f in $fr) {
      $n++
      if (-not $f.Pixels) { continue }
      # 画面から写した絵は透明度が 0 なので、透明度を見ない形で書き出す
      $bmp = [System.Drawing.Bitmap]::new($f.W, $f.H, [System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
      $data = $bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $f.W, $f.H), [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $bmp.PixelFormat)
      [System.Runtime.InteropServices.Marshal]::Copy($f.Pixels, 0, $data.Scan0, $f.Pixels.Length)
      $bmp.UnlockBits($data)
      $path = Join-Path $dir ('f{0:D4}_{1:D5}ms.png' -f $n, [int]($f.Ms - $OriginMs))
      $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
      $shots += $path
    }
  }
  [pscustomobject]@{
    コマ = $fr.Count
    白いコマ = @($fr | Where-Object { $_.WhitePct -ge 50 }).Count
    明るいコマ = @($fr | Where-Object { $_.BrightPct -ge 50 }).Count
    窓が出たms = [int]($fr[0].Ms - $OriginMs)
    出揃ったms = [int]($fr[$settled].Ms - $OriginMs)
    出てから出揃うまでms = [int]($fr[$settled].Ms - $fr[0].Ms)
    間隔ms = if ($gaps.Count) { [int](($gaps | Measure-Object -Average).Average) } else { $null }
    色の移り = (@($colors | Select-Object -First 10) -join ' > ') + $(if ($colors.Count -gt 10) { " …（$($colors.Count) 色）" } else { '' })
    ほかの窓 = (@($groups | Select-Object -Skip 1 | ForEach-Object { $g = $_.Group[0]; "「$($g.Title)」$($g.W)x$($g.H) $($_.Count)枚 白$([int]$g.WhitePct)%" }) -join ' / ')
    絵 = $shots
  }
}

# 画面から直に撮ってよいか（並行の決まり）。だめなら投げる
function Assert-ChmonosScreenFree([string]$Store) {
  $held = Get-ChmonosScreenLockInfo
  $root = if ($Store) { Resolve-ChmonosStore $Store } else { $null }
  if ($root) {
    if ($held -and "$($held.store)" -ine $root) { throw "画面は $($held.key) の確かめが使っている（$($held.until) まで）" }
    $others = @(Get-ChmonosRunning | Where-Object { $_.Store -ine $root })
    if (-not $held -and $others.Count) { throw "ほかのアプリが開いている（$(($others | ForEach-Object { $_.Key }) -join '・')）。画面から撮る部品は、Lock-ChmonosScreen -Store で画面を取ってから" }
  }
  else { $deny = Get-ChmonosScreenDenial; if ($deny) { throw $deny } }
}

# 起動の瞬間を連続で撮り、白いコマの数と、出揃うまでの時間を返す。
#   -IntervalMs … 撮る間隔（既定 35ms。1枚撮るのに 10〜20ms 掛かるので、これより短くしても詰まらない）
#   -Frames     … 主の窓を何枚撮るか（既定 170 枚＝約6秒）
#   -Theme      … 起動の前に写しの表示の色を変える（終わったら Restore-ChmonosTheme で戻す）
#   -Exe        … 別の版で起動する（前後の版を比べる）
# 時刻は「起動を頼んだ時」から数える。アプリは開いたまま返す（-Close で閉じる）。
# その写しのアプリが開いていたら、先に閉じてから呼ぶ
function Measure-ChmonosLaunch {
  param([Parameter(Mandatory)][string]$Store, [int]$IntervalMs = 35, [int]$Frames = 170, [string]$Name = 'launch',
    [ValidateSet('', 'light', 'dark', 'system')][string]$Theme = '', [string]$Exe, [switch]$Close)
  Assert-ChmonosScreenFree $Store
  Initialize-ChmonosFramesType
  if ($Theme) { [void](Set-ChmonosTheme -Store $Store -Theme $Theme) }
  # 前から開いている同じ名前のプロセス（ユーザのアプリ・ほかの担当のアプリ）は撮らない
  $known = @(Get-Process -Name $ChmonosProcessName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
  $cap = [ChmonosFrames]::new($ChmonosProcessName, [int[]]$known, $true, $IntervalMs, 16)
  $cap.Start()
  try {
    $origin = $cap.Now()
    [void](Start-ChmonosApp -Store $Store -SettleSeconds 0 -Exe $Exe)
    # 主の窓のコマが -Frames 枚たまるまで（小窓のコマも数に入るので、少し多めに待つことがある）。出ないときは 30 秒で諦める
    $limit = [DateTime]::Now.AddSeconds(30 + $Frames * $IntervalMs / 1000.0)
    while ($cap.Count -lt $Frames -and [DateTime]::Now -lt $limit) { Start-Sleep -Milliseconds 100 }
  }
  finally { $cap.Stop() }
  if ($cap.Error) { Write-Warning "撮っている途中で止まった: $($cap.Error)" }
  $summary = Get-ChmonosFrameSummary -Frames @($cap.Frames.ToArray()) -OriginMs $origin -Name $Name
  if ($Close) { [void](Stop-ChmonosApp -Store $Store) }
  $summary
}

# 開いているアプリを、操作（-Action）の間だけ連続で撮る。短い間だけ出る帯・窓の出方を見る。
# -Seconds は撮る長さ（-Action が先に終わっても、この長さまで撮り続ける）
function Measure-ChmonosFrames {
  param([Parameter(Mandatory)][scriptblock]$Action, [double]$Seconds = 5, [int]$IntervalMs = 35, [string]$Name = 'frames', [string]$Store)
  $target = Resolve-ChmonosTarget $Store
  Assert-ChmonosScreenFree $target.Store
  Initialize-ChmonosFramesType
  $cap = [ChmonosFrames]::new($ChmonosProcessName, [int[]]@($target.Pid), $false, $IntervalMs, 24)
  $cap.Start()
  try {
    Start-Sleep -Milliseconds 150   # 操作の前の姿を1枚は撮る
    $origin = $cap.Now()
    & $Action | Out-Null
    while (($cap.Now() - $origin) -lt $Seconds * 1000) { Start-Sleep -Milliseconds 50 }
  }
  finally { $cap.Stop() }
  if ($cap.Error) { Write-Warning "撮っている途中で止まった: $($cap.Error)" }
  Get-ChmonosFrameSummary -Frames @($cap.Frames.ToArray()) -OriginMs $origin -Name $Name
}
