# 外から使う API（案）

> **要点**：Chmonos の手元の資産（自動の紐付けで決まった対応アバター・素体・手元のファイル・タグ・属性）を、何の言語からでも読めるように、**別の実行ファイルがローカルの HTTP で答える**（2026-10-12・ユーザ判断）。読むだけ。BOOTH へは問い合わせない。
> まだ作っていない。この文書は決めたことと答えの形の案。答えの形はユーザが見て決めるまで変わり得る。

## 決めたこと（2026-10-12）

- **何の言語からでも**を重く見て、ローカルの HTTP にする（CLI・MCP・名前付きパイプ・データ形式の公開だけ、と比べた）。
- **別の実行ファイル**（仮の名前 `Chmonos.Api.exe`）。C# の ASP.NET Core（Kestrel）の minimal API で、Native AOT の単体の exe にする。
  `HttpListener` は待つアドレスによって管理者の権限・URL の予約が要るので使わない。C++・Rust で作り直さない（検索の記法・辞書の展開・所持の定義を2か所に持つとずれる）。
- **アプリと同じ zip で配る**（アプリとバージョンを揃える。合わない組み合わせでは答えない）。
- **起動は本体と連動させない**。使う人が手で起動する（ログオン時の起動は後で設定に足すかもしれない）。
- **読むだけ**。書き込みは画面の `UiCommand` だけを通る決まりを崩さない。BOOTH の 1本ずつ・1.5秒の決まりを外から破れないよう、足りない情報も取りに行かない。
- **条件の JSON を最初の版から受ける**（画面の絞り込みの条件と同じ物を構造で）。判定を App の ViewModel から Core へ移してから作る。
- 検索欄の記法に、条件でしか書けない物（日付・属性・改変など）を足すのは**保留**（API とは別に考える）。

## 同期

- 正しい値はいつも保存先のファイル。API はそれを読んでメモリに持つ。
- アプリは書き込みを終えて錠を外した後に、`POST /internal/changed` で**変わった物の ID と通番だけ**を知らせる（中身は送らない。偽の知らせが来ても読み直しが増えるだけ）。
  待たない・すぐ諦める・API が動いていなければ何もしない。アプリの書き込みを API の都合で遅らせない。
- 知らせは落ち得る。API は起動したとき全部を読み、通番の飛びに気付いたら全部を読み直す。時々の確かめも入れる。
- 書き込み中の JSON は `AtomicReplace` で置き換わるので、書きかけを読むことは無い。まとめて書く操作（取り込み・引越し・戻す）の途中は半端な状態を答え得る（その間は 503 を返す案もある。未決）。
- API は書き込みの錠を取らない。保存先は `StoreLocation.Resolve`（`CHMONOS_HOME` か `location.json`）で決め、1つの API は1つの保存先にだけ答える。
- 保存の形（`format.json`）が API の知る版より新しいときは答えない（503・「API を新しくしてください」）。

## 守り

- 待つのは `127.0.0.1` だけ。ポートは決まった番号を既定にし、設定で変えられる。使っている番号・トークン・PID・API のバージョンを保存先の `api.json` に書く。
- すべての要求にトークン（`Authorization: Bearer …`）。ヘッダが要るのでブラウザは事前の確かめを挟み、他所のページからは通らない。
- `Host` が `127.0.0.1:<ポート>`・`localhost:<ポート>` 以外なら断る（DNS rebinding）。CORS の許可は出さない。
- 問い合わせの中身はログに残さない（購入履歴と検索の言葉）。

## 共通の決まり

- URL に `/v1/`。項目を足すのは互換、消す・名前を変える・意味を変えるのは非互換で `/v2/`。外の道具には「知らない項目は無視する」と書いてもらう。
- 名前は camelCase。日時は ISO 8601（時差付き）。ID は文字列。列挙の値は文字列。値が無いときは項目を省かず `null`。
- 一覧は続きの印（`cursor`）で分ける。1回 200 件まで。並びは必ず一意（同じ値なら ID で決める）。
- 答えに `ETag`。変わっていなければ `304`。
- エラーは `application/problem+json`（RFC 9457）。機械が読む `code` は変えない。
- 仕様書は OpenAPI をコードから作り、`/v1/openapi.json` で出す。
- 内部の状態の区別を潰さない：確認済み／候補／消した（`Rejected`）、外したファイル、非表示、未評価と 0。
- 計算で出す値（持っているか・所有アバター）も返す。外で計算させるとずれる。答えは Core の同じ所（`LocalBlock.IsOwned` など）から取る。

