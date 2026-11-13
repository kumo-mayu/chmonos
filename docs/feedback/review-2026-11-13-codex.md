# Codex の点検（2026-11-13・読むだけ3回）

ユーザ指示「codexの点検に出してください。回数はread3回、attack2回まで。一週間のusageの半分までは使って良いです。」
attack 2回の記録は `review-2026-11-13-codex-attack.md`。対応の表は、両方の分をここにまとめる。

## 指示書と走らせ方

- 指示書：harness の `external-audit` の `templates/read.md` に、`docs/dev/external-audit.md`（この日に G・H と attack の節を足した。commit c5051419）を入れ、回ごとに「今回の範囲」を先頭に足した
  - 1回目：G（ローカル API・プラグイン）と、外のアプリを起こす所の変更
  - 2回目：H（前回より後に入った保存の物）と A
  - 3回目：D の BOOTH の決まり（先読み・取り込みの変更）と E（新しい ViewModel の固まり・黙って止まる道）
- コマンド：`codex exec -s read-only -C <リポジトリ> -o <作業用>/codex-readN.md - < <作業用>/prompt-readN.md`（1・2回目は並行）
- codex-cli 0.160.1・gpt-6.1-sol。点検したコミット：c5051419（範囲は前回の直し b64cfc3a から）
- 使用量：5回（read 3・attack 2）を終えて、5時間の窓 53%・1週間の窓 8%

## まとめ役の確かめ

- 3回ともリポジトリは変わっていない（`git status` が空）
- 本番の保存先は変わっていない（`Test-ProductionUntouched`：settings.json・items 15 件・location.json の指す先も同じ）
- コードを読んで確かめた物（下の表の「確か」の列）：
  - 5：`VpmPackages.ReadCore` は manifest の文字を読めたかだけで `complete` を決め、`ParseManifest` は壊れた JSON で空を返す。attack の試験でも失敗した
  - 7：更新の知らせの ID は商品ごとに `item-updated:{ID}` で固定。既読の後の更新は同じ ID で足され、控えを読む側（`NotificationArchive` の `seen.Add(record.Id)`）は2件目以降を捨てる
  - 10：`ApiClients.Reload` は読んでから `_index` に入れるまでを錠でも世代でも守っていない
  - 11：`ChangeApiClientScope` は `client.Scopes.Contains` を直に呼ぶ（行の表示は `?.` で避けている）
  - 1：`LocalReferences.MapConversion` は `SameRow`（アバター・素体・プロファイルだけを比べる）で最初の行だけを残す
  - 2：`StartPrefetch` は錠の中で走っているかを見るが、`_prefetchRun` に入れるのは錠を出た後。`Imports.Finished` は裏のスレッドからも来る。
    ⑧の譲り（`BoothClient` の `WaitingCount > 0`）は相手の優先度を見ない。**送る前に譲るだけなので、1本ずつ・1.5秒は破らない**（Codex も同じ見立て）
- 読んでいない物（「要確認」）：3・4・8・9・15・16

## 対応の表

ユーザ判断 2026-11-13「おすすめの順で直してよいです」（1・2・5・7〜16 を直す、3・4 は測ってから、6 は記録だけ）。

