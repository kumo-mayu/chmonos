# 外から使う API：URL と答えの形

> **要点**：[local-api.md](local-api.md) の URL と答えの形。URL はユーザが広めの案から削った（2026-10-12）。答えの中身は Chmonos.Query（HTTP を持たない問い合わせ層）が作り、
> **形の見本は期待する答えの試験（`Chmonos.Query.Tests/Golden/`）**。形を変えたら見本を書き直し（`CHMONOS_UPDATE_GOLDEN=1`）、差分を読んでから入れる。
> 「作った」は答えを作る処理と HTTP の口があること（2026-10-12）。使う人向けの説明は `manual/40-api.md`。

## URL

| URL | 中身 | 状態 |
|---|---|---|
| `GET /v1/health`・`GET /v1/version` | 動いているか・読み終えたか・読めなかった商品の数（ID は入れない。トークンなしで答えるので・点検30）／API のバージョン・保存の形の版・通番・まとめて書いている途中か・注意（トークンなし） | 作った |
| `GET /v1/openapi.json` | 仕様書（OpenAPI。コードから作る。トークンなし） | 作った |
| `GET /v1/changes?since=`・`GET /v1/events` | 変化の一覧と SSE | 作った |
| `GET /v1/failed-items` | 読めなかった商品の ID（手で壊した JSON など） | 作った |
| `GET /v1/display-settings` | 使う人の表示の設定（R-18 を表示しない・非表示の件数・消えた画像・サムネイルにする画像） | 作った |
| `GET /v1/items`・`GET /v1/items/{id}`・`POST /v1/items/batch` | 一覧（要約）・1件（詳細）・ID をまとめて詳細 | 作った（`ItemMapper`） |
| `GET /v1/items/{id}/description` | 説明文（見出しごとの文と表示用の HTML） | まだ |
| `GET /v1/items/{id}/modifications`・`/notifications` | その商品を使っている改変・その商品の通知 | 作った |
| `POST /v1/search` | 式で検索（下）。答えは一覧（要約）と全体の数と続きの印 | 作った（`SearchEngine`） |
| `GET /v1/search/conditions` | 条件の種類・値の形・選択肢・切り替えの既定 | 作った |
| `POST /v1/search/facets` | 選択肢の件数（下） | 作った |
| `GET /v1/search/explain?q=` | 検索欄の文字列をどう読んだか | まだ |
| `GET /v1/saved-searches`・`/search-history` | 保存した検索・履歴（式に変換した物も添える） | まだ（保存の形から API の条件へ戻す変換が要る） |
| `GET /v1/avatars`・`/v1/avatars/{id}` | 登録簿（名前・素体・持っているか・対応する商品の数・絵の場所） | 作った |
| `GET /v1/avatar-bases` | 共通素体（属するアバター・呼び方） | 作った |
| `GET /v1/files/lookup?path=`・`?hash=` | ファイル・フォルダから商品を逆に引く | 作った |
| `GET /v1/unitypackages?avatar=` | 手元の `.unitypackage` と、その商品・対応アバター | 作った |
| `GET /v1/folders`・`/v1/unresolved`・`/v1/excluded`・`/v1/volumes` | 登録したフォルダ・商品が決まっていないファイル・外したファイル・ドライブ | まだ |
| `GET /v1/user-tags`・`/v1/attributes`・`/v1/categories`・`/v1/booth-tags` | 分類と件数（マスタに無いのに付いている物も返す） | 作った |
| `GET /v1/shops`・`/v1/shops/{key}` | ショップ（星・メモ・件数） | 作った |
| `GET /v1/modifications`・`/{id}`・`/v1/unity-projects` | 改変・Unity プロジェクト | 作った |
| `GET /v1/notifications` | 通知（既読・片付けた印つき） | 作った（変化の中身はまだ） |
| `GET /v1/recent`・`/v1/stats` | 最近の足跡・統計 | まだ |
| `GET /v1/images/{itemId}/{name}`・`/v1/modifications/{id}/images/{name}` | 画像の本体（ブラウザの画面のため） | 作った |

アバター・素体・ショップから商品を引く専用の URL は作らない（検索の式で書く。ユーザ判断 2026-10-12）。

