---
name: ui-check
description: Chmonos の画面を確かめる（写しの保存先で起動し、UI Automation で操作し、PrintWindow で撮って見て、本番が変わっていないかを照らす）。画面・レイアウト・文言を直したあと、ユーザに「確かめて」「起動して」「〇〇のデータで見れますか」と言われたとき、画面の不具合を調べるときに使う。
---

# 画面の確かめ

**UI Automation は「要素があるか」しか答えない。「どこにあるか」は答えない。**レイアウトを壊しても気付けないので、**必ず画像で見る**
（`DockPanel` の子は既定で `Dock="Left"` に落ちる、という壊し方を実際にやっている）。

## 道具

`scripts/ui-kit.ps1`。PowerShell の呼び出しごとにシェルが新しくなるので、毎回ドットで読み込む：

```powershell
. "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"
```

| 関数 | 何をするか |
|---|---|
| `Save-ProductionBaseline` / `Test-ProductionUntouched` | 本番の `settings.json` の更新日時・`items/*.json` の件数・`location.json` を控え、後で照らす |
| `Start-ChmonosApp -Store friendcheck` | `CHMONOS_HOME` を付けて起動する。短い名前は `%LOCALAPPDATA%\BoothAssetManager-<名前>`。**本番と friendtest では起動を断る**。初回の窓は `-Store <空のフルパス> -AllowNew` |
| `Stop-ChmonosApp` | この道具で起動したアプリだけ閉じる（pid と起動時刻を `%TEMP%\chmonos-ui-check.json` に控えてある）。ユーザが開いているアプリは閉じない |
| `New-ChmonosSandbox -From ui -Name xxx` | 写しを作る（`-From prod` は本番を読むだけ）。写しの `location.json` は外す（残すと friendtest が開く）。既にあれば断る |
| `Invoke-ChmonosByName -Name '改変' [-Type Button] [-Index n]` | 名前で探して押す（Invoke・選ぶ・切り替え・開く のうち持っている操作） |
| `Invoke-ChmonosByText -Text 'kip01'` | 文字の親をたどって押す（見出し・カード） |
| `Set-ChmonosText -Like '*から探す' -Value '...'` | 入力欄に入れる |
| `Get-ChmonosTexts [-Like '*件*']` | 見えている文字を並べる（文言が出たかを数える） |
| `Get-ChmonosElements -Type DataItem [-Name/-Like] [-Scope $el]` | 要素を探す |
| `Step-ChmonosScroll [-Times n] [-Index i] [-Up]` | 一覧を送る |
| `Save-ChmonosShot -Name x [-Region x,y,w,h] [-Element $window]` | 窓を撮って `%TEMP%\chmonos-shots\x.png` に置き、パスを返す。Read で開いて見る |
| `Get-ChmonosCenter $el` / `Invoke-ChmonosRealClick -X -Y -UserWasTold` | 実入力（下の決まりを守る） |

座標を目分量で決めない・秒数で待たない（押す位置を絵から読むと外れ、決め打ちの秒数では早すぎて取りこぼす）：

| 関数 | 何をするか |
|---|---|
| `Invoke-ChmonosClick -Name/-Like [-Type Button] [-Element $el] [-Right] -UserWasTold` | 名前で探して、その中心を実クリック。UI Automation の Invoke が効かない部品（クリックでコマンドを呼ぶ切り替えボタン・`ItemsControl` の中・小窓のボタン）はこちら |
| `Invoke-ChmonosMenuItem -Like 'Unityへ送る*' [-Expand] -UserWasTold` | 右クリックや「開く ▾」のメニューの項目（窓の外の別窓に出るので主の窓からは探せない）。`-Expand` は下の段を開くだけ |
| `Wait-ChmonosText -Like '*件*' [-TimeoutSeconds 15]` | 文言が出るまで待つ（出た瞬間に返る。`Start-Sleep` をやめる） |
| `Wait-ChmonosElement -Type Button -Name '送る'` / `Wait-ChmonosDialog [-Like]` | 要素・小窓が出るまで待つ |
| `Get-ChmonosDialog` / `Get-ChmonosDialogText $dialog` | 持ち主付きの小窓（MessageBox・選ぶ窓）と、その中の文字 |
| `Close-ChmonosDialog -Button 'OK' [-Like '題'] -UserWasTold` | 小窓を閉じる。閉じたかを確かめ、駄目ならもう一度押し、最後に Enter |
| `Get-ChmonosTrace [-Kind 知らせ] [-Like '*入っていました*']` / `Wait-ChmonosTrace -Like '…'` | 確かめ用の足跡（下） |