| 件 | 重さ | 何が起きるか | 出所 | 確か | 対応 |
|---|---|---|---|---|---|
| 1 | 重い | 素体・商品 ID を統合して変換先の行が重なると、人の決定が配列の順で捨てられる（後から選んだ有効な行が消え、外した印が残る） | read3 | 確かめた | 直した（9a4ddcc3。選んだ＞外した＞推した、同じなら統合先） |
| 2 | 重い | 先読みが2本走れ、⑧どうしで門を譲り合って進まない。止めるための取り消しも片方しか残らない | read3 | 道は確かめた・頻度は測っていない | 直した（0cfbba16） |
| 3 | 重い | アバターの管理の「変換」で商品を選ぶたび、全アバターの unitypackage の中身の控えを UI スレッドで読み直す | read3 | 要確認 | 直した（8520d989。控えの印が変わるまで使い回し・一覧を作る裏で先に読む。100体で2回目から約4ms）ユーザ判断「3は直す」 |
| 4 | 重い | ファイルの溜まりで組のチェック・「選択を解除」が、1件ごとに全体を数え直す（件数の二乗） | read3 | 要確認 | 直した（0c257739。数を持ち知らせを1回に。5000個で3ms以下）ユーザ判断「一緒に直しておきましょう」 |
| 5 | 中 | 壊れた `vpm-manifest.json` を読めた扱いにし、「全部外した」履歴や空の改変の控えを保存する（`unity-vpm.md` の「壊れているときは null」とも食い違う） | read2・attack2 | 確かめた・試験3件が失敗 | 直した（0309771f） |
| 6 | 中 | 時計が戻ると、VPM の履歴の今が古い版になり、同じ状態を調べるたびに履歴・改変の控えが増える | read2・attack2 | 試験3件が失敗 | 記録だけ（めったに起きず、履歴が増えるだけ） |
| 7 | 中 | 同じ商品の更新の知らせが控えに2件以上移ると、最初の1件しか読めない | read2 | 確かめた | 直した（0309771f。ID と作った日時の組で見る） |
| 8 | 中 | 受け取っていない古い許可の結果を後から受け取ると、新しい許可や取り消しを上書きできる | read1 | 要確認 | 直した（2698dcad） |
| 9 | 中 | 購読の本文を待つ間に権限を外しても、古い権限で購読し直せる | read1 | 要確認 | 直した（2698dcad） |
| 10 | 中 | 許したソフトの一覧の読み直しが2本重なると、取り消したトークンが次の読み直しまで効く | read1 | 確かめた | 直した（2698dcad。世代で古い読み直しを捨てる） |
| 11 | 中 | 手で `"scopes": null` にしたソフトは、設定でチェックを付けても保存されない（画面にも出ない） | read1 | 確かめた | 直す所なし（読むときに EmptyForNull が空へ直す。守りを試験で固定 2a380750） |
| 12 | 中 | `api-clients.json` に同じ ID の行が2つあると、読み直しが例外になり、片方のトークンで他方の出来事を読める | attack1 | 試験2件が失敗 | 直した（2698dcad。同じ ID の行は両方通さない） |
| 13 | 中 | `api-clients.json` のメニューに知らない条件（`when` の未知の欄）があると、条件が消えて無条件の項目になる | attack1 | 試験が失敗 | 直した（2698dcad） |
| 14 | 中 | 見張りの許しを外しても、外す前に積まれた未受信の出来事は受け取れる | attack1 | 試験が失敗 | 直した（2698dcad。外した種類の出来事は列から消す） |
| 15 | 中 | ファイルの割り振りで保存が途中で失敗すると、何件付いたかを出さず、主画面にも伝えない | read3 | 要確認 | 直した（47f8716b） |
| 16 | 中 | 変換扱いの保存に失敗しても、次の商品へ進む | read3 | 要確認 | 直した（47f8716b） |

10 と 12 は同じ `Reload` の作り（索引を出してから知らせる・世代を見ない）から来る。

## 1回目（G）の Codex の報告

以下、Codex の報告そのまま。

対象は `b64cfc3a..c5051419`。**コード上で確認できた指摘は4件、いずれも「中」**です。致命・重いに該当する新規不具合は、今回読んだ範囲では確認できませんでした。

重い順の要約：

1. **中：古い許可結果を後から受け取ると、新しい許可や取り消しを上書きできる。**
2. **中：購読要求の本文を待っている間に権限を外しても、古い権限で購読を復活できる。**
3. **中：許可一覧の再読み込みが競合すると、取り消したトークンが再び有効になる。**
4. **中：`scopes: null` のソフトは、設定画面で権限を変更しても保存されない。**

以下の「確か」はコード経路による確認です。再現実行はしていません。

### [中] 古い許可結果の受け取りが、新しい許可・取り消しを上書きする

