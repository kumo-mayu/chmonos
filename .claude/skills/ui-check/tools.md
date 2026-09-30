# 確かめの道具の一覧

置き場は `.claude/skills/ui-check/scripts/`。PowerShell の呼び出しごとにシェルが新しくなるので、毎回ドットで読み込む。
**並行**の列：○＝ほかの担当と同時に使える／×＝画面（マウス・前面・画面の絵）を使うので、`Lock-ChmonosScreen` で取ってから。

| ファイル | 中身 | 読む順 |
|---|---|---|
| `ui-kit.ps1` | 起動・探す・押す・待つ・撮る・足跡・写し・本番の照らし | 最初 |
| `ui-ops.ps1` | よく使う操作（商品を開く・取り込む・色・条件）・連続で撮る | ui-kit の後 |
| `fixtures.ps1` | 作り物のファイル（アプリを相手にしない。これだけで読める） | どこでも |
| `sandbox-recipes.ps1` | 写しの台本（`New-ChmonosSandbox -Recipe` が読む。直には読まない） | — |
| `unity-kit.ps1` | Unity の窓（[pitfalls.md](pitfalls.md)） | ui-kit の後 |
| `../perf-measure/scripts/perf-kit.ps1` | 固まり・メモリ・絵の比べ（`perf-measure` スキル） | ui-kit の後 |

## 起動と相手（ui-kit）

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Start-ChmonosApp -Store x [-Exe <別の版>] [-AllowNew] [-NoTrace] [-IsolateTemp] [-SettleSeconds 6]` | `CHMONOS_HOME` を付けて起動。短い名前は `%LOCALAPPDATA%\BoothAssetManager-<名前>`。**本番と friendtest は断る。**同じ写しが開いていれば断る。ほかのアプリが開いているときは、裏の取得を切った写しだけ | ○ |
| `Use-ChmonosStore x` | このシェルの相手を決める（並行のときは、読み込んだ直後に毎回） | ○ |
| `Stop-ChmonosApp [-Store x]` | この道具で起動したアプリを閉じる。複数開いていて相手を決めていなければ、閉じずにそう返す | ○ |
| `Get-ChmonosApp [-Store]` / `Get-ChmonosRoot [-Store]` | プロセス／主の窓の要素 | ○ |
| `Get-ChmonosRunning` | この道具で起動して、今開いているアプリの一覧 | ○ |
| `Get-ChmonosAppTemp [-Store]` | アプリの一時フォルダ（一時的に展開した物 `Chmonos\unpacked` の親）。並行で起動したアプリは写しごとに分かれる | ○ |
| `Lock-ChmonosScreen [-Store] [-Minutes 10]` / `Unlock-ChmonosScreen` | 画面（実入力・画面から撮る）を取る／返す | ○ |
| `Start-ChmonosSecond [-Store]`（ui-ops） | 同じ写しで2本目を起動し、「既に起動しています」の窓を返す | ○ |

相手の決まり方：`-Store` → `Use-ChmonosStore`（このシェルで `Start-ChmonosApp` した写し）→ 環境変数 `CHMONOS_UI_STORE` → 開いているのが1つならそれ。

## 探す・押す・待つ（ui-kit）

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Get-ChmonosElements -Type DataItem [-Name/-Like] [-Scope $el] [-Store]` | 要素を探す | ○ |
| `Get-ChmonosTexts [-Like '*件*']` | 見えている文字を並べる | ○ |
| `Invoke-ChmonosByName -Name '改変' [-Type Button] [-Index n] [-WaitSeconds 2]` | 名前で探して押す（Invoke・選ぶ・切り替え・開く のうち持っている操作） | ○ |
| `Invoke-ChmonosByText -Text '…'` | 文字の親をたどって押す（見出し・カード） | ○ |
| `Invoke-ChmonosElement $el` | 要素を、持っている操作で押す | ○ |
| `Set-ChmonosText -Like '*から探す' -Value '…'` | 入力欄に入れる | ○ |
| `Step-ChmonosScroll [-Times n] [-Index i] [-Up]` / `Set-ChmonosScrollPercent -Percent 100`（ui-ops） | 一覧を送る／画面の本体を割合で流す | ○ |
| `Wait-ChmonosText -Like` / `Wait-ChmonosElement -Type -Name` / `Wait-ChmonosDialog [-Like]` / `Wait-ChmonosCondition -Until {…}` | 出るまで待つ（出た瞬間に返る。出なければ `$null`） | ○ |
| `Get-ChmonosDialog [-Like]` / `Get-ChmonosDialogText $dialog` | 小窓と、その中の文字 | ○ |
| `Close-ChmonosDialog -Button 'OK' [-Like '題']` | 小窓のボタンを UI Automation で押して閉じる。戻りは「閉じた:…」か「閉じられない:…」（Enter では閉じない） | ○ |
| `Get-ChmonosMenuItem` / `Invoke-ChmonosMenuItem -Like '…' -Expand` | メニューの項目を探す／下の段を開く | ○ |