## 商品の答え（`ItemDetail`・`ItemSummary`）

見本は `Golden/item-*.json`（既定）・`item-9800001-include.json`（全部を頼んだ答え）・`summaries.jsonl`。決まりだけ書く：

- **既定の答えは結果だけ**（ユーザ判断 2026-10-12「使わない情報は落として、聞かれた時だけ返す」）。どう決まったか・人が何を打ち消したかの帳簿と、長くなる一覧は
  `?include=` で頼まれたときだけ返す。**頼まなかった項目は項目ごと出さない**（頼んで中身が無ければ `[]` か null。「頼んでいない」と「無い」を見分けられる）。
  頼んで返すときは中の区別を潰さない（消した紐付けも状態つきで返す）。知らない名前は誤り（`unknownInclude`）。

  | include | 足す物 |
  |---|---|
  | `avatarLinks` | `avatarLinks`：紐付けの出どころ（`supportSection`・`supportList`・`tag`・`variation`・`h2Link`・`manual`）と状態（`confirmed`・`candidate`・`rejected`）、素体の紐付け、検出した日時 |
  | `allFiles` | 外した（`detached`）・上書きで残った古い版（`oldVersion`。`replaced` 付き）のファイル。既定は `present`・`missing` だけ |
  | `contents` | ファイルごとの zip の中の一覧 |
  | `images` | `images`：画像の全部（既定は `thumbnail` だけ） |
  | `meta` | `meta`：BOOTH の情報を最後に取った日時 |

  商品以外：改変（`/v1/modifications`・`/v1/items/{id}/modifications`）の `include=detached` で外した商品を `detached` 付きで返し、
  共通素体の `include=rejected` で人が「素体ではない」とした物を `isRejected` 付きで返す。アバターの「手で持っているにしたか」・バリエーションの BOOTH の中の状態・購入の「BOOTH にまだあるか」は返さない。
  ファイルの逆引き（`files/lookup`）は「このファイルは何か」への答えなので、外した物も状態つきで既定で返す。
- `name` は画面と同じ表示名、`boothName` は BOOTH の名前。`category` は画面と同じ実質の1つで、`categorySource` は `user`・`booth`。
- **対応アバター** `compatibleAvatars` は画面の検索と同じ数え方（人が消した紐付けと説明文のリンクを除き、**人が確認していない推定は数える**。`match` は `direct`・`viaBase`）。
  確認済みだけを対応として返すと、検索の答えと食い違う。一覧の要約の `avatarIds` も同じ物。
- ファイルの `state` は `present`・`missing`（と、頼んだときの `detached`・`oldVersion`）。`unityPackages` が null はまだ調べていない（空は調べて無かった）。中身は送らず場所だけ。
- サムネイルは画面のカードと同じ1枚（設定に従う）。画像の `origin` は `booth`・`user`・`orphaned`、`role` は `booth`・`modified`・`other`。
- 購入の `kind` は `self`・`given`・`received`（保存の JSON の日本語の値ではなく、変えない英字）。

## 検索の式（`POST /v1/search`）

```json
{
  "where": { "and": [ { "text": "夏" }, { "not": { "condition": { "kind": "price", "min": 1000, "max": 2000 } } } ] },
  "includeHidden": true, "includeAdult": true,
  "textOptions": { "targets": ["name", "shop"], "caseSensitive": false },
  "sort": [ { "field": "wishCount", "order": "desc" } ],
  "cursor": null, "limit": 100
}
```

