# Codex の点検（読むだけ）2026-11-03・2回目

前の回（`review-2026-11-03-codex.md`）の後に足した物：その回の直し2件・取り込み画面の監視対象の「新しいファイルを取り込む」・「改変に追加」の改変の複数選び。直すかはユーザが決める。

- 指示書：前の回の指示書の範囲を `37ee3ffb..HEAD` に替え、11-03 に決めた事を「既に分かっていること」に足した
- コマンド：`codex exec -s read-only -C <リポジトリ> -o <作業用>/codex-review-4.md -`
- codex-cli 0.160.1・gpt-6.1-sol。点検したコミット `2094b8ce`

## まとめ役の確かめ

- リポジトリは変わっていない（`git status` 0件）
- 「中」3件は当たるコードを読んで指摘どおりと確かめた
  - 取り込み対象の一覧（`Folders`）は取り込みが終わっても空にならず、`AddDroppedPathsAsync` は一覧全部で始める。新着だけを渡すつもりが、前に残った対象も取り込み直す（落としたときも同じで、コメントの「落とした物の取り込みを頼まれたのであって、全部ではない」と食い違う）
  - カードの「改変に追加」は `AddPickedAsync` が商品ごとに `AddModificationMember` を出し、結果を見ずに数える
  - 商品ページの「改変に追加」は `CommitPickedAsync` を通り、既に入っている商品を除かない
- 第三者のデータ・購入物に由来する名前は報告に無い

## 対応

| 指摘 | 対応（ユーザが決める） |
|---|---|
| 「新しいファイルを取り込む」が、取り込み対象に残った前の対象も取り込み直す | 直す（ユーザ判断 2026-11-04）。ドロップ・「新しいファイルを取り込む」は今回の物だけを取り込む。一覧全部は「開始」を押したときだけ。試験 `ImportWatchedNowTests`（直す前は落ちることを確かめた） |
| カードから複数の改変へ足すとき、途中の失敗を言わない・失敗を成功と言うことがある | 直す（同）。送らない「改変に追加」を1つの手助け `ItemSelectionActions.AddPickedAsync` にまとめ、改変ごとに1回の保存・結果と例外を見る・足せない改変で止めて足せた所までを言う |
| 商品ページから送らずに足すと、既に入っている改変にも同じ商品が2重に入る | 直す（同）。商品ページもカードと同じ手助けを通り、既に入っている改変は飛ばして「既に入っています」と言う。試験 `ModificationCommitPickedTests` |
| 4つ以上選ぶと、絞って見えなくなった選びの名前が「ほか n 件」に省かれる | 直す（同）。窓の中の「選んでいる改変」は名前を全部並べる（結果を伝える文は今のまま縮める） |

以下、Codex の報告そのまま。

---
**確かな指摘は「中」3件、「軽い」1件です。今回読んだ範囲では、公開を止めるべき「致命」「重い」の問題は確認できませんでした。** ただし、対象外の再取り込みと、改変への追加失敗を通知しない経路は、公開前の修正を勧めます。

点検対象は `37ee3ffb..2094b8ce` です。

| 重大度 | 要約 |
|---|---|
| 中 | 「新しいファイルを取り込む」で、対象欄に残った過去のファイル・フォルダも再取り込みする |
| 中 | カードから複数改変へ追加すると、途中失敗を通知せず、失敗を成功と表示する場合もある |
| 中 | 商品ページの「改変に追加」は、既に入っている同じ商品も重複追加する |
| 軽い | 改変を4件以上選ぶと、検索で隠れた選択の名前をすべて確認できない |

前回の修正については、次を確認しました。