## 撮る（ui-kit）

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Save-ChmonosShot -Name x [-Region x,y,w,h] [-Element $window] [-Store]` | 窓を撮って `%TEMP%\chmonos-shots\x.png` に置く（前面でなくても撮れる）。`-Name '担当\x'` で下のフォルダに | ○ |
| `Save-ChmonosShotAround -Element $el -Name x [-Pad 20] [-Width] [-Height]` | 要素の周りだけを撮る | ○ |
| `Measure-ChmonosLaunch -Store x [-IntervalMs 35] [-Frames 170] [-Theme dark] [-Exe] [-Close]`（ui-ops） | 起動の瞬間を連続で撮り、白いコマの数・出揃うまでの時間を返す | × |
| `Measure-ChmonosFrames -Action {…} [-Seconds 5]`（ui-ops） | 開いているアプリを、操作の間だけ連続で撮る | × |
| `Get-ChmonosFrameSummary`（ui-ops） | コマの並びから数字を出す（上の2つが使う） | ○ |

## 足跡（ui-kit）

| 関数 | 何をするか |
|---|---|
| `Get-ChmonosTrace [-Kind 知らせ] [-Like] [-Last 40] [-Saved <名前かパス>] [-Store]` | 足跡を読む（`-Saved` は控えた足跡） |
| `Wait-ChmonosTrace -Like '…' [-Kind]` | 足跡に出るまで待つ |
| `Save-ChmonosTrace [-Name round1]` / `Get-ChmonosTraceHistory` | 今の足跡を控える／控えの一覧 |

## よく使う操作（ui-ops）

| 関数 | 何をするか | 頼っている名前 |
|---|---|---|
| `Show-ChmonosScreen -Nav '取り込み' [-WaitText]` | ナビで画面を移り、文字が出るまで待つ | ナビのボタン |
| `Open-ChmonosItem -Id <ID>` / `-Name <表示の名前>` | 検索に `id:<ID>` を入れ、出たカードを押して商品ページを開く。**検索の履歴に1件積まれる** | 検索欄 `QueryBox`・カード `ItemCard` |
| `Get-ChmonosItemName -Id` / `Get-ChmonosItemCards` | 写しの item から表示の名前を読む／検索に出ているカード | — |
| `Set-ChmonosTheme -Store x -Theme dark`（light・dark・system） / `Restore-ChmonosTheme -Store x` | 写しの設定の表示の色を書き換える／戻す（**アプリを閉じているとき**） | 設定の `colorTheme` |
| `Add-ChmonosSearchCondition -Like '*ファイルの場所*'` | 検索の「＋ 条件を追加」から条件を足す | メニュー「条件を追加」 |
| `Select-ChmonosFolder -Path` | Windows のフォルダを選ぶ窓にパスを入れて押す | 窓のクラス `#32770` |
| `Add-ChmonosImportFolder -Path [-Watch]` / `Clear-ChmonosImportTargets` | 取り込みの対象に足す（監視を聞かれたら はい／いいえ）／対象を全部外す | 「フォルダを選択」・題「監視…」・「対象から外す」 |
| `Start-ChmonosImport [-TimeoutSeconds 600] [-During {…}]` | 「取り込みを開始」を押し、終わるまで待つ。戻りは `Done`・`Seconds`・`Trace`・`Lines` | 「取り込みを開始」・足跡の `ScanFolders` |
| `Invoke-ChmonosImport -Path a,b [-Watch]` | 上をつないだ物（画面を移る → 対象を外す → 足す → 取り込む → 結果） | — |
| `Get-ChmonosImportResult` | 足跡の結果の行と、画面の「結果」より後ろの文 | 文字「結果」（欄に名前が無い） |

どれも実入力を使わない（並行で使える）。

## 実入力（ui-kit。使う前にユーザへ告げる・並行 ×）

