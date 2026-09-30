---
name: ui-check
description: Chmonos の画面を確かめる（写しの保存先で起動し、UI Automation で操作し、PrintWindow で撮って見て、本番が変わっていないかを照らす）。画面・レイアウト・文言を直したあと、ユーザに「確かめて」「起動して」「〇〇のデータで見れますか」と言われたとき、画面の不具合を調べるときに使う。
---

# 画面の確かめ

**UI Automation は「要素があるか」しか答えない。「どこにあるか」は答えない。**レイアウトを壊しても気付けないので、**必ず画像で見る**。

## 始める前に読むのはここまで

確かめの時間の多くは、確かめそのものではなく準備と手探りに消えていた（2026-09-30 は1日で4〜5時間）。
**台本を作業用フォルダに書き直す前に、下の部品を探す。**無ければ `scripts/` に足す（作業用フォルダに置くと、次の人がまた書く）。

```powershell
. "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-kit.ps1"   # 起動・探す・押す・待つ・撮る・足跡・写し
. "D:\work\ClaudeCode\booth-asset-manager\.claude\skills\ui-check\scripts\ui-ops.ps1"   # 商品を開く・取り込む・結果を読む・色・連続で撮る
Use-ChmonosStore tagcheck        # このシェルの相手（シェルは呼び出しごとに新しくなるので、毎回）
```

1. ビルド（`dotnet build`）。自分のアプリが実行ファイルを掴んでいたら、先に `Stop-ChmonosApp`。
   ユーザが「画面はまだ使わないで」と言っていたら、閉じずに脇へビルドして `Start-ChmonosApp -Exe` で起動する
   （`dotnet build BoothAssetManager.App -o <作業用フォルダ>\buildcheck`）。
2. `Save-ProductionBaseline`
3. 写しを選ぶ（[sandboxes.md](sandboxes.md)）。書き込む確かめなら、先に `Backup-ChmonosSandbox -Store <写し>`。
4. `Start-ChmonosApp -Store <写し>`（本番と friendtest は断る）
5. 操作は名前で（`Invoke-ChmonosByName`・`Open-ChmonosItem`・`Invoke-ChmonosImport`）。待つのは `Wait-*`（固定の `Start-Sleep` を並べない）。
   文言が出たかは足跡（`Get-ChmonosTrace`）で確かめる。撮るのは並び・色・大きさを見るときだけ。
6. `Save-ChmonosShot`（`-Region` か `Save-ChmonosShotAround` で見たい所だけ）→ Read で見る。同じ状態を撮り直さない。
7. `Stop-ChmonosApp` → `Restore-ChmonosSandbox -Store <写し> -Done` → `Test-ProductionUntouched`。結果を報告に書く（「本番は変わっていない」まで）。
8. 画面を直したときは、その画面の写しでもう一度起動し、開いたまま渡す。見て決めてほしいことを聞くときも、開いた状態で聞く。

続けて何枚も撮るときは、手順を1本のスクリプト（作業用フォルダの自分の名前のフォルダ）にして1回で流す。
書く前に `docs/dev/powershell.md`（黙って別の物が動く落とし穴）。

## 部品の置き場

| 何をしたいか | どこ |
|---|---|
| 関数の一覧と引数・並行で使えるか | [tools.md](tools.md) |
| どの写しを使うか・写しを台本から作る・作り物のファイル | [sandboxes.md](sandboxes.md) |
| UI Automation で見つからない・押せない・実入力を使う・Unity | [pitfalls.md](pitfalls.md) |
| 速さ・固まり・メモリ | `perf-measure` スキル（`perf-kit.ps1`） |

よく使う物だけ：

| したいこと | 関数 |
|---|---|
| 商品ページを開く | `Open-ChmonosItem -Id 90000003`（検索に `id:` を入れてカードを押す） |
| フォルダを取り込んで、終わるまで待ち、結果を読む | `Invoke-ChmonosImport -Path <フォルダ>`（戻りの `Lines`・`Trace`） |
| 小窓のボタンを押す | `Close-ChmonosDialog -Button 'いいえ' -Like '監視*'`（戻りが「閉じた:」で始まるかを見る） |
| 表示の色を変えて起動する／戻す | `Set-ChmonosTheme -Store x -Theme dark` ／ `Restore-ChmonosTheme -Store x` |
| 検索の条件を足す | `Add-ChmonosSearchCondition -Like '*ファイルの場所*'` |
| 起動の瞬間の白いコマを数える | `Measure-ChmonosLaunch -Store x -Theme dark` |
| 大きな zip・件数の多い zip・壊れた zip・読めないフォルダを作る | `fixtures.ps1`（`New-ChmonosFixtureZip` ほか） |
| 写しを控えて、終わったら戻す | `Backup-ChmonosSandbox` ／ `Restore-ChmonosSandbox` |
| 写しを作り物のデータで作る | `New-ChmonosSandbox -Name x -Recipe movecheck` |
| 前の起動の足跡を読む | `Get-ChmonosTrace -Saved <写しの名前>`（起動のたびに控えへ移る） |