- **複数行の一括保存**：`CommitPickedAsync` → `AddModificationMembers` → `AddMembersAsync` → 改変ごとの錠内で最新記録へ追記 → 1回の保存、に変わっています。`Failed` と通常の保存例外を受け、理由を表示して送信を止めます。複数改変の途中失敗も、この経路では成功済みの改変を伝えて停止します。
- **追加日時**：同じ一括追加の中では `now.AddTicks(index)` で日時を分けています。既存行や別の追加呼び出しとの衝突までは検査していません。通常操作で衝突する具体的な再現経路は確定していません。
- **0件時の照合抑制**：状態不変の窓への復帰では全件照合を繰り返さず、絞り込みの最後に案内を計算します。「調べています」からの更新経路も残っています。今回追った経路では、案内が古いままになる新規の問題は確定できませんでした。

### [中] 新着の取り込みが、対象欄に残った過去の対象まで再実行する

- 場所：[ImportViewModel.cs:1093](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ImportViewModel.cs:1093)、[同:979](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ImportViewModel.cs:979)、[同:1321](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ImportViewModel.cs:1321)、[同:1384](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ImportViewModel.cs:1384)、[同:1538](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ImportViewModel.cs:1538)
- 条件：監視していないフォルダを通常の取り込みで処理し、対象欄に残す。そのフォルダへ別のファイルを置く。その後、監視フォルダにも新しいファイルを置き、「新しいファイルを取り込む」を押す。
- 起きること：監視フォルダの新着だけでなく、先のフォルダも再走査・取り込みされます。監視外に置いたファイルまで取り込まれ、表示する新着件数と実際の処理範囲が食い違います。大きなフォルダでは不要な処理も増えます。
- 根拠：`ImportWatchedAsync` は新着を `AddDroppedPaths` に渡します。しかし、同関数は共有の `Folders` に追記してから `StartOrStackAsync` を呼びます。実際の対象は `ResolveUnpackedTargets` の `Folders.ToList()` です。取り込み終了時にも `Folders` は空になりません。`ImportWorkSet` の重複防止は同じ取り込み内だけなので、終了後の次の取り込みには効きません。
- 確かさ：**確か**。対象を渡す経路を確認しました。実行再現はしていません。
- 試験：**試験の穴**。`ImportWatchedNowTests.開いた後に置いたファイルを_取り込みに積んで始める` は、過去の対象が残っていない状態です。
- 直し方の方向：この操作の要求には、今回発見した新着だけを渡します。表示用の対象一覧と、今回実行する対象を分けます。過去の対象フォルダを残した試験を追加します。

### [中] カードから複数改変へ追加する経路は、途中失敗を正しく伝えない

- 場所：[ItemSelectionActions.cs:250](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemSelectionActions.cs:250)、[同:274](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemSelectionActions.cs:274)、[CommandHandler.cs:1456](D:/work/ClaudeCode/chmonos/Chmonos.Core/Commands/CommandHandler.cs:1456)、[FireAndForget.cs:42](D:/work/ClaudeCode/chmonos/Chmonos.App/FireAndForget.cs:42)
- 条件：カードの「改変に追加」で複数の改変を選ぶ。先の改変への保存成功後、後の改変で容量不足・アクセス拒否などが起きる。または、選択後に後の改変が削除される。
- 起きること：保存例外では、一部だけ記録されたまま結果の通知が出ません。改変が見つからず `Failed` が返った場合は、追加できていないのに「追加しました」と表示して次へ進みます。
- 根拠：新しい改変ごとのループは `AddPickedAsync` を呼びます。同関数は商品ごとに `AddModificationMember` を実行し、戻り値を見ずに `added++` します。改変ごとの錠は各命令で解放されます。例外はループを抜け、最後の結果表示にも到達せず、入口の `.Forget()` がログへ残すだけです。修正済みの `CommitPickedAsync` は通りません。
- 確かさ：**確か**。例外と `Failed` の両方の行き先を確認しました。
- 試験：**試験の穴**。`ModificationCommitPickedTests.二つ目の改変に足せなければ_そこで止め_足せた所までを言う` は別経路です。`ModificationMemberOrderAndFilesTests.まとめて足すときは_商品ごとに選んだ物が入り_選べるファイルの無い商品は行を出さない` は正常時だけです。
- 直し方の方向：カードの追加も、改変ごとに一括保存し、例外・`Failed` を確認します。失敗したら停止し、成功済みと未処理の範囲を知らせます。既存商品の重複除外は残します。