- 式の1段には `and`・`or`・`not`・`text`・`condition` のどれか1つ。深さ32段・葉200・枝も含めた段500まで（葉だけを数えると、空の枝を並べて越えられた・点検30）。
- `text` は検索欄の記法そのまま（前置き・`OR`・`-` も同じ）。**辞書で別表記に広げる処理は v1 では入れない**（画面でも既定は切。索引の控えが無いと保存先へ書くため。後から互換のまま足せる）。
- `not` は葉まで下ろし、条件の葉では**画面の「除く」と同じ判定**（範囲・日付・属性は値の分からない商品も外す）。
- **絞らない値の条件**（「両方」・境の無い範囲）は種類を問わず「すべてに当たる」。`not` を付ければ何にも当たらない。
- 範囲・日付は送らなかった側を制限しない（両方送らなければ絞らない）。
- 並べる欄は `id`・`name`・`publishedAt`・`acquiredAt`・`wishCount`・`paid`・`fetchedAt`。値の無い商品は向きを問わず後ろ、同じ値は ID の順（並びは必ず一意）。
- `cursor` は前の答えの `nextCursor`。最後に返した商品の並べる値と ID を持ち、並べ方を変えて送られたら断る（`invalidCursor`）。中身は約束にしない。

### 条件（`condition`）

種類と値の形の一覧は `GET /v1/search/conditions`（見本 `Golden/search-conditions.json`）。保存の形をそのまま受けず、API の形で受けて変換する（`ConditionKinds.ToState`）。

| 形 | 種類 | 欄 |
|---|---|---|
| choice | saleStatus・adult・owned・gift・freePaid・favorite・hidden・brokenZip・missingFile・notOnBooth | `choice`（先頭が既定）・`options`（補助の切り替えを名前で） |
| list | category・boothTag・shop・unityProject・path | `values`・`match`（`any`・`all`。AND を持たない種類は OR）・`includeFavoriteShops`（shop） |
| avatar | avatar | `avatars`（ID）・`bases`（素体名）・`match`・`viaBase`（既定 true）・`matched`（既定 true）・`unspecified`（既定 false） |
| range | wishCount・price | `min`・`max`・`source`（`paid`・`booth`）・`allVariations`・`includeUnknown`・`ignoreOutliers`（既定 true） |
| date | publishedAt・acquiredAt | `from`・`to`（`YYYY-MM-DD`）・`scope`（`first`・`all`。入手日） |
| attribute | attribute | `ranges`（`name`・`min`・`max`）・`match` |
| userTag | userTag | `tags`（`top`・`subs`・`match`・`noSub`）・`match` |
| modification | modification | `modifications`（`avatar`・`modifications`・`match`）・`match` |
| editStatus | editStatus | `choice`（`unedited`・`edited`・`both`）・`fields`・`match` |
| updateNotice | updateNotice | `choice`（`updated`・`other`・`both`）・`fields`（種類）・`match` |
| recent | recent | `recent`（`used`・`viewed`・`added`）・`fromDays`・`toDays` |

読めない値は既定で読まず、決まった名前の誤りで返す（`unknownCondition`・`invalidChoice`・`invalidOption`・`invalidCondition`）。
編集状況の「取り込みの③待ちは未入力に数えない」は API では分からないので、待っていない物として数える。

## 選択肢の件数（`POST /v1/search/facets`）

`{ "where": …, "fields": ["category", "boothTag", "shop", "avatar", "userTag", "attribute"] }` → `{ "total": 2, "fields": { "category": [ { "value": "3D衣装", "name": null, "count": 1 } ] }, "avatarGroups": { "matched": 0, "unspecified": 2 } }`

- 数えるのは `where` の結果の中。画面は「自分の条件は自分を除いて数える」が、式は木なので「自分」を決められない。数えたい欄の条件を外した式を送ってもらう。
- `avatar` は画面の検索と同じ数え方の対応アバターごと。`avatarGroups` は対応アバターの「対応している」「対応の指定が無い」の数。`userTag` は `大分類` と `大分類/小分類`。`attribute` は評価した商品の数。

## SSE（`GET /v1/events`）の出来事

| 出来事 | 中身 |
|---|---|
| `changed` | 通番と、変わった物の種類・ID |
| `reloaded` | 全部を読み直した（外の道具も全部を取り直す） |
| `busy` | まとめて書く操作の始まり・終わり（種類つき） |
| `formatTooNew` | 保存の形が新しくなり、答えられなくなった |
| `stopping` | API が止まる |

- 切れたら `Last-Event-ID`（通番）でつなぎ直し、飛んだ分は `/v1/changes` で取る。

## 2026-10-14 に足した物

