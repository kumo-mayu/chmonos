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
| `Start-ChmonosApp -Store x [-Exe <別の版>] [-AllowNew] [-NoTrace] [-IsolateTemp] [-SettleSeconds 6]` | `CHMONOS_HOME` を付けて起動。短い名前は `%LOCALAPPDATA%\Chmonos-sandboxes\<名前>`。**本番と friendtest は断る。**同じ写しが開いていれば断る。ほかのアプリが開いているときは、裏の取得を切った写しだけ | ○ |
| `Use-ChmonosStore x` | このシェルの相手を決める（並行のときは、読み込んだ直後に毎回） | ○ |
| `Stop-ChmonosApp [-Store x]` | この道具で起動したアプリを閉じる。複数開いていて相手を決めていなければ、閉じずにそう返す | ○ |
| `Get-ChmonosApp [-Store]` / `Get-ChmonosRoot [-Store]` | プロセス／主の窓の要素 | ○ |
| `Get-ChmonosRunning` | この道具で起動して、今開いているアプリの一覧 | ○ |
| `Get-ChmonosAppTemp [-Store]` | アプリの一時フォルダ（一時的に展開した物 `Chmonos\unpacked` の親）。並行で起動したアプリは写しごとに分かれる | ○ |
| `Lock-ChmonosScreen [-Store] [-Minutes 10]` / `Unlock-ChmonosScreen` | 画面（実入力・画面から撮る）を取る／返す | ○ |
| `Start-ChmonosSecond [-Store]`（ui-ops） | 同じ写しで2本目を起動し、「既に起動しています」の窓を返す | ○ |

相手の決まり方：`-Store` → `Use-ChmonosStore`（このシェルで `Start-ChmonosApp` した写し）→ 環境変数 `CHMONOS_UI_STORE` → 開いているのが1つならそれ。

## AutomationId で探す・押す（ui-kit。まずこちら）

操作できる部品には AutomationId が付いている（一覧は `docs/spec/ui-input.md`「読み上げの名前」。画面で見るなら `Get-ChmonosIds`）。
名前（文言）や並びで探すと、文言を直しただけで確かめが壊れるので、ID がある物は ID で探す。
行ごとに繰り返す部品は同じ ID が並ぶので、`-Name`／`-Like`（その行の名前）か `-Index` で絞る。

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Get-ChmonosIds [-Like 'Import*']` | 今の画面に出ている ID の一覧（ID・型・個数・押せるか・名前） | ○ |
| `Get-ChmonosById -Id x [-Name/-Like] [-Scope $el]` | ID で探す（`*` を使える。メニューの中・別の窓も見る） | ○ |
| `Wait-ChmonosById -Id x` / `Get-ChmonosTextById x` | 出るまで待つ／名前（＝画面の文）を読む | ○ |
| `Invoke-ChmonosById -Id x [-Like '商品名*'] [-Index n]` | ID で探して押す。戻りは「押した:」「無い:」「押せない:」 | ○ |
| `Set-ChmonosToggleById -Id x [-Off]` | 開閉・チェックを、指した状態にする（押すと切り替わってしまう物に。畳む欄そのもの（Expander）は開閉の操作で開く・閉じる） | ○ |
| `Set-ChmonosValueById -Id x -Value v` | 欄に値を入れる（候補付きの入力欄は中の欄に入れる。**決めるのは `Select-ChmonosSuggestion`**） | ○ |
| `Invoke-ChmonosMenuById -Menu x -Item y [-MenuLike] [-ItemLike] [-OpenOnly]` | メニューを開いて、中の項目を押す（「＋ 条件を追加」の2段・行の［開く ▾］） | ○ |
| `Get-ChmonosWindows` | アプリの窓の全部（主の窓・メニュー・持ち主の無い小窓） | ○ |

## 探す・押す・待つ（ui-kit。ID の無い物）

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Get-ChmonosElements -Type DataItem [-Name/-Like] [-Scope $el] [-Store]` | 要素を探す | ○ |
| `Get-ChmonosTexts [-Like '*件*']` | 見えている文字を並べる | ○ |
| `Invoke-ChmonosByName -Name '改変' [-Type Button] [-Index n] [-WaitSeconds 2]` | 名前で探して押す（Invoke・選ぶ・切り替え・開く のうち持っている操作） | ○ |
| `Invoke-ChmonosByText -Text '…'` | 文字の親をたどって押す（見出し・カード） | ○ |
| `Invoke-ChmonosElement $el` | 要素を、持っている操作で押す | ○ |
| `Set-ChmonosText -Like '*から探す' -Value '…'` | 入力欄に入れる | ○ |
| `Step-ChmonosScroll [-Times n] [-Index i] [-Up]` / `Set-ChmonosScrollPercent -Percent 100`（ui-ops） | 一覧を送る（`-Index` を省くと、縦に流せるいちばん大きい物）／画面の本体を割合で流す | ○ |
| `Wait-ChmonosText -Like` / `Wait-ChmonosElement -Type -Name` / `Wait-ChmonosDialog [-Like]` / `Wait-ChmonosCondition -Until {…}` | 出るまで待つ（出た瞬間に返る。出なければ `$null`） | ○ |
| `Get-ChmonosDialog [-Like]` / `Get-ChmonosDialogText $dialog` | 小窓と、その中の文字 | ○ |
| `Close-ChmonosDialog -Button 'OK' [-Like '題']` | 小窓のボタンを UI Automation で押して閉じる。戻りは「閉じた:…」か「閉じられない:…」（Enter では閉じない） | ○ |
| `Get-ChmonosMenuItem` / `Invoke-ChmonosMenuItem -Like '…' -Expand` | メニューの項目を探す／下の段を開く | ○ |