- 場所：[ApiClients.cs:392](Chmonos.Api/ApiClients.cs:392)、[ApiClients.cs:424](Chmonos.Api/ApiClients.cs:424)、[ApiClients.cs:267](Chmonos.Api/ApiClients.cs:267)
- 条件：同じソフトの要求Aを許可し、結果を受け取らずに残す。その後、再試行した要求Bを読み取りだけで許可して結果を受け取る。Bの許可を設定で取り消した後、10分以内にAの結果を受け取る。
- 起きること：Aで許した古い権限のトークンが発行され、取り消したソフトが再び利用可能になる。Bを取り消していなくても、Bのトークン・権限・メニュー宣言がAの記録で置き換わる。
- 根拠：`Submit` がまとめるのは `Pending` の要求だけなので、承認済み・未受領のAと新しいBが共存する。`CollectAsync` はAに残った承認時の権限で記録を作り、`AddAsync` が名前・exe・Originの一致する現在の行を無条件に置き換える。受け取りの錠は要求ごとの二重発行を防ぐが、同じソフトの承認順・取り消しとの順序は確かめない。
- 確かさ：**確か**
- 試験：`ApiClientTests.同じソフトが求め直して許すと_前の行を置き換え_前のトークンは使えない` は順番どおりの受け取りを試験している。**古い承認結果の後着、後の取り消しをまたぐ受け取りは試験の穴。**
- 直し方の方向：同じソフトの新しい承認や取り消しで、以前の未受領の承認を失効させる。結果の受け取り時にも承認の世代を照合し、古い承認で現在の行を置き換えない。

### [中] 権限を外す前に始まった要求から、購読を復活できる

- 場所：[ApiHost.cs:430](Chmonos.Api/ApiHost.cs:430)、[PluginEndpoints.cs:40](Chmonos.Api/PluginEndpoints.cs:40)、[PluginEvents.cs:145](Chmonos.Api/PluginEvents.cs:145)、[PluginEvents.cs:503](Chmonos.Api/PluginEvents.cs:503)
- 条件：見張りを許したソフトが `PUT /v1/plugin/subscriptions` を開始し、本文の送信を途中で待たせる。その間に設定で対象の見張り権限を外し、再読み込みが済んでから本文を送り終える。
- 起きること：外した種類を再び購読できる。次の許可一覧の再読み込みまでに発生した操作が列へ積まれ、権限を外したソフトから取得できる。
- 根拠：`GuardAsync` が要求開始時の `ApiClientRecord` を要求へ添える。入口はその記録を取得してから本文を `await` し、本文到着後も古い `client.Scopes` で検査する。`OnClientsChanged` が一度購読を外しても、後から呼ばれる `Subscribe` は錠の中で現在の権限を照合せず、購読を登録する。`Publish` も登録済みの種類を使い、配信時の権限照合はしない。
- 確かさ：**確か**
- 試験：`PluginEventTests.許しを外すと_購読もその場で止まる` は、購読完了後に権限を外す順序を試験している。**本文受信中に権限を外す順序は試験の穴。**
- 直し方の方向：購読登録の錠の中で、現在のソフトと権限を照合する。入口が保持する古い記録だけで、長く残る購読を作らせない。

### [中] 同時の再読み込みで、取り消したトークンが復活する

- 場所：[ApiClients.cs:135](Chmonos.Api/ApiClients.cs:135)、[ApiClients.cs:167](Chmonos.Api/ApiClients.cs:167)、[ApiServer.cs:106](Chmonos.Api/ApiServer.cs:106)、[SettingsViewModel.Api.cs:364](Chmonos.App/ViewModels/SettingsViewModel.Api.cs:364)
- 条件：見張りなどによる再読み込みAが、取り消し前の一覧を読み終える。その索引作成中に設定から許可を取り消し、再読み込みBが新しい一覧を反映する。その後、Aが索引を代入する。
- 起きること：ディスクには取り消しが保存されているのに、APIでは古いトークンや権限が再び有効になる。次に再読み込みが行われるまで食い違う。
- 根拠：`Reload` は読み込み・索引作成・`_index` の代入を直列化していない。`volatile` は代入の可視性を確保するだけで、古い読み込み結果の後着を防がない。設定からの直接再読み込みは `StoreSync` の処理順にも入らない。保存側の `JsonFileStore.UpdateAsync` の錠は書き手同士を守るが、`Load` と索引の公開はその錠の対象外。
- 確かさ：**確か**
- 試験：`ApiClientTests.取り消したら_そのトークンは使えない` と `控えのファイルが変わったら_見張りの道で読み直す` は、それぞれ単独の再読み込みを試験している。**2本の読み込み結果が逆順で公開される場合は試験の穴。**
- 直し方の方向：読み込みから索引公開までを直列化するか、世代番号で古い結果の公開を拒む。`Changed` の通知は、画面側との待ち合いを避けられる位置から出す。

### [中] `scopes: null` の権限変更が、画面上だけ変わって保存されない

