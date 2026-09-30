---
name: ui-check
description: Chmonos の画面を確かめる（写しの保存先で起動し、UI Automation で操作し、PrintWindow で撮って見て、本番が変わっていないかを照らす）。画面・レイアウト・文言を直したあと、ユーザに「確かめて」「起動して」「〇〇のデータで見れますか」と言われたとき、画面の不具合を調べるときに使う。
---

# 画面の確かめ

**UI Automation は「要素があるか」しか答えない。「どこにあるか」は答えない。**レイアウトを壊しても気付けないので、**必ず画像で見る**。

## 窓を出さずに描く（見た目だけを確かめるときは、まずこちら）

`tools/ViewShot` は、アプリを起動せずに、画面や部品を作り物のデータで組んで PNG に描く。窓を出さないので画面を占有せず、何本でも並べて走らせられる。
アプリと同じ XAML・色の表・ViewModel で描く（保存先は回ごとの作業用フォルダ、通信は止めてある。本番にも BOOTH にも触れない）。

| 確かめたいこと | 使う物 |
|---|---|
| 並び・折り返し・色・札・文言の入り方・明るい色と暗い色・幅や倍率を変えたとき | **この台**（1場面 2〜4秒。18場面×明暗で約15秒） |
| 見た目を変えない直し（名前を足す・作りを替える）で、本当に変わっていないこと | **この台の `diff`**（直す前と後に描いて比べる） |
| 押したときの動き・画面の移り方・入力・速さ・実際のデータでの見え方 | アプリを起動（下の「流れ」） |

```powershell
dotnet run --project tools/ViewShot -- list                                   # 場面の一覧
dotnet run --project tools/ViewShot -- shot resolve-broken-zip --theme both    # 1場面を明るい色と暗い色で
dotnet run --project tools/ViewShot -- shot item-files-states --width 1280,1440,1600   # 幅を変えて
dotnet run --project tools/ViewShot -- shot search-cards --scale 1,1.5 --zoom 125      # Windows の拡大率と、設定の「表示の大きさ」
dotnet run --project tools/ViewShot -- shot --all --theme both --out $env:TEMP\chmonos-shots\before   # 全部
dotnet run --project tools/ViewShot -- diff $env:TEMP\chmonos-shots\before $env:TEMP\chmonos-shots\after --out $env:TEMP\chmonos-shots\diff
```

- 画像は既定で `%TEMP%\chmonos-shots\view\<場面>-<light|dark>[-w幅][-s倍率][-z大きさ].png`。結果の行にパスが出るので Read で見る
- 場面は「見たい所」で切り出して出す（窓全体は重い）。全体は `--full`、範囲を決めるなら `--crop x,y,幅,高さ`（DIP）
- `diff` は違う画素の数・違う所の範囲・色の差の最大を言い、`--out` に前・後・違う所（赤）を並べた画像を書く。同じなら終了コード 0。
  台の描画は同じ入力なら画素まで同じになる（2回描いて34枚とも一致）ので、1画素でも違えば見た目が変わっている
- **名前・型・押せるかの直し（見た目が変わらない物）は `peers <場面>`**：読み上げ・自動操作の窓口の木を文字で書き出す。直す前と後で書き出して、文字の差で比べる
  （行は「型（部品の型）「名前」 #ID [押す・切り替え…]」。画面の外・隠している部品には印が付く。メニューや吹き出しは出ない）
- 名前に `first-frame` の付く場面は、最初の配置だけで描いた1コマ（後から足す物を待たない。絵は灰色のまま）。色・幅の組み合わせは回せない
- アプリを開いたままのとき（実行ファイルが掴まれてビルドが落ちる）は、脇へビルドして使う：
  `dotnet build tools/ViewShot -o <作業用フォルダ>\viewshot` → `<作業用フォルダ>\viewshot\ViewShot.exe shot …`
- 結果に「知らせの窓が出ようとした」「アプリのログに n 行」「落ち着かなかった」「見たい所が見つからなかった」が出たら、場面が思った状態になっていない

**場面を足す**：`tools/ViewShot/Scenes.*.cs` に書いて登録する（書き方は `Scenes.cs` の冒頭）。作り物のデータ（`Fake`）を保存先に書き、
`context.StartAsync()` で ViewModel を組み、目当ての画面へ移って、ViewModel の値で状態を作る。外から作れない状態（取り込みの結果・一時展開の進み具合）は
`Backdoor.cs` に入れ方を足す。直す画面に場面が無ければ、直す前に場面を足す（次に同じ所を直す人も使える）。

**この台で確かめられない物**（アプリを起動して確かめる）：