使い手の試作（自作の Web ページ・Unity のエディタ）が書いた不足から足した。上の表の説明文の行（「まだ」）は、ここで作った。

| URL・欄 | 要求 | 答え |
|---|---|---|
| `GET /v1/items/{id}/description` | なし | `{ id, summary, sections: [{ heading, text }], html }`。誤りは 404 `itemNotFound`・503 `descriptionUnreadable` |
| `GET /v1/images/{itemId}/{name}`・`/v1/modifications/{id}/images/{name}` の `?format=`・`?size=` | `format`：`png`・`jpeg`（`jpg` も同じ）。`size`：長い辺の画素 16〜2048 | 書き直した画像（`image/png`・`image/jpeg`）。誤りは 400 `invalidFormat`・`invalidSize`、422 `imageUnreadable` |
| `ItemSummary`（一覧・検索の要約）の欄 | — | `priceText`・`publishedAt`・`acquiredAt`・`wishCount`・`paid` を足した |
| `POST /v1/search`・`/v1/search/facets` の `hasUnityPackage`（`GET /v1/items` の `?hasUnityPackage=` も） | `true`・`false`。省けば絞らない | 絞った答え（`total`・続きの印も絞った後の数） |
| `POST /v1/items/batch` の `include` | カンマ区切りの文字列に加え、名前の配列 | 今までと同じ。配列の中の null・数は 400 `invalidInclude` |

- **説明文**：`summary` は BOOTH の商品の短い説明（画面の商品ページの説明の最初の欄）、`sections` は見出しごとの素の文（画面の見出しごとの欄と同じ `H2Sections`）、
  `html` は保存先の `items/{id}.h2.html` そのまま（アプリの読み口 `ItemRepository.LoadDescriptionHtmlAsync`）。動く物は保存するときに取り除いてある（`H2SectionExtractor`）ので、別の無害化はしない。
  中の画像・リンクは BOOTH の場所を指すので、ページに表示すると BOOTH へ取りに行く（外の道具の問い合わせで、Chmonos の 1本ずつの門は通らない）。
  写しが説明文を落としているとき（アプリと控えを共有しない）は記録のファイルから読む。説明が無い商品は誤りにせず空で返し、読めないときだけ 503。
  一覧や詳細に入れないのは、長く、写しにも持たない物だから。
- **画像**：省けば今までどおり元のファイル（多くは WebP）。書き直すのは Core の `ImageConversion`（復号の守りは保存のときと同じ `ImageLimits.Safe`・寸法の上限）。
  拡大はしない。`size` だけなら元の形式で縮める。JPEG は透けた所を白で埋める。範囲を 2048 までにしたのは、保存の長辺が 384（改変 768・店の帯 1200）で、それより大きく頼む理由が無いため。
- **要約の並べる欄**：値は並べ替えの鍵と同じ関数（`SortFields`）から出す（`SearchEngineTests` で鍵と突き合わせる）。
  `acquiredAt` は並べ替えの `acquiredAt` と同じ「購入1件の日付のうち最も早い物（購入記録が無ければ商品の入手日）」で、**詳細の `acquiredAt`（商品の入手日の欄そのもの）とは違い得る**。
  `paid` は自分用に払った額の合計（額を1つも入れていなければ null、無料は 0）。`priceText` は詳細と同じ BOOTH の値段の表示で、並べる欄ではない。
- **hasUnityPackage**：判断は `/v1/unitypackages` と同じ（ある zip の中と、見つかっている登録したフォルダの中。`Catalogs.HasSendablePackage`）。
  検索の条件（`condition`）の種類にはしない（条件は画面の絞り込みと同じ物だけ。画面に無い）。非表示・R-18 と同じく、式より先に絞る。
- **条件 `unityProject`**：場所は `UnityProjectPath.Normalize`（前後の空白・区切りを `\` に・`..` を畳む・末尾の区切りを落とす）で揃えて照らす。大文字小文字は区別しない（今までどおり）。
  改変に結ぶ `PUT /v1/modifications/{id}/unity-project` も同じ関数で揃えてから書く。画面の条件も同じ索引（`ModificationUsage`）を引くので、画面と API は同じ答え。保存した値は書き換えない。