## URL（案）

| URL | 中身 |
|---|---|
| `GET /v1/health` | 動いているか・保存先を読み終えたか |
| `GET /v1/version` | API のバージョン・保存の形の版・今の通番 |
| `GET /v1/items` | 商品の一覧（要約）。`cursor`・`limit` |
| `GET /v1/items/{id}` | 商品1件（詳細） |
| `POST /v1/search` | 検索。`q`（検索欄の記法）と `conditions`（条件の JSON）を AND。答えは商品の一覧（要約） |
| `GET /v1/avatars` | アバターの登録簿（所有しているかを含む） |
| `GET /v1/avatars/{id}/items` | そのアバターに対応する商品（素体経由を含めるかを指定） |
| `GET /v1/tags`・`GET /v1/attributes` | ユーザータグ・属性の一覧 |
| `GET /v1/changes?since=<通番>` | 前に見た時点からの変化（変わった ID） |

## 答えの形（案）

商品1件（`GET /v1/items/{id}`）：

```json
{
  "id": "1234567",
  "name": "表示に使う名前（手で直した名前があればそれ）",
  "boothName": "BOOTH の商品名",
  "url": "https://booth.pm/ja/items/1234567",
  "shop": { "name": "…", "subdomain": "…" },
  "category": "3D衣装",
  "isAdult": false,
  "isHidden": false,
  "isFavorite": false,
  "isDelisted": false,
  "isOwned": true,
  "avatars": [
    { "avatarId": "7654321", "name": "…", "source": "supportSection", "state": "confirmed" },
    { "avatarId": "1111111", "name": "…", "source": "tag", "state": "candidate" },
    { "avatarId": "2222222", "name": "…", "source": "manual", "state": "rejected" }
  ],
  "avatarBases": [ { "name": "…", "source": "supportList", "state": "candidate" } ],
  "avatarsDetectedAt": "2026-10-01T12:00:00+09:00",
  "userTags": [ { "top": "衣装", "subs": ["夏"] } ],
  "attributes": { "かわいい": 3 },
  "files": [
    {
      "hash": "…", "sizeBytes": 123456, "variationId": 111,
      "paths": ["D:\\Assets\\…\\foo.zip"],
      "state": "present",
      "unityPackages": ["foo/foo.unitypackage"]
    }
  ],
  "folders": [ { "path": "D:\\Assets\\…", "fileCount": 120, "totalBytes": 9000000, "missingSince": null } ],
  "purchases": [ { "variationId": 111, "variationName": "…", "price": 1500, "kind": "自分用" } ],
  "image": { "thumbnailPath": "C:\\…\\images\\1234567\\…" },
  "updatedAt": "2026-10-10T09:00:00+09:00"
}
```

- `state` は `confirmed`（確認済み）・`candidate`（推定で、まだ人が選んでいない）・`rejected`（人が消した）。中の `Confirmed`・`Rejected` の2つの真偽を1つの値にした（両方が真の組み合わせは作らない）。
- ファイルの `state` は `present`・`missing`（場所が空）・`detached`（外した・上書きで残った古い版）。
- アセットのファイルは場所だけ返し、中身は送らない。画像も場所を返す。
- 一覧の要約は、`id`・`name`・`shop`・`category`・`isOwned`・`isAdult`・`isHidden` と、確認済みの対応アバターの ID だけ。

条件の JSON（`POST /v1/search`）：

```json
{
  "q": "夏 avatar:…",
  "conditions": [
    { "kind": "date", "field": "purchased", "from": "2026-01-01", "to": null },
    { "kind": "attribute", "values": ["かわいい"], "match": "any", "exclude": false }
  ],
  "includeHidden": false,
  "cursor": null,
  "limit": 100
}
```

- 条件の形は、検索の履歴に残している形（`SearchModuleState`）をそのまま出さず、API の形から変換する（中を直しても外の約束が壊れないように）。

## 未決

- 非表示と R-18 の既定（画面と同じく外すか、全部返して印だけ付けるか）。
- まとめて書く操作の途中に、503 を返すか、古い答えを返し続けるか。
- 選択肢の件数（どの値を選べるか）を出すか。
- BOOTH の情報（説明文・画像）を外の道具が再配布しないよう、注意を書くか（`docs/research/booth-terms.md` を読んでから）。
- 外の道具への変化の知らせは、最初は `/changes` だけ。SSE は要る道具が出てから。