## 撮る（ui-kit）

| 関数 | 何をするか | 並行 |
|---|---|---|
| `Save-ChmonosShot -Name x [-Region x,y,w,h] [-Element $window] [-Store]` | 窓を撮って `%TEMP%\chmonos-shots\x.png` に置く（前面でなくても撮れる）。`-Name '担当\x'` で下のフォルダに | ○ |
| `Save-ChmonosShotAround -Element $el -Name x [-Pad 20] [-Width] [-Height]` | 要素の周りだけを撮る（窓の見えている範囲に無い部品は断る。先に流す） | ○ |
| `Measure-ChmonosLaunch -Store x [-IntervalMs 35] [-Frames 170] [-Theme dark] [-Exe] [-Close]`（ui-ops） | 起動の瞬間を連続で撮り、白いコマの数・出揃うまでの時間を返す | × |
| `Measure-ChmonosFrames -Action {…} [-Seconds 5]`（ui-ops） | 開いているアプリを、操作の間だけ連続で撮る | × |
| `Get-ChmonosFrameSummary`（ui-ops） | コマの並びから数字を出す（上の2つが使う） | ○ |

## 足跡（ui-kit）

| 関数 | 何をするか |
|---|---|
| `Get-ChmonosTrace [-Kind 知らせ] [-Like] [-Last 40] [-Saved <名前かパス>] [-Store]` | 足跡を読む（`-Saved` は控えた足跡）。アプリを閉じた後は、指した写し（指していなければいちばん新しい物）の足跡を読む。複数開いていて相手を決めていなければ読まない |
| `Wait-ChmonosTrace -Like '…' [-Kind]` | 足跡に出るまで待つ |
| `Save-ChmonosTrace [-Name round1]` / `Get-ChmonosTraceHistory` | 今の足跡を控える／控えの一覧 |

## よく使う操作（ui-ops）

