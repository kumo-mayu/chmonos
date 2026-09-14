---
name: ui-check
description: Chmonos の画面を確かめる（写しの保存先で起動し、UI Automation で操作し、PrintWindow で撮って見て、本番が変わっていないかを照らす）。画面・レイアウト・文言を直したあと、ユーザに「確かめて」「起動して」「〇〇のデータで見れますか」と言われたとき、画面の不具合を調べるときに使う。
---

# 画面の確かめ

**UI Automation は「要素があるか」しか答えない。「どこにあるか」は答えない。**レイアウトを壊しても気付けないので、**必ず画像で見る**
（`DockPanel` の子は既定で `Dock="Left"` に落ちる、という壊し方を実際にやっている）。

## 道具

`scripts/ui-kit.ps1`。PowerShell の呼び出しごとにシェルが新しくなるので、**毎回ドットで読み込む**：

```powershell
. "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"
```

| 関数 | 何をするか |
|---|---|
| `Save-ProductionBaseline` / `Test-ProductionUntouched` | 本番の `settings.json` の更新日時・`items/*.json` の件数・`location.json` を控え、後で照らす |
| `Start-ChmonosApp -Store friendcheck` | `CHMONOS_HOME` を付けて起動する。短い名前は `%LOCALAPPDATA%\BoothAssetManager-<名前>`。**本番と friendtest では起動を断る**。初回の窓は `-Store <空のフルパス> -AllowNew` |
| `Stop-ChmonosApp` | **この道具で起動したアプリだけ**閉じる（pid と起動時刻を `%TEMP%\chmonos-ui-check.json` に控えてある）。ユーザが開いているアプリは閉じない |
| `New-ChmonosSandbox -From ui -Name xxx` | 写しを作る（`-From prod` は本番を読むだけ）。写しの `location.json` は外す（残すと friendtest が開く）。既にあれば断る |
| `Invoke-ChmonosByName -Name '改変' [-Type Button] [-Index n]` | 名前で探して押す（Invoke・選ぶ・切り替え・開く のうち持っている操作） |
| `Invoke-ChmonosByText -Text 'kip01'` | 文字の親をたどって押す（見出し・カード） |
| `Set-ChmonosText -Like '*から探す' -Value '...'` | 入力欄に入れる |
| `Get-ChmonosTexts [-Like '*件*']` | 見えている文字を並べる（文言が出たかを数える） |
| `Get-ChmonosElements -Type DataItem [-Name/-Like] [-Scope $el]` | 要素を探す |
| `Step-ChmonosScroll [-Times n] [-Index i] [-Up]` | 一覧を送る |
| `Save-ChmonosShot -Name x [-Region x,y,w,h] [-Element $window]` | 窓を撮って `%TEMP%\chmonos-shots\x.png` に置き、パスを返す。**Read で開いて見る** |
| `Get-ChmonosCenter $el` / `Invoke-ChmonosRealClick -X -Y -UserWasTold` | 実入力（下の決まりを守る） |

## 流れ

1. **ビルド**（`dotnet build`）。自分で起動したアプリが開いていると実行ファイルが掴まれて失敗するので、先に `Stop-ChmonosApp`。
   ユーザのアプリが開いていて失敗したら、閉じてよい（「メモ:」「報告:」の最中は閉じない。見て判断してもらう質問の間も開いておく）。
2. `Save-ProductionBaseline`
3. `Start-ChmonosApp -Store <写し>`。どの写しを使うかは CLAUDE.md の表。**書き込む確かめは `friendcheck`（友人のデータ）か `ui`（本番の写し）**。
4. UI Automation で操作する（ボタンは Invoke、一覧は Scroll、入力欄は Value）。
5. `Save-ChmonosShot` で撮り、Read で見る。**見たい所だけ `-Region` で切る**（窓全体の画像は重い）。同じ状態を撮り直さない。
6. `Stop-ChmonosApp` → `Test-ProductionUntouched`。**結果をユーザへの報告に書く**（「本番は変わっていない」まで）。

ユーザが自分で触るための「起動して」は、環境変数を付けない普段の起動でよい（friendtest が開く）。それはユーザのアプリなので、この道具では閉じない・こちらから書き込む操作をしない。

続けて何枚も撮るときは、手順を1本のスクリプト（作業用フォルダ）にして1回で流すと往復が減る。自作の関数に `Where`・`Measure`・`Clear` のような名前を付けない（既にある別名とぶつかり、黙って別の物が動く）。

## UI Automation のつまずき

- **GridView の一覧（リスト表示）の行は `ListItem` ではなく `DataItem`。**改変の画面のように1画面に複数の一覧があると、別の一覧の行も混ざる。行の中の固有のボタン（「Unity ▾」など）で絞る
- **仮想化した一覧は画面に見えている行しか数えない。**「畳んだら減るはず」を数で確かめると、下の行が見えて逆に増える。畳んだ中の行が在るか無いかで見る
- 画面の外の要素は四角が空か無限大になる。**座標に使う前に `Get-ChmonosCenter` を通す**
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

## 友人のデータ

`friendcheck` の画像には第三者の商品名・絵が写る。**撮った画像はどこへも送らない。**文書・コミット・ログには数と傾向だけ書く（CLAUDE.md の 5.）。
撮った物は `%TEMP%\chmonos-shots` に置き、リポジトリに入れない。
