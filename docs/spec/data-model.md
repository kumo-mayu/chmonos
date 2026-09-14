# データの形と書き方（今の決め事）

> **要点**：保存するものは全部、人が開いて読めて直せる JSON。商品は1商品1ファイルで `booth`（取ってきた物）と `local`（人と取り込みが決めた物）に分ける。
> 書くときは「自分が持つ項目」だけを、読み直した最新に重ねる。計算で出せる値は書かない。公開前なので古い形のデータに合わせる変更はしない。
>
> **コード**：`Core/Models/`（`ItemRecord`・`LocalBlock`・`LocalFields.cs`・`AppSettings`・`UiState`）、`Core/Storage/`（`DataStore`・`ItemRepository`・`JsonStore`・`AppPaths`）
>
> **経緯**：`docs/history/grill-2-ui-and-data-model.md`（ファイル構成・item・LocalFile）、`docs/history/import-concurrency.md` §2（持ち主）、`docs/history/tech-debt-2026-09-14.md` 1-1〜1-5・3-2

## 保存先

既定は `%LOCALAPPDATA%\BoothAssetManager`。環境変数 `BOOTH_ASSET_MANAGER_HOME` ＞ 設定した場所 ＞ 既定（`StoreLocation`）。多重起動はロックファイルで止める。

| ファイル | 中身 | 書き手 |
|---|---|---|
| `items/{id}.json` | 商品1件（`booth` ＋ `local`） | 取り込み・再取得・編集・検出など（持ち主の宣言つき） |
| `items/{id}.h2.html` | 表示用の説明HTML（商品ページを開いたときだけ読む） | 取り込み・再取得 |
| `images/{id}/{URLのハッシュ}.webp` | 画像（長辺384・WebP）。`{ハッシュ}.missing` は404で取れなかった印 | 画像の取得 |
| `settings.json` | 設定（設定画面で選ぶ物） | `UiCommand.ChangeSettings` だけ |
| `ui-state.json` | 画面が覚えている状態（ナビ・絞り込み欄の畳み方・積んだ条件・窓の位置・画面の幅 `paneWidths`） | `UiCommand.ChangeUiState` だけ |
| `unresolved.json` | 商品が決まっていないファイル | 取り込み（`UnresolvedMerge`）と未確定の画面の操作（錠つき） |
| `excluded.json` | 管理から外したファイル（パスとハッシュ両方） | 錠つき |
| `avatar-registry.json` | アバターと共通素体の登録簿 | 錠つき |
| `userTags.json` / `attributes.json` | ユーザタグ・属性のマスタ（名前・メモ・並び） | タグ・属性の管理 |
| `modifications/{id}.json` | 改変1件 | 改変の画面 |
| `notifications.json` | 要確認（既読の印つき。上限を超えたら古い既読から捨てる） | 再取得・検出 |
| `search-history.json` | 検索の履歴 | `UiCommand.ChangeSearchHistory` |
| `recent.json` | 「最近」の足跡（追加・使った・閲覧） | 取り込み・Unityへ送る・商品ページ |
| `shop-banners.json` | ショップのバナーを調べた記録 | ショップの画面 |
| `video-titles.json` | YouTube の動画のタイトルの控え（動画ID・題・取った日時）。30日を過ぎたら取り直すか消す | 商品ページの動画の欄・起動時の整理 |
| `scan-cache.json` | パス → サイズ・更新日時・ハッシュ（消してもよい） | 取り込み |
| `import-state.json` | 中断した取り込みの3項目（済んだ数・全体・日時）。最後まで終われば消す | 取り込み |
| `edit-session.json` | 編集キューの位置 | 編集画面 |
| `volumes.json` | ドライブ文字と通し番号の組（`docs/spec/folder-view.md`） | 取り込み・フォルダビュー |
| `unitypackages/{zipのハッシュ}.json` | zip の中の unitypackage のパス一覧（中身から決まるので古くならない。消してもよい） | 取り込みの裏 |
| `logs/app.log` | 失敗の書き残し（1MBで `app.old.log` へ回す。消してよい） | `AppLog` |

## 商品（item）