| 関数 | 何をするか | 頼っている ID・名前 |
|---|---|---|
| `Show-ChmonosScreen -Nav '取り込み' [-WaitText]` | ナビで画面を移り、文字が出るまで待つ。小窓が開いたままなら止まる | ナビのボタンの名前 |
| `Open-ChmonosItem -Id <ID>` / `-Name <表示の名前>` `[-KeepFilters]` | 検索に `id:<ID>` を入れ、出たカード（行）を押して商品ページを開き、中身が落ち着くまで待つ。条件で絞られて出ないときは条件をクリアして探し直す。**検索の履歴に1件積まれる** | `QueryBox`・`ItemCard`・`ItemEdit`・`SearchClearFilters` |
| `Get-ChmonosItemName -Id` / `Get-ChmonosItemCards` | 写しの item から表示の名前を読む／検索に出ているカード | `ItemCard` |
| `Set-ChmonosTheme -Store x -Theme dark`（light・dark・system） / `Restore-ChmonosTheme -Store x` | 写しの設定の表示の色を書き換える／元の形へ戻す（**アプリを閉じているとき**） | 設定の `colorTheme` |
| `Add-ChmonosSearchCondition -Kind BrokenZip`（`-Like '*ファイルの場所*'` も） | 検索の「＋ 条件を追加」から条件を足す。足した条件の部品は `SearchModule.<種類>.<部品>`。同じ種類を複数置ける条件（候補から積む物・ユーザータグ）は2つ目から `SearchModule.<種類>-2.<部品>`（番号は上から。足した後の数を待つ）。見出しの「…」は `SearchModule.<種類>.Menu`、そのメニューの行は `.Menu.Exclude`・`.Menu.Collapse`・`.Menu.MoveUp`・`.Menu.MoveDown`・`.Menu.Remove`、除いている札は `.Excluded`。パネルの「…」は `SearchFilterMenu`（行 `SearchFilterMenu.GroupKinds`） | `SearchAddModule`・`SearchAddModule.<種類>` |
| `Select-ChmonosFolder -Path` | Windows のフォルダを選ぶ窓にパスを入れて押す | 窓のクラス `#32770` |
| `Select-ChmonosSaveFile -Path <フルパス>` | バックアップの書き出しでは、保存の後に「画像も入れる／画像は入れない」を聞く窓が出るので `Close-ChmonosDialog -Button '画像も入れる' -Like 'バックアップ*'` で答える。Windows の保存の窓（バックアップの書き出しなど）にフルパスを入れて「保存」を押す。同じ名前が既にあると断る（上書きの確認が出るため） | 窓のクラス `#32770`・ファイル名の Edit の ID `1001` |
| `Add-ChmonosImportFolder -Path [-Watch]` / `Clear-ChmonosImportTargets` | 取り込みの対象に足す（監視を聞かれたら はい／いいえ）／対象を全部外す | `ImportChooseFolder`・題「監視…」・`ImportFolderRemove` |
| `Start-ChmonosImport [-TimeoutSeconds 600] [-During {…}]` | 「開始」を押し、終わるまで待つ。戻りは `Done`・`Seconds`・`Trace`・`Messages`・`Summary`・`Buttons`・`Lines` | `ImportStart`・`ImportCancel`・足跡の `ScanFolders` |
| `Invoke-ChmonosImport -Path a,b [-Watch]` | 上をつないだ物（画面を移る → 対象を外す → 足す → 取り込む → 結果） | — |
| `Get-ChmonosImportResult` | 結果を ID で読む：`Messages`（ID → 文。読めなかった文は `ImportUnreadableLine`、オンラインのみは `ImportOnlineOnlyLine`、壊れた zip は未確定が `ImportBrokenZipLine`・商品が `ImportBrokenZipOnItemsLine`）・`Summary`（`ImportSummary.<名前>` → 数。**その回の数**）・`Buttons`（出ているボタンの ID） | `Import*` |
| `Get-ChmonosOpenDialogNote` | 小窓が開いたままなら、題とボタンを言う文（無ければ `$null`） | — |

どれも実入力を使わない（並行で使える）。

## 実入力（ui-kit。使う前にユーザへ告げる・並行 ×）

| 関数 | 何をするか |
|---|---|
| `Get-ChmonosCenter $el` | 要素の中心（画面の座標）。画面の外・空の四角は投げる |
| `Invoke-ChmonosRealClick -X -Y -UserWasTold [-Right]` | 実クリック |
| `Invoke-ChmonosClick -Name/-Like [-Type Button] [-Element $el] [-Right] -UserWasTold` | 名前で探して、その中心を実クリック |
| `Invoke-ChmonosMenuItem -Like 'Unityへ送る*' -UserWasTold` | メニューの項目を実クリック（ID のある項目は `Invoke-ChmonosMenuById` で足りる） |
| `Select-ChmonosSuggestion -Id SearchModule.Category.Input -Text '3D' [-Pick '3D衣装'] -UserWasTold`（ui-ops） | 候補付きの入力欄に字を入れ、候補を実クリックで決める（候補の行は UI Automation では決まらない） |
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
