# Codex の点検（読むだけ）2026-11-03

v1.2.0 の前に足した物（検索のスイッチ「開いているUnity」・Unity へ送るときの Windows の通知・unitypackage の複数選び・表示順の引き継ぎ・「すべて畳む／広げる」）と、前の回の直し（`dcacac1a`）の点検。直すかはユーザが決める。

- 指示書：`docs/dev/external-audit.md` と harness の共通の雛形（read）を元に、範囲を `fe7777ab` の後の変更に絞った
- コマンド：`codex exec -s read-only -C <リポジトリ> -o <作業用>/codex-review-3.md -`
- codex-cli 0.160.1・gpt-6.1-sol。点検したコミット `37ee3ffb`

## まとめ役の確かめ

- リポジトリは変わっていない（`git status` 0件）
- 「中」は当たるコードを読んで指摘どおりと確かめた（`CommitPickedAsync` は1行ずつ `AddModificationMember` を出し、結果の `Failed` も見ない。`AddMemberAsync` は末尾へ無条件に足す）
- 「軽い」の道（窓に戻る → `RefreshProjectStatus` → 0件なら全件をもう1回照らす）もコードのとおり。重さは測っていない
- 第三者のデータ・購入物に由来する名前は報告に無い

## 対応

| 指摘 | 対応（ユーザが決める） |
|---|---|
| 「改変に追加して送る」で複数を記録する途中で保存に失敗すると、一部だけ残り、黙って送らず、やり直すと重複する | |
| スイッチで0件の間、窓に戻るたびに0件の理由を見るための全件の照らしを繰り返す | |

以下、Codex の報告そのまま。

---
**新規の指摘は「中」1件、「軽い」1件です。今回読んだ範囲では、v1.2.0の公開を止めるべき「致命」「重い」の問題は確認できませんでした。複数記録の途中失敗は、公開前の修正を勧めます。**

| 重大度 | 要約 |
|---|---|
| 中 | 複数パッケージの記録が途中で失敗すると、一部だけ残り、説明なく送信が中断する。再操作で重複する |
| 軽い | 検索が0件の間、結果に変化がなくても、窓へ戻るたびに案内用の全件照合を繰り返す |

`dcacac1a` の2件は、修正経路を確認しました。

- **確認不能なフォルダ**：`ProjectFolderState` の実装は `DiskCheck.FolderState`。アクセス拒否・一般のIO障害は `Unknown` となり、`ScanProjectAsync` は控えの結果を残して理由を表示します。試験 `フォルダを確かめられないときは_無いと言わず前回の結果を残す` も追加されています。
- **古い調査による控えの上書き**：調査から戻った後、`UnityScans.Save` の直前に取り消しを確認しています。取り消し例外は保存せず戻り、画面への反映にも参照比較があります。試験 `読み直しの後に終わった古い調べは_新しい結果の控えを上書きしない` が逆順完了を扱っています。確認直後から保存までの隙間は、了承済みの範囲として指摘しません。

### [中] 複数記録の途中失敗で一部だけ残り、再操作すると重複する

