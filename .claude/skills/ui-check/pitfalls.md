# 画面の確かめのつまずき

[SKILL.md](SKILL.md) の流れでつまずいたときに読む。どれも実際に踏んだ物。

## 確かめ用の足跡（撮らずに文言を確かめる）

`Start-ChmonosApp` は既定で `CHMONOS_UITRACE` を付けて起動する（`-NoTrace` で切る）。アプリはこの変数があるときだけ、
出した窓の文言と押されたボタン・選ぶ窓の答え・実行した `UiCommand` と結果・Unity の取り込みの結果を1行ずつ書く（`Core/Services/UiTrace.cs`）。

```powershell
Get-ChmonosTrace -Kind 知らせ -Last 5
Wait-ChmonosTrace -Like '*既に全部入っていました*'   # 出るまで待つ
Get-ChmonosTrace -Saved tagcheck -Kind 命令          # 前の起動の分（写しの名前か、Save-ChmonosTrace で付けた名前）
```

- 足跡は写しごとのファイル（`%TEMP%\chmonos-ui-check\<写し>.uitrace.log`）。同じ写しで次に起動するときに、前の分は控え
  （`%TEMP%\chmonos-uitrace-history`・新しい 40 本）へ移る。回ごとに取っておくなら `Save-ChmonosTrace -Name round1`。
- 知らせの窓（`Services.Notice`）は自前の WPF の窓（`Views/NoticeWindow`・AutomationId `ChmonosNotice`）で、本文もボタンも UI Automation に出る（本文は入力欄の名前）。
  それでも文言の確かめは足跡が速くて確か（撮って読むのは、並びや色を見るときだけ）。ファイルやフォルダを選ぶ窓だけは Windows が描く（`Select-ChmonosFolder`）。

## UI Automation

- GridView の一覧（リスト表示）の行は `ListItem` ではなく `DataItem`。改変の画面のように1画面に複数の一覧があると、別の一覧の行も混ざる。行の中の固有のボタン（「Unity ▾」など）で絞る
- 仮想化した一覧は画面に見えている行しか数えない。「畳んだら減るはず」を数で確かめると、下の行が見えて逆に増える。畳んだ中の行が在るか無いかで見る
- 画面の外の要素は四角が空か無限大になる。座標に使う前に `Get-ChmonosCenter` を通す
- 持ち主付きの窓（`ShowDialog` の窓・知らせの窓）は主の窓の子として出る（デスクトップの直下には無い）。撮るときは `-Element` に Window の要素を渡す。
  知らせの窓は、主の窓より前やアプリが後ろにいたときは持ち主なしでデスクトップの直下に出る（`Get-ChmonosDialog` は両方を探す）
- 「はい・いいえ」だけの知らせの窓は Esc も × も効かない（MessageBox と同じ）。閉じるにはどちらかのボタンを押す（`Close-ChmonosDialog -Button`）
- `Close-ChmonosDialog` は UI Automation の Invoke で押す。**押せない・閉じないときは「閉じられない:」で始まる文を返す**（前は Enter で閉じていて、
  「いいえ」を頼んだのに既定の「はい」で閉じていた。2026-09-30）。ボタンの名前が違うときは、在るボタンの名前が戻りに並ぶ
- `SendMessage` 系の合成クリックは WPF に届かない
- GridSplitter を継いだ部品は、クラス名が `GridSplitter` のまま出る
- **操作できる部品には AutomationId が付いている**（2026-09-30。`docs/spec/ui-input.md`「読み上げの名前」）。カード・カードの星・リストの行・札・下の帯・
  未確定の行と候補・ファイルの行のボタンとメニュー・取り込みの結果・設定の欄は、どれも「押す」で動く（実際のアプリで確かめた）。
  前にここに書いていた「カードのクリックは実入力」「ItemsControl の中は実入力で押す」は、もう要らない。ID は `Get-ChmonosIds` で見る
- **カードの一覧に「段」の項目は出ない**（2026-09-30）。前は一覧（`ListBox`）の下に段（`ListItem`・名前は型の名前）が並び、その中にカードがあった。
  今はカード（`ListItem`・ID `ItemCard`／`ShopCard`）が一覧（クラス名 `CardRowsListBox`）の直下に並ぶ。一覧の `ListItem` を数えればカードの枚数（今作られている分）になる。
  一覧は「選ぶ」を持たない（流す操作は持つ）。タグと属性の管理の段は `CardRowItems` として木に残るが、既定の見方（`Get-ChmonosElements`）には出ない