- 場所：[SettingsViewModel.Api.cs:317](Chmonos.App/ViewModels/SettingsViewModel.Api.cs:317)、[SettingsViewModel.Api.cs:493](Chmonos.App/ViewModels/SettingsViewModel.Api.cs:493)、[CommandHandler.cs:382](Chmonos.Core/Commands/CommandHandler.cs:382)、[FireAndForget.cs:42](Chmonos.App/FireAndForget.cs:42)
- 条件：手で修正した `api-clients.json` のソフトの行に `"scopes": null` がある。設定の「つないでいるソフト」を開き、権限のチェックを付ける。
- 起きること：チェックは付くが権限は保存されない。画面に保存失敗の案内が出ず、利用者は許可できたように見える。
- 根拠：行の表示は `record.Scopes?.Contains(...)` でnullを空として扱う。一方、変更処理は `client.Scopes.Contains(...)` を直接呼ぶ。`ChangeApiClients` が補正するのは外側の `Clients` だけなので、変更関数内で例外となり、保存へ進まない。`SaveApiClientsAsync` はこの例外を捕まえず、`Forget` がログへ残すだけで、チェックの差し戻しもしない。
- 確かさ：**確か**
- 試験：`SettingsApiTests.つないでいるソフトは_権限のチェックで書き換え_ほかのソフトとほかの欄は残す` は通常の配列を使う。**nullの権限配列から変更する場合は試験の穴。**
- 直し方の方向：変更時も `Scopes` を空配列へ補正する。保存失敗時はチェックを保存済みの値へ戻し、失敗を画面へ伝える。

**確かめていない範囲**

- ビルド・試験・アプリ起動・通信は行っていません。既存試験はソースを読み、対象条件の有無を確認しました。
- WPFの実操作、Unity・VCC・ALCOMの起動、ポート再利用、ディスク不足・権限拒否・電源断の実地確認はしていません。
- `RunningInstances` と指定された外部アプリ起動の変更からは、新規の確かな指摘を得られませんでした。これらの安全性全体を保証する結果ではありません。
- 指定差分の全分岐の網羅、Hの保存機能全体は確認していません。
- リポジトリの変更はありません。禁止された保存先・第三者データ・購入アセットは、一覧取得も含めて触っていません。

## 2回目（H・A）の Codex の報告

以下、Codex の報告そのまま。

点検範囲は `b64cfc3a..c5051419` です。**新規の指摘は「中」3件**です。いずれもコードの経路を確認しました。実行による再現確認はしていません。

| 重大度 | 要約 |
|---|---|
| 中 | 同じ商品の別々の更新通知が、保管庫では重複扱いされ、最初の1件しか読めない |
| 中 | 壊れたVPM manifestを読み取り成功と扱い、不完全な状態を履歴・改変に保存する |
| 中 | 時計が戻るとVPMの最新状態を取り違え、同じ読み取りでも履歴が増え続ける |

### [中] 同じ商品の更新履歴が、保管庫では最初の1件しか読めない

- 場所：[BoothChangeNotes.cs:38](Chmonos.Core/Services/BoothChangeNotes.cs:38)、[NotificationService.cs:617](Chmonos.Core/Services/NotificationService.cs:617)、[NotificationArchive.cs:201](Chmonos.Core/Storage/NotificationArchive.cs:201)、[NotificationArchivePages.cs:132](Chmonos.Core/Storage/NotificationArchivePages.cs:132)
- 条件：同じ月に、同じ商品の更新通知を既読にした後、もう一度更新を受ける。それぞれの通知が保持件数の上限を超えて保管庫へ移る。
- 起きること：別々の更新内容でも、保管庫の一覧・読み出しには最初の通知しか出ません。後の通知も圧縮ファイル内には残りますが、通常の操作では取り出せません。
- 根拠：`NoteChangesAsync` は更新通知に商品ごと固定のIDを使います。既読の通知には重ねず、新しい通知を同じIDで追加します。`PruneAsync` はそれぞれを保管して通常の通知から外します。一方、`CollectAsync` と `CollectEntriesAsync` は、IDだけの `HashSet` で後続行を捨てます。これは保存途中の中断による再追記と、別の機会の更新通知を区別できません。錠は追記を直列にするだけで、この取り違えは防ぎません。
- 確かさ：**確か**。
- 試験：`NotificationArchiveTests.同じ知らせが2回足されても_読むと1件` と `NotificationArchivePagesTests.同じ知らせが2回あっても1件で_区切りをまたいでも2回出ない` は同じ通知の再追記を扱います。**同じIDで日時・差分が異なる通知は試験の穴**です。
- 直し方の方向：通知の発生を識別する鍵と、商品単位で未読をまとめる鍵を分けます。同じ通知の再追記だけを除き、別の更新は両方読めるようにします。