`scripts/unity-kit.ps1`（Unity を相手にするとき。ui-kit を読んだ後にドットで読み込む）：
`Get-UnityEditors` / `Get-UnityWindows [-Title]` / `Wait-UnityImportWindow` / `Save-UnityShot -Window $w -Name x`（隠れていても中身が撮れる）/
`Close-UnityImport -Button Import|Cancel|OK -UserWasTold`（閉じたかを確かめて押し直す。Unity は1回目のクリックが窓を前に出すだけに使われる）。

## 確かめ用の足跡（撮らずに文言を確かめる）

`Start-ChmonosApp` は既定で `CHMONOS_UITRACE=%TEMP%\chmonos-uitrace.log` を付けて起動する（`-NoTrace` で切る）。アプリはこの変数があるときだけ、
出した窓の文言と押されたボタン・選ぶ窓の答え・実行した `UiCommand` と結果・Unity の取り込みの結果を1行ずつ書く（`Core/Services/UiTrace.cs`）。

```powershell
Get-ChmonosTrace -Kind 知らせ -Last 5
Wait-ChmonosTrace -Like '*既に全部入っていました*'   # 出るまで待つ
```

Win32 の MessageBox は中身が UI Automation に出ないので、文言の確かめは足跡で行う（撮って読むのは、並びや色を見るときだけ）。

## 流れ

1. ビルド（`dotnet build`）。自分で起動したアプリが開いていると実行ファイルが掴まれて失敗するので、先に `Stop-ChmonosApp`。
   ユーザのアプリが開いていて失敗したら、閉じてよい（「メモ:」「報告:」の最中は閉じない。見て判断してもらう質問の間も開いておく）。
   ユーザが「画面はまだ使わないで」と言っているときは、アプリを閉じずに脇へビルドする（`dotnet build BoothAssetManager.App -o <作業用フォルダ>\buildcheck`）。
2. `Save-ProductionBaseline`
3. `Start-ChmonosApp -Store <写し>`。どの写しを使うかは [sandboxes.md](sandboxes.md)（画面ごとの既定・一覧・作り物の作り方）。
4. UI Automation で操作する（ボタンは Invoke、一覧は Scroll、入力欄は Value）。押す位置を絵から目分量で決めない——
   `Invoke-ChmonosClick`・`Invoke-ChmonosMenuItem` で名前から押す。待つときは `Wait-*`（固定の `Start-Sleep` を並べない）。
5. `Save-ChmonosShot` で撮り、Read で見る。見たい所だけ `-Region` で切る（窓全体の画像は重い）。同じ状態を撮り直さない。
   文言が出たかは足跡（`Get-ChmonosTrace`）で確かめる。撮るのは並び・色・大きさを見るときだけ。
6. `Stop-ChmonosApp` → `Test-ProductionUntouched`。結果をユーザへの報告に書く（「本番は変わっていない」まで）。
7. その画面の写しでもう一度起動し、開いたまま渡す（ユーザは直後に自分で触る。「起動してくれ」と言わせない）。
   見て決めてほしいことを聞くときも、開いた状態で聞く。

ユーザが自分で開いたアプリ（環境変数なしの起動＝friendtest）はユーザの物。この道具では閉じない・こちらから書き込む操作をしない。

