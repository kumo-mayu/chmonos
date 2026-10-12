# 外から使う API：URL と答えの形（案）

> **要点**：[local-api.md](local-api.md) の URL と答えの形の案。**広めに出して、ユーザが削る**（2026-10-12・ユーザ指示）。印の「◎」は最初に要る物、「○」はあると便利、「△」は削ってよさそうな物（作り手の見立て）。

## URL

### 仕組み

| | URL | 中身 |
|---|---|---|
| ◎ | `GET /v1/health` | 動いているか・読み終えたか（合言葉なし） |
| ◎ | `GET /v1/version` | API のバージョン・保存の形の版・今の通番・まとめて書いている途中か・BOOTH の情報についての注意（合言葉なし） |
| ◎ | `GET /v1/openapi.json` | 仕様書 |
| ◎ | `GET /v1/changes?since=<通番>` | 前に見た時点からの変化（種類と ID） |
| ◎ | `GET /v1/events` | SSE。変化・全部の読み直し・まとめて書く始まりと終わり・止まる |
| ○ | `GET /v1/display-settings` | 使う人の表示の設定（R-18 を表示しない・非表示の件数を出す、など）。外の道具が画面と同じに隠すため |

### 商品

| | URL | 中身 |
|---|---|---|
| ◎ | `GET /v1/items` | 一覧（要約）。`cursor`・`limit`・`includeHidden`・`includeAdult`・`sort` |
| ◎ | `GET /v1/items/{id}` | 1件（詳細。下の形） |
| ◎ | `POST /v1/items/batch` | ID をまとめて渡して詳細を返す（一覧から詳細へ、1件ずつ聞かずに済む） |
| ○ | `GET /v1/items/{id}/description` | 説明文（見出しごとの文と、表示用の HTML） |
| ○ | `GET /v1/items/{id}/modifications` | その商品を使っている改変 |
| ○ | `GET /v1/items/{id}/notifications` | その商品の通知（更新・価格・説明文の変化） |

### 検索

| | URL | 中身 |
|---|---|---|
| ◎ | `POST /v1/search` | 式で検索（下の形）。答えは一覧（要約） |
| ◎ | `GET /v1/search/conditions` | 使える条件の種類と、それぞれの値の形（外の道具が条件の画面を組むため） |
| ○ | `POST /v1/search/facets` | 選択肢の件数：式の結果の中で、指定した欄（カテゴリ・対応アバター・タグ…）の値ごとの件数 |
| ○ | `GET /v1/search/explain?q=…` | 検索欄の文字列をどう読んだか（前置き・辞書で広げた別表記）。「なぜ当たったか」を説明する |
| ○ | `GET /v1/saved-searches` | 保存した検索（式に変換した物も添える） |
| △ | `GET /v1/search-history` | 検索の履歴 |

### アバター・素体

| | URL | 中身 |
|---|---|---|
| ◎ | `GET /v1/avatars` | 登録簿（名前・呼び方・素体・所有しているか） |
| ◎ | `GET /v1/avatars/{id}` | 1体 |
| ◎ | `GET /v1/avatars/{id}/items` | 対応する商品。`via=direct|base|both`・`state=confirmed|candidate|all` |
| ○ | `GET /v1/avatar-bases`・`GET /v1/avatar-bases/{name}/items` | 共通素体と、対応する商品 |

### ファイル・Unity（自動の紐付けの資産を外で使う中心）

| | URL | 中身 |
|---|---|---|
| ◎ | `GET /v1/files/lookup?path=…` | このファイル・フォルダはどの商品の物か（Unity や外の道具から逆に引く） |
| ◎ | `GET /v1/files/lookup?hash=…` | 同じくハッシュで |
| ◎ | `GET /v1/unitypackages` | 手元の `.unitypackage`（zip の中・フォルダの中）と、その商品・対応アバター。`avatar=` で絞る |
| ○ | `GET /v1/folders` | 商品として登録したフォルダ |
| ○ | `GET /v1/unresolved` | 商品が決まっていないファイル |
| △ | `GET /v1/excluded` | 管理から外したファイル |
| △ | `GET /v1/volumes` | ドライブ文字と通し番号の組 |

### 分類・ショップ・改変・その他

| | URL | 中身 |
|---|---|---|
| ◎ | `GET /v1/user-tags` | ユーザータグ（大分類・小分類・メモ・件数） |
| ◎ | `GET /v1/attributes` | 属性（名前・メモ・件数） |
| ○ | `GET /v1/categories` | カテゴリ（BOOTH のカテゴリと自分で入れたカテゴリ・件数） |
| ○ | `GET /v1/booth-tags` | BOOTH のタグと件数 |
| ◎ | `GET /v1/shops`・`GET /v1/shops/{key}`・`GET /v1/shops/{key}/items` | ショップ（名前・サブドメイン・自分で作ったショップか・星・メモ・件数） |
| ○ | `GET /v1/modifications`・`GET /v1/modifications/{id}` | 改変（アバター・使った商品とファイル・Unity プロジェクト・メモ） |
| ○ | `GET /v1/unity-projects` | 改変に書いた Unity プロジェクトと、その改変 |
| ○ | `GET /v1/notifications` | 通知（既読・解決済みの印つき） |
| △ | `GET /v1/recent` | 「最近」の足跡 |
| △ | `GET /v1/stats` | 統計（件数・金額）。画面の統計と同じ数え方 |
| ○ | `GET /v1/images/{itemId}/{name}` | 画像の本体（ブラウザの画面のため。場所は商品の答えにも入れる） |
| ○ | `GET /v1/modifications/{id}/images/{name}` | 改変の画像の本体 |