### [中] 壊れたVPM manifestを成功扱いし、不完全な状態を保存する

- 場所：[VpmPackages.cs:90](Chmonos.Core/Services/VpmPackages.cs:90)、[同:136](Chmonos.Core/Services/VpmPackages.cs:136)、[UnityScanStore.cs:171](Chmonos.Core/Storage/UnityScanStore.cs:171)、[ModificationService.cs:753](Chmonos.Core/Services/ModificationService.cs:753)
- 条件：以前の読み取りではmanifestに存在したパッケージについて、VCCの入れ替え中などでフォルダが一時的に無くなり、manifestも書きかけになる。その間にプロジェクト調査、または改変への「使ったもの」の記録が走る。
- 起きること：実際には確認できていないパッケージを「外した」と履歴に記録します。改変にも欠けたVPM一覧、場合によっては空の一覧が「記録した時のVPM」として残ります。
- 根拠：`ReadCore` の完全性判定は、manifestの文字列を取得できたかだけを見ます。文字列が壊れたJSONでも `complete` は真です。その後の `ParseManifest` は `JsonException` を受けて空を返し、解析失敗を完全性判定へ伝えません。そのため `TryRead` はnullにならず、`UnityScanStore.Save` は前の状態を保護する分岐を通らず履歴を進めます。`ReadVpmAsync` でもnullでないため以前の控えへの代替が働かず、不完全な一覧を改変へ保存します。
- 確かさ：**確か**。[仕様の「壊れているときはnull」](docs/spec/unity-vpm.md:25)とも食い違います。
- 試験：`VpmPackagesTests.manifestが壊れている_欄の形が違うときも_止まらずに読めた分を返す` は部分的な読み取りだけを確認します。`VpmHistoryTests.package_jsonが在るのに壊れているときは_読めなかったとする` は別ファイルのケースです。**壊れたmanifestで `TryRead` がnullになり、履歴と改変の控えを守る試験がありません。**
- 直し方の方向：manifestの解析結果に成功・失敗を持たせ、完全性判定へ反映します。部分的な一覧を返す `Read` と、保存に使う `TryRead` の判定を分けて維持します。

### [中] 時計が戻ると、VPMの最新状態を取り違えて履歴が増え続ける

- 場所：[UnityScanStore.cs:171](Chmonos.Core/Storage/UnityScanStore.cs:171)、[VpmHistory.cs:59](Chmonos.Core/Services/VpmHistory.cs:59)、[同:112](Chmonos.Core/Services/VpmHistory.cs:112)、[Modification.cs:218](Chmonos.Core/Models/Modification.cs:218)、[ModificationService.cs:732](Chmonos.Core/Services/ModificationService.cs:732)、[ModificationVpmRows.cs:46](Chmonos.App/ViewModels/ModificationVpmRows.cs:46)
- 条件：進んでいたPCの時計で版Aを記録する。その後時計を戻し、版Bへ変更して調べる。時計が以前の記録日時に追い付く前に、同じ状態を繰り返し調べる。
- 起きること：履歴では版Aが最新扱いになり、変化のない版Bの行が調査のたびに増えます。改変のVPM控えにも同じ問題があり、最後に記録した版Bより古い版Aを画面に出し、追加の記録で版Bの控えを繰り返し足します。
- 根拠：`Advance` は配列の末尾を現在状態として比較し、現在の壁時計で新しい行を末尾へ追加します。しかし次の読み取り・保存時の `Clean` は日時順に並べ直すため、時計を戻す前の版Aが末尾へ戻ります。次の調査もA→Bの変化と判定されます。`s_saveLock` は直列化だけで日時の逆転を扱いません。改変側も `RecordedVpm` が日時順に並べ、`WithVpm` と表示がその末尾を使うため同じ取り違えになります。
- 確かさ：**確か**。
- 試験：`VpmHistoryTests.変わらなければ_何度調べても行を足さない` は時計が進む場合です。`ModificationVpmSnapshotTests.同じ中身なら足さず_版が変われば足し_表示名だけの違いでは足さない` もありますが、**時計を戻してから保存・再読込を繰り返す場合は試験の穴**です。
- 直し方の方向：記録した順序と実際の日時を区別し、最新状態を日時の最大値だけで決めないようにします。履歴・改変の両方で、時計を戻した後も同じ状態の保存が増えないことを確認します。