- 素の `ItemsControl` は今も中身を隠す。出ない部品があったら、画面側が `Controls/ContentItemsControl` になっているかを見て、報告に書く（道具で回り込まない）
- ID も名前も無くて困った部品は、報告に書く（名前や並びに頼ると、文言を変えただけで確かめが壊れる）。商品ページの「どの商品か」は `ItemIdCopy`（名前が「ID 1234567」。2026-09-30 に付けた）。
  ナビのボタンは `Nav.Search`・`Nav.Import`・`Nav.Settings`…（`Nav.<画面>`。戻る・進む・開閉は `Nav.Back`・`Nav.Forward`・`Nav.Toggle`）、
  改変の画面の上の切り替えは `ModificationHubLevel.Project`／`.Avatar`／`.Modification`、
  左の一覧の行は `FolderTreeRow`・`TagTopRow`・`AttributeRow`・`AvatarRow`・`AvatarBaseRow`・`ModificationMemberRow`（名前＝行の名前）、
  ショップの星は `ShopFavorite`（ショップ画面）・`ShopCardFavorite`（一覧のカード）、統計の行は `StatsShopRow`・`StatsCategoryRow`、商品ページのショップ名は `ItemShopLink`。
  **畳む欄の見出しの押す所（商品説明・BOOTHのタグ・対応アバター・動画など）は、名前が状態で替わる**：「商品説明を開く」⇄「商品説明を折りたたむ」。
  欄そのもの（`ItemDescriptionExpander` など。名前は「商品説明」のまま）を `Set-ChmonosToggleById` で開閉するのが確実。
  リストの行は `ItemListRow`、未確定の行は `ResolveFileRow`、未確定の上の検索欄は `ResolveFilter`、検索の「＋ 条件を追加」は `SearchAddModule`（2026-09-30 に付けた）
- **開閉のボタン・チェックは「押す」と切り替わる。**最初から開いている欄を畳んでしまう（名前は状態で替わる：「ローカルファイルを開く」⇄「ローカルファイルを折りたたむ」。名前で探すなら今の状態の側で）。
  `Set-ChmonosToggleById`（今の状態を見て、違うときだけ切り替える）を使う
- **ボタン・メニューの項目の名前の `_` は、そのまま出る**（前は WPF がアクセスキーの印として最初の1つを外していた。`Services/AutomationNames` で守る）。名前を付けずに見出しの文字を読ませているメニューの項目だけは、まだ最初の `_` が消える。
  ファイル名で絞るときは、`_` の無い名前で作り物を作るか、`-Like` で前後を合わせる
- **候補付きの入力欄（`SuggestBox`）は、UI Automation では決められない。**値は入り、候補（`Candidates`）も出るが、候補の行は「選ぶ」しか持たず、
  選んでも決まらない。`Select-ChmonosSuggestion`（実クリック）を使う。アプリの側の直しが要る物として報告した（2026-09-30）
- **設定の数字の欄は、値を入れただけでは保存されない**（フォーカスが外れたときに書く作り）。UI Automation で入れた値は画面に出るが、`settings.json` は変わらない
- **小窓が開いていても、後ろの主の窓の部品は「押す」で動いてしまう。**人には押せない状態のまま確かめが進み、小窓の答えを待っている処理は走らない
  （最後のファイルを外すと「商品を残しますか」が続けて出る。答えずに進めて、外れていなかった）。押した後は足跡で命令の結果（`DetachFile → …`）を見る。
  `Show-ChmonosScreen`・`Open-ChmonosItem` は、小窓が開いていたら止まってそう返す
- **ボタンが出すメニュー（行の［開く ▾］）は、アプリが後ろにいると1秒もせずに閉じる。**開いてから項目を押すまでを1つの呼び出しで行う（`Invoke-ChmonosMenuById`）。
  検索の「＋ 条件を追加」は2段（見出しの下に条件）で、下の段は見出しを開くまで出ない（同じ関数が見出しを順に開いて探す）
- **木を丸ごとなめると、その間アプリの画面のスレッドが止まる**（`Get-ChmonosIds`・ID に `*` を使った `Get-ChmonosById`・`Get-ChmonosTexts`）。
  速さと固まりを測っている間は呼ばない（見張りの「固まり」に道具の分が乗る）