写しで起動しても、起動時の裏の作業（⑦ 期限の来た商品の取り直し・残りの画像）は BOOTH へ問い合わせる（2026-09-23 に確かめの間に走っていた）。
問い合わせはゲートを通るので決め事には触れないが、何度も起動し直す確かめでは、写しの設定で「使っていない間の取得」（`resumeFetchInBackground`）を切っておく。
確かめで写しに残した変更（動かした属性・足した物）は、元に戻すか報告に書く。

続けて何枚も撮るときは、手順を1本のスクリプト（作業用フォルダ）にして1回で流すと往復が減る。
スクリプトを書く前に `docs/dev/powershell.md` を読む（黙って別の物が動く落とし穴がある）。

## UI Automation のつまずき

- GridView の一覧（リスト表示）の行は `ListItem` ではなく `DataItem`。改変の画面のように1画面に複数の一覧があると、別の一覧の行も混ざる。行の中の固有のボタン（「Unity ▾」など）で絞る
- 仮想化した一覧は画面に見えている行しか数えない。「畳んだら減るはず」を数で確かめると、下の行が見えて逆に増える。畳んだ中の行が在るか無いかで見る
- 画面の外の要素は四角が空か無限大になる。座標に使う前に `Get-ChmonosCenter` を通す
- 持ち主付きの窓（`ShowDialog` の窓・`MessageBox`）は主の窓の子として出る（デスクトップの直下には無い）。撮るときは `-Element` に Window の要素を渡す
- モーダルの `MessageBox` が開くと、以降の `SendKeys` が全部詰まる（ボタンは `WM_COMMAND` で押せる）
- `SendMessage` 系の合成クリックは WPF に届かない
- GridSplitter を継いだ部品は、クラス名が `GridSplitter` のまま出る

## 実入力（mouse_event）

UI Automation で届かない所（カードのクリック・ホバー・境目のドラッグ）だけ。

- **使う前にユーザへ告げる。**`-UserWasTold` はその確認で、付けないと動かない
- 座標は `Get-ChmonosCenter` から取る。関数は、アプリが前面にない・座標が窓の外・上に別の窓がある ときは送らずに止まる
  （UI Automation で要素が見つからず四角が空になり、画面の左上＝デスクトップを押し・ドラッグし・ダブルクリックした事故がある。2026-09-14）
- ホバーの見た目・ドラッグは実入力でも確かめきれないことが多い。そのときは「実際のマウスでの確認が要る」と報告に書き、確かめたふりをしない
- 窓が最大化されていると、窓の四角は (-8,-8) から始まる
- ポップアップ・メニュー・ツールチップは窓の外の別の窓に出るので、窓を撮っても写らない。デスクトップごと撮る（`CopyFromScreen`）
- `ItemsControl` の中の部品は UI Automation に出ないことが多い（タグの管理の小分類の行など）。そこは実入力で押す
  （素の `ItemsControl` は中身を隠してしまう。画面側を `Controls/ContentItemsControl` に替えると出る。検索のカードは 2026-09-20 にそうした）
- Alt を単独で押さない（`keybd_event` で前面に出すときの小細工など）。離した瞬間に窓のシステムメニューが開き、以降のキーが飲まれる。前面に出すのは `[ChmonosWin]::Bring`
- 小窓が開いているときに主の窓を前に出すと、小窓がその下に隠れて押せなくなる（`Invoke-ChmonosRealClick` は小窓の方を前に出す）

## Unity

Unity を実際に動かす確かめ（改変の「Unity ▾」・「Unityで選択」）は、ユーザが「試して」と言ったときだけ（2026-09-15）。
Unity が手前に出て画面を横取りするので、ユーザが見ていられるときに行う。報告には「Unity での確かめは未（言われたら行う）」と書く。

## 友人のデータ

友人のデータの写し（[sandboxes.md](sandboxes.md) に挙げた物）の画像には第三者の商品名・絵が写る。**撮った画像はどこへも送らない。**文書・コミット・ログには数と傾向だけ書く（CLAUDE.md の「友人のデータは第三者のもの」）。
撮った物は `%TEMP%\chmonos-shots` に置き、リポジトリに入れない。