**確かめていない範囲**

- ビルド・既存試験の実行、新しい試験の作成は行っていません。試験についてはソースを読みました。
- 電源断・容量不足・権限拒否・保存競合の実験、WPF・Unityの実操作、通信間隔の実測はしていません。
- 今回はHとAの重点経路を追いました。変更全体の全分岐、通知gzipの復旧処理の全境界、StoreMover・BackupArchiveの既存処理全体は網羅していません。Gは対象外です。
- ファイルの変更・作成・削除、外部通信、アプリ起動は行っていません。禁止された保存先・購入物・第三者データは、一覧取得も含めて触れていません。

## 3回目（D・E）の Codex の報告

以下、Codex の報告そのまま。

**新規の指摘は6件です（重い4件・中2件）。今回追った範囲では、BOOTH の「1本ずつ・1.5秒以上」を破る経路は確認できませんでした。** 対象は `b64cfc3a..c5051419`。コードと既存試験を読んだ結果で、実行による再現・時間計測はしていません。

| 重大度 | 要約 |
|---|---|
| 重い | 素体・商品IDの統合で、変換先に対する人の決定が配列順で捨てられる |
| 重い | 先読みが二重起動でき、同じ優先度どうしで譲り続ける経路がある |
| 重い | 変換画面で商品を選ぶたび、全所有アバターの中身をUIスレッドで読み直す |
| 重い | ファイルの組を一括選択すると、1件ごとに全体を数え直す |
| 中 | ファイルの割り振りの保存例外で、途中結果と主画面への反映が飛ぶ |
| 中 | 変換扱いの保存に失敗しても、次の商品へ進む |

### [重い] 統合で変換先が重なると、人の決定が配列順で捨てられる

- 場所：[LocalReferences.cs:300](Chmonos.Core/Models/LocalReferences.cs:300)、[Conversion.cs:83](Chmonos.Core/Models/Conversion.cs:83)、[AvatarServiceEditing.cs:626](Chmonos.Core/Services/AvatarServiceEditing.cs:626)、[ItemIdChanger.cs:500](Chmonos.Core/Services/ItemIdChanger.cs:500)
- 条件：同じプロファイルについて、素体Aのつなぎ先を「外す」→素体Bを選んで確認済みにする。その後、素体AをBへ統合する。この操作で、配列には「A・外した印」「B・確認済み」の順で行が残ります。
- 起きること：統合後は、先頭の「B・外した印」だけが残り、後から人が選んだ有効なBが消えます。商品IDの参照を統合して同じ行になる場合も同じです。
- 根拠：`ConversionEdits.Choose` は他のつなぎ先の外した行を残して、選んだ行を末尾へ足します。統合は商品の錠内で `LocalReferences.Rewrite` → `MapConversion` を実行します。重複判定の `SameRow` は確認・拒否・向きを比較せず、`MapConversion` は最初の行だけを残します。書き込みの競合がなくても起きます。
- 確かさ：**確か**。D2・D3で参照先を書き換えるようにした後の、重複処理の漏れです。
- 試験：`LocalReferencesTests.MergingTwoBasesFoldsTheDuplicates` は確認済みの統合先が先頭にある順序だけを試しています。逆順、外した行と有効な行、向きが異なる行は**試験の穴**です。
- 直し方の方向：衝突した行の採用規則を配列順から切り離します。統合先の人の決定を保つか、食い違う決定を確認させる形にし、拒否・確認・向きを含む逆順の試験を足します。

### [重い] 先読みが二重起動し、互いに順番を譲り続けられる