- **検索の条件は同じ種類を複数置ける**（候補から積む条件とユーザータグ・2026-10-01）。1つ目の ID は前のまま（`SearchModule.Category.Input`）、
  2つ目から種類の所に番号が付く（`SearchModule.Category-2.Input`。名前も「カテゴリ（2つ目）で絞り込む」）。番号は上からの位置で、並べ替えると振り直る。
  `SearchModule.Category.*` を `*` で探すと2つ目は入らない（`SearchModule.Category-*.*` も足す）。`SearchModule.Avatar*` は `AvatarUnconfirmed` も拾う。
  条件の「…」（`SearchModule.<種類>.Menu`）は乗せるか枠の中に止まるまで透明だが、木には出ていて「押す」でメニューが開く。
  条件の「除く」はメニューの行 `.Menu.Exclude`（入・切）、除いている間だけ見出しに札 `.Excluded`（押すと除くのをやめる）
- 検索に条件が残っていると、`id:` で探しても商品が出ない（前の確かめの「壊れたzip：ある」など）。`Open-ChmonosItem` は条件をクリアして探し直し、戻りにそう書く

## 実入力（mouse_event）

UI Automation で届かない所（候補付きの入力欄の候補を決める・ホバー・境目のドラッグ・右クリックのメニューを出す）だけ。
カード・星・行・札・メニューの項目は「押す」で動くので、実入力は要らない（2026-09-30 に確かめた）。

- **使う前にユーザへ告げる。**`-UserWasTold` はその確認で、付けないと動かない
- **ほかのアプリが開いている間は、`Lock-ChmonosScreen` で画面を取ってから**（取っていないと、部品は送らずに止まる）
- 座標は `Get-ChmonosCenter` から取る。関数は、アプリが前面にない・座標が窓の外・上に別の窓がある ときは送らずに止まる
  （UI Automation で要素が見つからず四角が空になり、画面の左上＝デスクトップを押し・ドラッグし・ダブルクリックした事故がある。2026-09-14）
- ホバーの見た目・ドラッグは実入力でも確かめきれないことが多い。そのときは「実際のマウスでの確認が要る」と報告に書き、確かめたふりをしない
- 窓が最大化されていると、窓の四角は (-8,-8) から始まる
- ポップアップ・メニュー・ツールチップは窓の外の別の窓に出るので、窓を撮っても写らない。デスクトップごと撮る（`CopyFromScreen`）
- Alt を単独で押さない（`keybd_event` で前面に出すときの小細工など）。離した瞬間に窓のシステムメニューが開き、以降のキーが飲まれる。前面に出すのは `[ChmonosWin]::Bring`
- 小窓が開いているときに主の窓を前に出すと、小窓がその下に隠れて押せなくなる（`Invoke-ChmonosRealClick` は小窓の方を前に出す）

## 起動の瞬間・短い間だけ出る物

- `Save-ChmonosShotAround` は、流した先（窓の見えている範囲の外）の部品を撮れない（断る）。先に `Set-ChmonosScrollPercent` で流す
- `Save-ChmonosShot`（PrintWindow）は「今の中身」を描かせて撮る。**起動の瞬間に実際に見えた白い窓は写らない。**
  見えた物を撮るのは `Measure-ChmonosLaunch`・`Measure-ChmonosFrames`（画面から直に撮る。窓が画面に見えている必要がある）
- 数字は全部のコマから出し、絵は色が動いたコマだけを `%TEMP%\chmonos-shots\<名前>\` に残す（全部残すと数百MB）
- 前後の版を比べるときは `-Exe` で版を替え、交互に測る（`perf-measure` スキルの決まり）

## Unity

Unity を実際に動かす確かめ（改変の「Unity ▾」・「Unityで選択」）は、ユーザが「試して」と言ったときだけ（2026-09-15）。
Unity が手前に出て画面を横取りするので、ユーザが見ていられるときに行う。ほかの担当と並行では行わない。

`scripts/unity-kit.ps1`（ui-kit を読んだ後にドットで読み込む）：
`Get-UnityEditors` / `Get-UnityWindows [-Title]` / `Wait-UnityImportWindow` / `Save-UnityShot -Window $w -Name x`（隠れていても中身が撮れる）/
`Close-UnityImport -Button Import|Cancel|OK -UserWasTold`（閉じたかを確かめて押し直す。Unity は1回目のクリックが窓を前に出すだけに使われる）。