## 守ること

- **本番と friendtest を開かない・書かない。**ユーザが自分で開いたアプリ（環境変数なしの起動＝friendtest）はユーザの物。この道具では閉じない・操作しない。
- **実入力（`mouse_event`）は、使う前にユーザへ告げる。**UI Automation で届かない所だけ（[pitfalls.md](pitfalls.md)）。
- **写しに残した変更は、戻すか報告に書く。**`Backup-ChmonosSandbox` → `Restore-ChmonosSandbox` を使えば、戻したことと一致が確かめられる。
- **友人のデータの写しの画像・名前・ID を、どこへも送らない・文書やコミットに書かない。**数と傾向だけ書く（CLAUDE.md「友人のデータは第三者のもの」）。
  撮った物は `%TEMP%\chmonos-shots` に置き、リポジトリに入れない。
- **Unity を実際に動かすのは、ユーザが「試して」と言ったときだけ。**報告には「Unity での確かめは未（言われたら行う）」と書く。
- **写しで起動しても BOOTH へ問い合わせる**（起動時の裏の作業：期限の来た商品・残りの画像）。何度も起動し直す確かめでは、
  裏の取得を切った写しを使う（[sandboxes.md](sandboxes.md) の「裏の取得」の列。台本から作った写しは切ってある）。

## 並行で確かめるとき

アプリの二重起動の止め方は**保存先ごと**（`<保存先>\app.lock`）なので、**写しが違えば同時に何本でも起動できる**。
道具も、起動したアプリを写しごとに控える（`%TEMP%\chmonos-ui-check\<写し>.json`）。決まりは4つ。

1. **道具を読み込んだら `Use-ChmonosStore <自分の写し>`。**複数開いていて相手を決めていないと、部品は断る
   （黙って最後の物を相手にすると、別の担当のアプリを操作する・閉じる）。1本しか開いていなければ、決めなくても前と同じに動く。
2. **同じ写しを2人で使わない。**写しが足りなければ、台本か `-From` で自分の写しを作る。
3. **BOOTH への問い合わせは、アプリを何本開いても合わせて1本ずつ・1.5秒以上**（門はアプリ1本ごとにあるので、2本が同時に問い合わせると破る）。
   - 並行で起動できるのは、裏の取得を切った写し（`resumeFetchInBackground: false`・`startImportOnLaunch` が入っていない）だけ。`Start-ChmonosApp` が見て、切れていなければ断る。
   - 並行の間は、BOOTH へ問い合わせる操作をしない：手掛かり（ダウンロード元の記録・zip の中の URL）の付いたファイルの取り込み・商品の取り直し・
     未確定の自動検索・ショップの画像・「足りない情報を取得」・対応アバターの検出。**要る確かめは、ほかのアプリが閉じているときに1本だけで行う。**
   - 道具が見ているのは、この道具で起動したアプリだけ。ユーザが自分で開いているアプリの通信は数えていない。
4. **実入力と、画面から直に撮る部品は1人ずつ。**マウス・前面の窓・画面の絵は PC に1つしか無い。
   ほかのアプリが開いている間は、`Lock-ChmonosScreen`（既定 10 分）で画面を取った人だけが使える。終わったら `Unlock-ChmonosScreen`。

| 並行で使える（UI Automation・窓へのメッセージ・PrintWindow） | 並行で使えない（画面を取ってから） |
|---|---|
| 探す・押す（`Invoke-ChmonosByName`・`ByText`・`Invoke-ChmonosElement`・`Set-ChmonosText`・`Step-ChmonosScroll`） | `Invoke-ChmonosRealClick`・`Invoke-ChmonosClick` |
| 待つ（`Wait-*`）・小窓（`Get-ChmonosDialog`・`Close-ChmonosDialog`） | `Invoke-ChmonosMenuItem`（押す方。`-Expand` は使える）・`Close-ChmonosDialog -RealClick` |
| 撮る（`Save-ChmonosShot`・`Save-ChmonosShotAround`）・足跡 | `Show-ChmonosFront`・キー送り（`SendKeys`） |
| `ui-ops.ps1` の操作（商品を開く・取り込む・色・条件・フォルダを選ぶ窓） | `Measure-ChmonosLaunch`・`Measure-ChmonosFrames`（画面から直に撮る） |
| 作り物のファイル・写しの控えと戻し（自分の写し） | デスクトップごと撮る（ポップアップ・ツールチップ）・Unity を動かす確かめ |

並行で使える部品でも、**メニューとポップアップ**は、ほかのアプリが前に出ると閉じる。開けなかったら、もう一度開く。
ホバー・キーボードの焦点・ドラッグの見た目は、並行では確かめられない。**速さとメモリの数字は、ほかのアプリが動いていると動く**ので、1本だけで測る。