- 場所：[StartupWork.Prefetch.cs:85](Chmonos.App/Services/StartupWork.Prefetch.cs:85)、[同:111](Chmonos.App/Services/StartupWork.Prefetch.cs:111)、[BoothClient.cs:903](Chmonos.Core/Booth/BoothClient.cs:903)
- 条件：先読みをオンにし、起動時の⑦と手動の取り込みを並行させる。両方の完了通知による `StartPrefetch` が、実行中タスクを記録する前に交差する。
- 起きること：先読みが2本走れます。両方の問い合わせが門に並ぶと、送信せず互いに譲って並び直す動きを繰り返せます。また、中止用の変数には片方の取り消し元しか残りません。
- 根拠：`StartPrefetch` は119行の錠で実行中か調べますが、タスクを作り、`_prefetchRun` を記録するのは錠を出た後の133～158行です。2呼び出しとも「実行中なし」を通れます。送信側は、待ち手の優先度を見ず `WaitingCount > 0` なら譲るため、同じ⑧の相手にも譲ります。
- 確かさ：**確か**。二重起動と譲り合いを繰り返す実行順をコード上で追跡しました。実際の発生頻度は未計測です。通信間隔の違反を確認した指摘ではありません。
- 試験：`ResolvePrefetchTests` は各開始条件を個別に試しています。`BoothPriorityTests.先回りは間隔を待つ間に並んだ人の操作へ譲る` の相手は人の操作です。同時開始と⑧どうしの待ちは**試験の穴**です。
- 直し方の方向：開始の予約からタスクの記録までを一つの排他区間にします。門を譲る判定も、自分より高い優先度の有効な待ち手に限定します。

### [重い] 変換画面の商品選択で、全所有アバターの中身を同期で読み直す

- 場所：[AvatarsViewModel.Conversion.cs:67](Chmonos.App/ViewModels/AvatarsViewModel.Conversion.cs:67)、[ItemConversionEditor.cs:169](Chmonos.App/ViewModels/ItemConversionEditor.cs:169)、[ConversionScan.cs:83](Chmonos.Core/Services/ConversionScan.cs:83)、[UnityPackagePathStore.cs:76](Chmonos.Core/Storage/UnityPackagePathStore.cs:76)
- 条件：多数のアバターと大きなunitypackageの中身の控えがある状態で、アバターの管理の「変換」を開き、商品を順々に選ぶ。
- 起きること：選択のたびに、UIスレッドが大量のJSON読み取り・復元・全アセット走査を終えるまで入力や描画へ戻れません。
- 根拠：選択変更 → `ConversionEditor.Show` → 推した理由の計算 → `Conversion.FbxIndex` と同期で進みます。`FbxIndex` は手元にある全アバターの対象ファイルについて `packages.Load` を呼びます。これは控えを使い回す関数ではなく、毎回 `JsonStore.Read` でファイルを同期で読んで一覧を作ります。左の一覧作成を裏へ出した処理は、この選択経路には効きません。
- 確かさ：**確か**。UI上の同期処理と反復読みを確認。停止時間は未計測です。
- 試験：`AvatarsConversionTests.推した理由を行の下に出し_推せなかった行には推していない訳を出す` は表示内容を試しています。読み取りスレッド・回数・大きな控えは**試験の穴**です。
- 直し方の方向：FBX索引を裏で作り、商品・登録簿・控えが変わるまで共有します。選択後の計算も裏へ出し、選択が替わった結果は表示しないようにします。

### [重い] ファイルの組の一括チェックが件数の二乗の処理になる

- 場所：[FilePoolRows.cs:159](Chmonos.App/ViewModels/FilePoolRows.cs:159)、[FilePoolViewModel.cs:254](Chmonos.App/ViewModels/FilePoolViewModel.cs:254)、[同:550](Chmonos.App/ViewModels/FilePoolViewModel.cs:550)、[同:596](Chmonos.App/ViewModels/FilePoolViewModel.cs:596)
- 条件：同じ作者名などの頭を持つファイルが数千～数万件ある溜まりで、組のチェックを入れる、または「選択を解除」を押す。
- 起きること：UIスレッドで大量の全件集計が連続し、その間操作できません。畳んだ組のチェックでも通ります。
- 根拠：組の `IsChecked` は各ファイルを1件ずつ変更します。毎回 `OnFileRowChanged` → `OnCheckedChanged` が走り、ボタンの `AttachText`・`AttachHint` などの再評価で、`CheckedCount` が全組・全ファイルを数え直します。組自身も各行からチェック状態の変更を通知します。一括変更をまとめる仕組みがなく、N件の変更でN件の全体集計を繰り返します。
- 確かさ：**確か**。二乗になる処理経路を確認。停止時間は未計測です。
- 試験：`FilePoolWindowTests.名前の頭が同じファイルは組にして畳み_見出しに中のunitypackageを添える` は2件の一括選択を試しています。大量件数と集計回数は**試験の穴**です。
- 直し方の方向：一括変更中は通知と集計を止め、最後に1回だけ更新します。選択数は差分で保持し、表示を聞かれるたびの全件走査も避けます。