| 関数 | 何をするか |
|---|---|
| `Get-ChmonosCenter $el` | 要素の中心（画面の座標）。画面の外・空の四角は投げる |
| `Invoke-ChmonosRealClick -X -Y -UserWasTold [-Right]` | 実クリック |
| `Invoke-ChmonosClick -Name/-Like [-Type Button] [-Element $el] [-Right] -UserWasTold` | 名前で探して、その中心を実クリック |
| `Invoke-ChmonosMenuItem -Like 'Unityへ送る*' -UserWasTold` | メニューの項目を実クリック |
| `Show-ChmonosFront` | 前面に出す |

## 作り物のファイル（fixtures）

置き場は `%LOCALAPPDATA%\Chmonos-fixtures\<組の名前>`（リポジトリの外）。名前は作り物（`sample-…`）、中身は種から作る乱数（同じ引数なら同じ中身）。

| 関数 | 何を作るか |
|---|---|
| `New-ChmonosFixtureZip -Set s -Name a.zip -SizeMB 300 [-Parts 3]` | 圧縮の効かない中身の zip。全体か1個が 4096 MB を超えると Zip64（`-SizeMB 4200` で 22 秒） |
| `New-ChmonosFixtureZip -Set s -Name b.zip -Count 5000` | 小さなファイルが n 個の zip。65,536 個以上で Zip64（7万個で 1.2 秒・39 MB） |
| `New-ChmonosFixtureBrokenZip -Set s -Kind Truncated [-SizeMB 5] [-KeepRatio 0.6]`（`-Kind Garbage` も） | 途中で切った zip／中身がでたらめの zip |
| `New-ChmonosFixtureFolder -Set s -Name loose -Count 20000 [-PerFolder 100] [-Bytes 256]` | 小さなファイルを n 個置いたフォルダ |
| `New-ChmonosFixtureUnityPackage -Set s [-Assets 50] [-SizeMB 5] [-InZip]` | 作り物の unitypackage（`-InZip` で zip の中に1つ） |
| `Set-ChmonosFixtureZone -Path f -ReferrerUrl u [-HostUrl u]` / `Get-ChmonosFixtureZone` | ダウンロード元の記録（Zone.Identifier）を付ける／読む。**付けると、取り込みが BOOTH へ問い合わせ得る** |
| `Deny-ChmonosFixtureRead -Path p` / `Restore-ChmonosFixtureRead -Path p` | 読む権限を外す／戻す（ファイルもフォルダも） |
| `Remove-ChmonosFixtures -Set s` / `Get-ChmonosFixtures` | 組を消す（外した権限は先に戻す）／置いてある組の一覧 |

名前に `次使う\a.zip` のように下のフォルダを付けられる。権限を外す・消すのは置き場の中だけ（外のパスは断る）。
記録の URL は作り物の番号で（`https://booth.pm/ja/items/1234567`）。実在の商品 ID を使わない。

## 写し（ui-kit）

| 関数 | 何をするか |
|---|---|
| `New-ChmonosSandbox -From ui -Name x` | 今ある保存先を写す（`-From prod` は本番を読むだけ） |
| `New-ChmonosSandbox -Name x -Recipe movecheck [-Items n] [-Full]` / `Get-ChmonosRecipes` | 作り物のデータで台本から作る／台本の一覧（[sandboxes.md](sandboxes.md)） |
| `Backup-ChmonosSandbox -Store x [-Name before] [-Force]` | 丸ごと控える（`%LOCALAPPDATA%\Chmonos-sandbox-backups`）。同じ名前の控えがあれば断る |
| `Restore-ChmonosSandbox -Store x [-Name before] [-Done]` | 控えた状態へ戻し、一致を確かめる。`-Done` で控えも消す |
| `Compare-ChmonosSandbox -Store x [-Quick] [-ShowPaths]` | 写しと控えを照らす（既定は中身。`-Quick` は大きさと日時）。違いは数と上のフォルダごと（**ファイル名は `-ShowPaths` のときだけ**） |
| `Get-ChmonosSandboxBackups` | 置いてある控えの一覧 |

控える・戻すのは、アプリがその写しを開いていないときだけ（開いていれば断る）。

## 本番が変わっていないか（ui-kit）

`Save-ProductionBaseline`（確かめの前）→ `Test-ProductionUntouched`（後）。本番の `settings.json` の更新日時・`items/*.json` の件数・`location.json` を照らす。