## 商品1件の形

```json
{
  "id": "1234567",
  "name": "表示に使う名前（手で直した名前があればそれ）",
  "boothName": "BOOTH の商品名",
  "url": "https://booth.pm/ja/items/1234567",
  "shop": { "key": "example", "name": "…", "subdomain": "example", "isLocal": false, "url": "…" },
  "category": "3D衣装",
  "boothCategory": { "id": 208, "name": "3D衣装", "parentName": "3Dモデル" },
  "boothTags": ["…"],
  "priceText": "¥ 1,500",
  "wishCount": 1234,
  "publishedAt": "2025-05-01T12:00:00+09:00",
  "isAdult": false, "isEndOfSale": false, "isSoldOut": false, "isDelisted": false,
  "isHidden": false, "isFavorite": false, "isOwned": true, "isAvatar": false,
  "hasUpdate": false, "hasBrokenArchive": false, "hasMissingFiles": false,
  "variations": [ { "id": 111, "name": "…", "price": 1500, "status": "…", "type": "…" } ],
  "purchases": [ { "variationId": 111, "variationName": "…", "price": 1500, "kind": "self", "purchasedAt": "2026-01-01", "note": null, "existsOnBooth": true } ],
  "acquiredAt": "2026-01-01",
  "avatars": [
    { "avatarId": "7654321", "name": "…", "source": "supportSection", "state": "confirmed", "isOwned": true }
  ],
  "avatarBases": [ { "name": "…", "source": "supportList", "state": "candidate" } ],
  "avatarsDetectedAt": "2026-10-01T12:00:00+09:00",
  "userTags": [ { "top": "衣装", "subs": ["夏"] } ],
  "attributes": { "かわいい": 3 },
  "memo": "…",
  "files": [
    {
      "hash": "…", "sizeBytes": 123456, "variationId": 111,
      "paths": ["D:\\Assets\\…\\foo.zip"],
      "state": "present",
      "missingSince": null, "isArchiveBroken": false,
      "replaced": null,
      "unityPackages": [ { "entry": "foo/foo.unitypackage", "roots": ["Assets/foo"] } ],
      "contents": ["foo/foo.unitypackage", "foo/readme.txt"]
    }
  ],
  "folders": [ { "path": "D:\\Assets\\…", "fileCount": 120, "totalBytes": 9000000, "missingSince": null, "unityPackages": ["…"] } ],
  "images": {
    "thumbnail": { "name": "….webp", "path": "C:\\…\\images\\1234567\\….webp" },
    "booth": [ { "name": "….webp", "path": "…", "caption": null, "role": null } ],
    "user": [ { "name": "…", "path": "…", "caption": "…", "addedAt": "…" } ]
  },
  "videos": [ { "id": "…", "title": "…" } ],
  "fetchedAt": "2026-10-10T09:00:00+09:00",
  "updatedAt": "2026-10-10T09:00:00+09:00"
}
```

- 紐付けの `state` は `confirmed`（確認済み）・`candidate`（推定で、まだ人が選んでいない）・`rejected`（人が消した）。中の `Confirmed`・`Rejected` の2つの真偽を1つの値にした。
- ファイルの `state` は `present`（ある）・`missing`（場所が空）・`detached`（外した）・`oldVersion`（上書きで残った古い版）。
- アセットのファイルは場所だけ返し、中身は送らない。
- 一覧の要約は、`id`・`name`・`shop`・`category`・`isOwned`・`isAdult`・`isHidden`・`isFavorite`・確認済みの対応アバターの ID・サムネイルの名前。

## 検索の式（`POST /v1/search`）

```json
{
  "where": {
    "and": [
      { "text": "夏 avatar:…" },
      { "or": [
        { "condition": { "kind": "attribute", "values": ["かわいい"], "match": "any" } },
        { "condition": { "kind": "userTag", "top": "衣装", "sub": "夏" } }
      ] },
      { "not": { "condition": { "kind": "date", "field": "purchased", "from": "2026-01-01", "to": null } } }
    ]
  },
  "includeHidden": true,
  "includeAdult": true,
  "sort": [ { "field": "purchasedAt", "order": "desc" } ],
  "cursor": null,
  "limit": 100
}
```

- 葉は `text`（検索欄の記法）か `condition`（画面の絞り込みの条件1つ）。枝は `and`・`or`・`not`。
- 条件の中の「どれか・すべて」（`match`）や、対応アバターの「素体経由を含める」は条件の値として持つ。画面の「除く」は `not` で書く。
- 条件の形は、検索の履歴に残している形（`SearchModuleState`）をそのまま出さず、API の形から変換する（中を直しても外の約束が壊れないように）。
- `includeHidden`・`includeAdult` の既定は `true`（全部返して印を付ける）。

## SSE（`GET /v1/events`）の出来事

| 出来事 | 中身 |
|---|---|
| `changed` | 通番と、変わった物の種類・ID |
| `reloaded` | 全部を読み直した（外の道具も全部を取り直す） |
| `busy` | まとめて書く操作の始まり・終わり（種類つき） |
| `formatTooNew` | 保存の形が新しくなり、答えられなくなった |
| `stopping` | API が止まる |

- 切れたら `Last-Event-ID`（通番）でつなぎ直し、飛んだ分は `/v1/changes` で取る。
