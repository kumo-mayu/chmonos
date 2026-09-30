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
- `ItemsControl` の中の部品は UI Automation に出ないことが多い（タグの管理の小分類の行など）。
  素の `ItemsControl` は中身を隠してしまう。画面側を `Controls/ContentItemsControl` に替えると出る（検索のカードは 2026-09-20 にそうした）
- 名前の無い部品に頼ると、文言や並びを変えただけで確かめが壊れる。名前（`AutomationProperties.Name`・`AutomationId`）が無くて困った部品は、報告に書く

## 実入力（mouse_event）

UI Automation で届かない所（カードのクリック・ホバー・境目のドラッグ）だけ。

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