### [中] 商品ページから送らずに追加すると、同じ商品が重複する

- 場所：[ItemViewModel.Unity.cs:102](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemViewModel.Unity.cs:102)、[ItemUnityActions.cs:378](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemUnityActions.cs:378)、[ModificationService.cs:313](D:/work/ClaudeCode/chmonos/Chmonos.Core/Services/ModificationService.cs:313)
- 条件：ある商品が既に改変Aに入っている。商品ページの「改変に追加」でAと、まだ入っていない改変Bを選ぶ。同じファイルを選んだまま確定する。
- 起きること：Bへの追加に加えて、Aにも同じ商品・同じファイルの行がもう1つ増えます。送らない追加では既存商品を重ねない、という決め事に反します。
- 根拠：商品ページは `CommitPickedAsync(..., sending: false)` を呼びます。`sending` は失敗時の文言にしか使われません。各改変へ `AddModificationMembers` を渡し、サービスは最新の `Members` の末尾へ無条件に追記します。カード側にある `UsedMembers` による既存判定もありません。
- 確かさ：**確か**。同じ版を繰り返し記録する仕様一般ではなく、送らない追加の重複除外が欠けている指摘です。単一選択時から存在した経路が、今回の複数選択にも引き継がれています。
- 試験：**試験の穴**。`ModificationCommitPickedTests` は空の改変への追加を扱い、既存商品を含む複数改変への追加を確認していません。
- 直し方の方向：送らない追加では、改変の錠内で現在の使用商品を確認し、既存商品を除外します。追加件数と既存のため飛ばした件数を改変ごとに伝えます。

### [軽い] 4件以上選ぶと、隠れた選択の名前が省略される

- 場所：[PickModificationDialogViewModel.cs:262](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/PickModificationDialogViewModel.cs:262)、[同:267](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/PickModificationDialogViewModel.cs:267)、[ModificationPicking.cs:149](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ModificationPicking.cs:149)
- 条件：検索語を変えながら改変を4件以上選び、後半の選択が一覧から見えなくなる。
- 起きること：ボタン上の表示が先頭2件の名前と「ほか n 件」になり、隠れた選択の名前をすべて確認できません。選択は残るため、省略された改変にも記録されます。
- 根拠：`PickedRows` は非表示の選択も保持します。一方、`PickedText` は結果通知用の `NamesText` を共用し、4件以上では名前を2件に省略します。`ResolvePickedAsync` は省略された物を含む全選択を返します。
- 確かさ：**確か**。選択を保持すること自体は合意どおりで、確認用の名前表示が足りない問題です。
- 試験：**試験の穴**。`改変はいくつでも選べ_絞って見えなくなった選びも残して名前で出す` は2件だけです。別の試験は結果通知用の名前省略を確認しています。
- 直し方の方向：確定前の選択表示と、結果通知の要約を分けます。確定前は全選択の名前を確認できる一覧などを用意します。

「確かめていない範囲」は次のとおりです。

- ビルド・既存試験の実行、新しい試験の作成はしていません。試験についてはコードを読んだ評価です。
- WPF・Unityの実操作、切断・容量不足・権限拒否・処理途中の競合、処理時間の計測はしていません。
- 別の追加呼び出し間での `AddedAt` 衝突は未検証です。現在の試験は同じ一括追加内の日時の違いだけを確認しています。
- 起動時と手動の取り込みが同じ実行に合流する場合、`ImportRunner` と `ImportWorkSet` に二重起動・重複受付を防ぐ経路があります。新しい入口からBOOTHの共通ゲートを迂回する道は確認しませんでしたが、実行による間隔測定はしていません。
- ファイルの変更、外部通信、アプリ起動は禁止どおり行っていません。禁止された保存先・第三者データ・購入物は、一覧取得も含めて触れていません。