### [中] ファイルの割り振りで保存例外が出ると、途中結果を知らせず止まる

- 場所：[FilePoolViewModel.cs:643](Chmonos.App/ViewModels/FilePoolViewModel.cs:643)、[同:708](Chmonos.App/ViewModels/FilePoolViewModel.cs:708)、[LocalFileEditor.cs:435](Chmonos.Core/Services/LocalFileEditor.cs:435)
- 条件：複数ファイルを「付ける」途中で、保存先の容量不足・アクセス拒否などが起きる。「外す」の保存失敗も同じ経路です。
- 起きること：先に成功した分だけ保存され、失敗理由・成功件数が表示されません。主画面への反映通知も飛ぶため、サブ画面と主画面が食い違います。
- 根拠：保存例外は `ChangeLocalAsync` → 命令 → `AttachAsync`／`DetachAsync` へ伝わります。両関数には `_isBusy` を戻す `finally` だけがあり、結果表示・`NoteItemsChangedBySubWindow`・最後の読み直しはその後にあります。例外時はそこへ到達せず、入口の `Forget()` がログへ残して終了します。
- 確かさ：**確か**。`FireAndForget` の仕組み自体ではなく、呼び手の途中結果の処理漏れです。
- 試験：`FilePoolWindowTests` は通常の付け外しと除外による不成立を試しています。2件目の保存例外と、その後の両画面の一致は**試験の穴**です。
- 直し方の方向：保存例外を画面側で受け、成功済み・失敗・未処理を表示します。成否にかかわらず、保存できた商品を主画面へ反映します。

### [中] 変換扱いの保存失敗後も次の商品へ進む

- 場所：[AvatarsViewModel.Conversion.cs:144](Chmonos.App/ViewModels/AvatarsViewModel.Conversion.cs:144)、[同:164](Chmonos.App/ViewModels/AvatarsViewModel.Conversion.cs:164)、[ItemConversionEditor.cs:118](Chmonos.App/ViewModels/ItemConversionEditor.cs:118)
- 条件：「推した通りに決めて次へ」または「プロファイルではない」を押し、書き込みだけが失敗する。容量不足や置き換えの拒否などで、古い記録は読み直せる状態。
- 起きること：保存していない商品を離れ、次の商品が表示されます。失敗の知らせも共通の編集欄に残るため、どの商品を保存できなかったか分かりにくくなります。
- 根拠：`RunConversionCommandAsync` は `Failed` と保存例外を通知へ変換し、呼び手には正常完了の `Task` を返します。`DecideSuggestedAsync` と `MarkNotProfileAsync` は成功か判断できず、必ず `SelectNextUndecided` を呼びます。待っている間に人が別の商品を選んだ場合も、その選択からさらに次へ進みます。
- 確かさ：**確か**。
- 試験：`AvatarsConversionTests` の「推した通りに決めると…」「プロファイルではないにすると…」は成功時だけです。保存失敗と、保存待ち中の選択変更は**試験の穴**です。
- 直し方の方向：保存結果を呼び手へ返し、成功時だけ次へ進めます。操作開始時の商品が現在も選ばれていることも確認します。

### 確かめていない範囲

- ビルド・試験・本体・実験用プログラムは実行していません。新しい試験も作成していません。
- 実時間での通信間隔、複数プロセスの実動作、UI停止時間、保存失敗の実機再現は未確認です。
- 指定されたViewModel・部品は濃淡を付けて読みました。特に `ManageViewModelBase`・`MemberFilePicking`・通知保管画面・貼り付け登録・`DeferredSection`・`TextMenu` の全分岐は網羅していません。
- G・H全体、およびA～C・Fの既存機能全体は再点検していません。
- 禁止された保存先・第三者データ・購入アセットは開いていません。リポジトリの変更、コミット、外部通信も行っていません。