- 動く物・押して出る物：乗せたときの色と吹き出し・右クリックやプルダウンのメニュー（別の窓に出る）・アニメーション・長さの無い進み具合の棒・フォーカスの枠と Tab の動き・ドラッグ
- Windows が描く物：窓の題の帯と枠・ファイルやフォルダを選ぶ窓。起動の瞬間（白い地）も対象外
- 一覧を流している最中の様子（ちらつき・遅れて出る絵）。流した先の並びは、場面で送る位置を決めれば描ける
- 文字の縁：台はグレースケールで縁をぼかす。実際の画面は ClearType なので、字の太さの印象が少し違う
- この PC の物を読む所：改変の画面の「Unityプロジェクト」の段（Unity Hub の一覧）・Unity／VCC／ALCOM のボタン（入っているかで変わる）。
  日付から決まる所（カレンダーの「今日」）は、日が変わると `diff` に出る
- 主の窓を祖先に探す結び付きと `Window.GetWindow` に頼る所は効かない（中身を窓から外して描くため）。
  今は1か所：ナビを畳んだときの項目の見た目（名前は幅で切れて見えないが、件数の点が出ない）
- 絵は裏で読む。台は「描いた画像が0.4秒変わらなくなるまで」待つので、待ちきれない物は無いはずだが、絵が灰色のままなら「落ち着かなかった」が出ていないかを見る

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
5. 操作は **AutomationId で**（`Invoke-ChmonosById`・`Set-ChmonosToggleById`・`Invoke-ChmonosMenuById`。画面に出ている ID は `Get-ChmonosIds`）。
   ID の無い物だけ名前で（`Invoke-ChmonosByName`）。まとまった操作は `Open-ChmonosItem`・`Invoke-ChmonosImport`。待つのは `Wait-*`（固定の `Start-Sleep` を並べない）。
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
| この画面で何を ID で探せるか | `Get-ChmonosIds`（ID・型・名前・個数） |
| ID の部品を押す／開閉を決まった状態にする／文を読む | `Invoke-ChmonosById -Id ItemCardFavorite -Like '商品名*'` ／ `Set-ChmonosToggleById -Id ItemFilesToggle` ／ `Get-ChmonosTextById SearchResultSummary` |
| メニューの項目を押す | `Invoke-ChmonosMenuById -Menu ItemFileOpenMenu -MenuLike 'a.zip*' -Item ItemFileOpenMenu.Unpack` |
| 商品ページを開く | `Open-ChmonosItem -Id 90000003`（検索に `id:` を入れてカードを押す） |
| フォルダを取り込んで、終わるまで待ち、結果を読む | `Invoke-ChmonosImport -Path <フォルダ>`（戻りの `Lines`・`Trace`） |
| 小窓のボタンを押す | `Close-ChmonosDialog -Button 'いいえ' -Like '監視*'`（戻りが「閉じた:」で始まるかを見る） |
| 表示の色を変えて起動する／戻す | `Set-ChmonosTheme -Store x -Theme dark` ／ `Restore-ChmonosTheme -Store x` |
| 検索の条件を足す | `Add-ChmonosSearchCondition -Kind BrokenZip`（足した条件の部品は `SearchModule.BrokenZip.*`） |
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
| 探す・押す（`Invoke-ChmonosById`・`Invoke-ChmonosMenuById`・`Invoke-ChmonosByName`・`ByText`・`Invoke-ChmonosElement`・`Set-ChmonosText`・`Step-ChmonosScroll`） | `Invoke-ChmonosRealClick`・`Invoke-ChmonosClick` |
| 待つ（`Wait-*`）・小窓（`Get-ChmonosDialog`・`Close-ChmonosDialog`） | `Invoke-ChmonosMenuItem`（押す方。`-Expand` は使える）・`Close-ChmonosDialog -RealClick`・`Select-ChmonosSuggestion` |
| 撮る（`Save-ChmonosShot`・`Save-ChmonosShotAround`）・足跡 | `Show-ChmonosFront`・キー送り（`SendKeys`） |
| `ui-ops.ps1` の操作（商品を開く・取り込む・色・条件・フォルダを選ぶ窓） | `Measure-ChmonosLaunch`・`Measure-ChmonosFrames`（画面から直に撮る） |
| 作り物のファイル・写しの控えと戻し（自分の写し） | デスクトップごと撮る（ポップアップ・ツールチップ）・Unity を動かす確かめ |

並行で使える部品でも、**メニューとポップアップ**は、ほかのアプリが前に出ると閉じる。開けなかったら、もう一度開く。
ホバー・キーボードの焦点・ドラッグの見た目は、並行では確かめられない。**速さとメモリの数字は、ほかのアプリが動いていると動く**ので、1本だけで測る。