- 場所：[ItemUnityActions.cs:312](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemUnityActions.cs:312)、[同:346](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/ItemUnityActions.cs:346)、[ModificationService.cs:285](D:/work/ClaudeCode/chmonos/Chmonos.Core/Services/ModificationService.cs:285)、[ModificationRepository.cs:165](D:/work/ClaudeCode/chmonos/Chmonos.Core/Storage/ModificationRepository.cs:165)、[SearchViewModel.cs:143](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.cs:143)、[FireAndForget.cs:42](D:/work/ClaudeCode/chmonos/Chmonos.App/FireAndForget.cs:42)
- 条件：カードから「改変に追加して送る」で2件以上選ぶ。1件目の記録が成功した後、2件目の保存時にドライブ切断・容量不足・ファイルへのアクセス拒否などが起きる。復旧後、同じ選択で操作をやり直す。
- 起きること：最初の操作では一部の記録だけ残り、Unityへの送信は始まりません。画面には途中失敗や記録済み件数が出ません。やり直すと、先に記録された物も再び追加されます。
- 根拠：`CommitPickedAsync` は、各行を別々の `UiCommand.AddModificationMember` で保存します。改変ごとの錠も各更新で解放され、複数行全体をまとめた確定にはなっていません。途中の保存例外は呼び出し元まで抜けるため、後続の `SendManyAsync` と結果表示には到達せず、カードの入口の `.Forget()` がログへ残すだけです。`AddMemberAsync` は末尾へ無条件に追加するので、再操作では先の成功分も重複します。命令が `Failed` を返した場合も、このループは結果を確認していません。
- 確かさ：確か。保存失敗の実機再現は未実施です。
- 試験：**試験の穴**。`PickPackagesDialogTests.カードから送るときは_選ぶ窓で選んだ物を全部返す` は選択結果まで、`選んだ2つ以上を送れなかったら_1回の警告の窓で物と理由を並べる` は送信失敗を扱います。複数記録の2件目だけが保存失敗する場合は確認できませんでした。
- 直し方の方向：複数行を1つの `UiCommand` に渡し、改変の錠の中で一度に追記・保存します。例外と `Failed` を受け、記録できなければ理由を表示して送信を止めます。2件目相当の失敗後も記録が部分的に増えない試験を足します。

### [軽い] 0件の案内のための全件照合が、変化のない窓への復帰でも繰り返される

- 場所：[SearchViewModel.UnityContents.cs:145](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:145)、[同:317](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:317)、[同:455](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:455)、[SearchViewModel.Filtering.cs:89](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.Filtering.cs:89)、[SearchFilterPass.cs:40](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchFilterPass.cs:40)、[MainViewModel.cs:620](D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/MainViewModel.cs:620)
- 条件：「開いているUnity」をオンにし、調査完了後の検索結果を0件にする。Unityや条件を変えず、別の窓とChmonosを行き来する。多数の商品や複雑な検索条件があるほど処理量が増えます。
- 起きること：窓へ戻るたび、案内を再計算するため全商品を画面のスレッドで照合します。0件のまま絞り直す場合には、同じ案内用の照合が1回の `ApplyFilters` 内で2回走る経路もあります。
- 根拠：復帰 → `RefreshOpenProjectsAsync` → 開いている物が前回と同じでも `RefreshProjectStatus` → `OpenUnityEmptyHint` → `EmptyBecauseOfOpenUnity` → `SearchFilterPass.Run`。最後の処理は全商品を列挙し、条件別件数用の一覧まで作ります。また、`ApplyFilters` 内の `EnsureProjectContents` が末尾で `RefreshProjectStatus` を呼び、その後96行でも案内を計算します。前の結果も今回も0件で調査済みなら、両方が全件照合へ進みます。
- 確かさ：確か。繰り返す経路を確認しました。処理時間と体感上の停止時間は未計測なので、フリーズとは断定しません。
- 試験：**試験の穴**。`SearchUnityContentsTests` は0件の案内文を確認していますが、状態不変の復帰時や、1回の絞り込み中の照合回数は確認していません。
- 直し方の方向：案内用の判定は条件・商品・調査結果が変わったときだけ更新します。`RefreshProjectStatus` の進捗表示更新から全件照合を分離し、存在するかだけを調べる判定では最初の一致で終了できる形にします。

通知の守りの確認、`ui-state.json` の他項目を保持する更新、まとめて開閉した後の `Lines` の再構築と操作可否の更新については、読んだ経路から新規の破損を確定できませんでした。

「確かめていない範囲」は次のとおりです。

- ビルド・試験の実行、新しい試験の作成は行っていません。既存試験はコードを読みました。
- WPF・Unity・通知領域の実操作、実際の通信障害・保存障害、速度の計測は行っていません。
- 禁止された保存先・第三者データ・購入物は、一覧取得を含めて触れていません。
- リポジトリの変更、外部通信、アプリ起動は行っていません。終了時の `git status --short` は空でした。