- **1商品1ファイル**。全件を1ファイルにまとめない（壊れたときの被害・差分・手で直す）。
- **`booth` と `local` を分ける**。再取得は `booth` を差し替えるだけで、人の入力を壊しようがない。
- **説明は2つに分ける**：JSON には検索・差分用の `h2Sections`（見出しと本文のテキスト）、表示用の HTML は別ファイル。
- **購入記録（`Purchases`）は自立した記録**：種類のID・買ったときの名前・価格・ギフトかどうか。BOOTH から種類が消えても消さず、`ExistsOnBooth=false` にする（支出には数える）。
  `ExistsOnBooth` は書く側が毎回 `Booth.Variations` から計算し直す（編集画面と再取得が同じ式で書くので食い違わない）。
- **価格とギフトは種類ごと**。商品の購入額はその合計。統計の時系列は**入手日**（購入日という欄は作らない。無ければファイルの日付）。
- BOOTH に無い商品は仮ID（`local-…`）で作る（`docs/spec/private-items.md`）。

## 手元のファイル（`LocalFiles`）

- **同一性はハッシュ**。1件が `paths[]` を持つ（同じ zip が2か所なら1件に2パス。移したらパスが差し替わる。版違いは別の件）。
- **外した印 `Detached`**：「この商品のものではない」と外した行は消さずに残す（次の取り込みで同じ商品へ戻らないようにするため）。所持には数えない。
- **容量は2つ**：商品ページは論理値（重複を1回）、統計のディスク使用量は実占有（重複コピーも数える。内訳に1行）。
- `unityPackages`：zip の中の unitypackage の入る先の要約（取り込みの裏で1回だけ読む。`docs/spec/unity.md`）。
- フォルダごと登録した商品は `LocalFolders`（中身は記録しない）。

## 参照の持ち方

ユーザタグと属性は**名前**で参照（改名・統合はアプリが全商品を書き換える）。アバターは**BOOTHの商品ID**で参照（実在する安定したキー）。
属性の値（0〜100）は商品の側に持つ。共通素体は登録簿の関係として持ち、商品の対応アバターは出品者の宣言のまま触らない。

## 書き方

1. **商品の `local` は `ItemRepository.SaveLocalAsync(itemId, local, owns)` で書く。**`owns` に名指しした項目だけを、保存の直前に読み直した最新へ重ねる。
   持ち主は `LocalOwners` に宣言（編集画面＝タグ・属性・メモ・購入・入手日など／取り込み＝`LocalFiles` `LocalFolders`／検出＝`Avatars` `AvatarBases` …）。
   `Items.SaveAsync`（丸ごと）は**新しく作るときだけ**。商品番号の付け替えも、既にある先へは `SaveLocalAsync`。
2. **書き手が複数いるファイルは `JsonFileStore.UpdateAsync`（錠の中で読んで直して書く）。**画面の写しを丸ごと書き戻さない（取り込み元・未確定が消えていた）。
3. **設定・画面の状態・検索の履歴は、変え方を関数で渡す**（`UiCommand.ChangeSettings` など）。持つのは `SettingsService` だけで、画面は写しを持たない。
4. 書き込みは一時ファイル（`〜.tmp`）に書いてから置き換える。起動時に10分より古い `.tmp` を消す。
5. 保存の直前に商品がまだあるかを見て、無ければ書かずに飛ばす（塞がない）。
6. **計算で出せる値は書かない**（`[JsonIgnore]`）。
7. **公開前は古い形のデータに合わせる変更をしない**。合わない物は「どの保存先の何件がどう合わないか」を報告して直し方を聞く。

## 数え方（全画面で同じ）

- **所持**＝手元のファイルかフォルダを1つ以上持つ。ファイルの無い商品は検索には出る（印なし）が、統計・ショップの件数・支出には入れない。
- **非表示**：検索とショップの件数から外し、統計には入れる（見えなくしたいだけで、持っていないことにはしない）。
- **R-18を表示しない設定**：検索とショップの件数から外し、統計の金額には入れる。要確認には出す。
- **所有アバター**＝登録簿のIDが所持している商品に含まれるか（手で「持っている」にもできる）。フラグとしては持たない。
