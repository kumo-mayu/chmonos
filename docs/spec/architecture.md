# 作りの決め事（今の決め事）

> **要点**：画面から処理への道は、**書き込みと BOOTH への問い合わせ**なら `UiCommand` → `CommandHandler` の switch 1本。読むだけなら画面から直に呼んでよい。
> 商品の `local` は持ち主を宣言して `SaveLocalAsync`、書き手が複数いるファイルは錠つきの `UpdateAsync`。ディスクを見るのは画面のスレッドの外。
> 画面は同じウィンドウの中の差し替えで、戻るは主画面が持つ履歴で行う。
>
> **コード**：`Core/Commands/UiCommand.cs`・`CommandHandler`、`Core/Storage/ItemRepository`・`JsonStore`（`JsonFileStore.UpdateAsync`）・`DataStore`・`AppPaths`、`Core/Models/LocalFields.cs`（`LocalOwners`）、
> `Core/Services/SettingsService`・`DiskCheck`・`PathText`・`ItemOrder`、`App/AppServiceContainer`・`App/FireAndForget.cs`・`App/ViewModels/MainViewModel*.cs`
>
> **経緯**：`docs/history/tech-debt-2026-09-14.md`（3-1 で決め事を書き直した・4-1 の分け方）、`docs/history/import-concurrency.md` §2（持ち主）、`docs/history/grill-2-ui-and-data-model.md`（画面と安全性）

## 画面と処理の境目

- **`UiCommand` を通すのは、書き込みと BOOTH への問い合わせ**（ユーザ判断 2026-09-14）。新しい書き込みの操作は `UiCommand` に足す。読むだけの呼び出しは画面から直に呼んでよい。
- `CommandHandler` は入口で優先度「人が押した」を張る（`docs/spec/background-and-network.md`）。別の道を作ると、優先順位や記録が漏れる（手動の検出が漏れていた例がある）。
- 起動時の裏の作業は人の操作ではないので `UiCommand` を通さない。
- 結果は `CommandResult`（`Counted`・`SettingsChanged`・`ImageFetched` など）。「できなかった」は画面が理由を出す。
- アバターの登録簿の書き込みは `IAvatarRegistryEditor`（検出を差し替える試験の作り物に要らない操作を持たせない）。

## 書き込み

| 何を | どう書く | 持ち主 |
|---|---|---|
| 商品の `local` | `ItemRepository.SaveLocalAsync(itemId, local, owns, booth?)`：`owns` に名指しした項目だけを、直前に読み直した最新へ重ねる。存在しなければ書かずに飛ばす | `LocalOwners` に宣言（編集画面・対応アバター・取り込み・検出・再取得・ファイルの種類・星・画像…） |
| 新しい商品 | `Items.SaveAsync`（丸ごと）は**新しく作るときだけ** | — |
| 設定 | `UiCommand.ChangeSettings(変え方の関数)` → 錠の中でディスクの今の設定に当てる。持つのは `SettingsService.Current` だけで、画面は写しを持たない | 設定画面・取り込み元を足す所など |
| 画面の状態 | `UiCommand.ChangeUiState`（`ui-state.json`。ナビ・絞り込み欄・積んだ条件・窓の位置・画面の幅） | 各画面 |
| 検索の履歴 | `UiCommand.ChangeSearchHistory` | 検索・設定 |
| 未確定・除外・登録簿 | `JsonFileStore.UpdateAsync`（錠の中で読んで直して書く）。1件ずつ足し引きする | 取り込み・未確定の画面・アバター |

- 同じ項目に書き手が2人いるときは、マージの規則を持つ（対応アバター：`Manual` を残す）か、同じ式で計算し直す（`Purchases.ExistsOnBooth` は書く側が毎回 `Booth.Variations` から）。
- 書き込みは一時ファイルに書いてから置き換える。**計算で出せる値は書かない**（`[JsonIgnore]`）。
- 公開前は古い形のデータに合わせる欄・見分け・救済を足さない（合わないデータの方を報告する）。
- データの形は `docs/spec/data-model.md`。

## スレッドとディスク

- 利用者のファイル・フォルダ・Unity プロジェクト・取り込み元・ボリュームが在るかは、**画面のスレッドの外で見る**（`DiskCheck`・投げない）。行を先に出して後から印を付ける／読み込むときに裏で見る／押したときに裏で見る、のどれか。
  落ちているネットワークドライブは1回に数秒かかる。アプリ自身の保存先の中は対象外。
- zip を開く・unitypackage を読む・木を組むのも裏。
- 待たない作業は `〜.Forget()`（失敗をログへ）。`_ = 〜Async()` は書かない。
- パスの比較は `PathText.Same`（大文字小文字を無視）。

## 画面

- **タブを作らない**。同じウィンドウの中で差し替え、開くのは常に1つ。
- **戻る**：主画面が画面の履歴を1本持つ（最大50）。離れる画面を「名前と開き直す手順」として積み、画面そのものは持たない（画像や一覧を握ったままになるため）。ナビで移っても切らない。同じ商品の開き直しは積まずに差し替える。消えた商品は飛ばす。
  文言は全画面「← {行き先}に戻る」。Alt+← とマウスの戻るボタンはどの画面でも効く（編集画面では前の1件へ）。
- 検索の画面の状態はセッション中保持（ViewModel をアプリの間1つ持ち回る）。
- 大きな画面のクラスは関心ごとの `partial` のファイルに分けてある（`SearchViewModel.History.cs` など）。**クラスとしては1つのまま**（状態は分けていない）。画面に並べる行の型は `*Rows.cs`。
- 画面に依らない決まりは Core へ切り出して試験を付ける（`ItemOrder`・`UnresolvedMerge`・`VolumeTable` など）。画面側（App）の試験は作らない。
  切り出しの候補：検索の `Matches`、改変の画面の左の一覧の組み立て、編集画面の種類分けの推し当て。
- 別の画面を組み込む形（フォルダビューに商品ページ・未確定、改変の画面に改変の詳細）は `IsEmbedded` で戻るや境目を隠す。

## 起動と保存先

- 保存先は `BOOTH_ASSET_MANAGER_HOME` ＞ 設定した場所 ＞ 既定。サービスは起動時に保存先を受け取るので、引っ越しは再起動で効く。取り込み中は引っ越せない。
- 同時に1つしか起動しない（保存先の中のロック）。
- 初回（`settings.json` が無いとき）は別の窓で保存先と「画像を保存するか」だけ聞く（3つ以上聞かない）。本体はその後に組み立てる。
- 設定は保存した直後から効く（サービスは設定を抱えず、使うたびに今の値を読む）。例外は保存先・起動時に始まる裏の取得・サムネイルの保持上限。
- バックアップ：1つの zip に書き出す（画像を含めるかは選ぶ）。戻すときは別の空の場所に展開して、次の起動からそこを使う。計算し直せる索引・書きかけ・保存先の場所を覚えるファイルは入れない。

## 試験とビルド

- `dotnet build`・`dotnet test BoothAssetManager.Core.Tests`（1,200件超）。**通信する試験を既定の一式に入れない**（`experiments/` の実行ファイルで行う）。
- 実験のプロジェクトはソリューションに入れない（ファイルは `experiments/` に残る）。
- 改行は LF（`.gitattributes` の `* text=auto eol=lf`）。ビルドの警告は0件を保つ。
