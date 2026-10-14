# API（開発者向け）

<!-- nav:top -->
[← 目次へ](README.md)
<!-- /nav:top -->

（次のバージョンから）この章の機能は、次のバージョンから使えます。

ほかのアプリや自作のツールから、Chmonos のデータを HTTP で読めるようにする機能です。プログラムを書く人に向けた機能で、ふだんの整理には使いません。

- はじめは読むだけです。設定で許すと、お気に入り・ユーザータグ・属性・メモなどを変えられます（[データを変える](#データを変える)）。BOOTH に問い合わせることはありません。
- 同じ PC の中（`127.0.0.1`）からだけ使えます。
- 答えは JSON です。名前の付け方や意味は変えない約束で、大きく変えるときは URL の `/v1/` を上げます。

## 使う前に

API が返す BOOTH の商品の情報（商品名・説明文・画像など）の権利は、それぞれの作者にあります。自分で使うための機能なので、取り出した情報を他の人に渡したり、公開したりしないでください。購入したファイルの扱いは、各ショップの利用規約に従ってください。

## 使えるようにする

1. 設定の「API」を開き、「APIを使う」をオンにします。すぐに待ち受けを始め、「状態」に「ポート 39847 で動いています。」と表示されます。
2. ブラウザで `http://127.0.0.1:39847/v1/health` を開きます。`"status":"ok"` と表示されれば動いています。

API は Chmonos と一緒に動き、Chmonos を閉じると止まります。「APIを使う」をオフにしたときも、すぐに止まります。Chmonos でデータを変えると、API は数秒以内に読み直します。取り込みの間は、終わるまで前の答えを返します。

ポートは既定で `39847` です。「待ち受けられませんでした」と表示されたときは、ほかのアプリがそのポートを使っています。設定の「API」の「ポート」を変えると、その場で新しいポートで待ち受けます。

### ウィンドウを表示せずに動かす

ウィンドウを開かずに API を使いたいときは、`Chmonos.exe --headless` で起動します。ショートカットの「リンク先」の末尾に ` --headless` を足しておくと便利です。

- 動いている間は、タスクバーの通知領域に Chmonos のアイコンが表示されます。右クリックして「Chmonosを開く」でウィンドウを開き、「終了」で終えます。
- 開いたウィンドウを閉じても、Chmonos は通知領域で動き続けます。
- （次のバージョンから）動いている間に Chmonos をふつうに起動しても、ウィンドウが開きます。
- 「APIを使う」がオフのままだと、API は待ち受けません。
- 先に一度ふつうに起動して、データの保存先を決めておいてください。
- 設定の「API」の「ウィンドウを表示せずに起動したとき、監視フォルダの新しいファイルと前回の続きを取り込む」をオンにすると、起動したときに取り込みを始めます。

## トークンを付けて問い合わせる

API は、トークンを付けた問い合わせにだけ答えます。トークンは、データの保存先にある `api.json` の `token` です。保存先は設定の「データ」の「エクスプローラで開く」で開けます。

```powershell
$api = Get-Content "$env:LOCALAPPDATA\Chmonos\api.json" -Raw | ConvertFrom-Json
Invoke-RestMethod "http://127.0.0.1:$($api.port)/v1/items?limit=5" -Headers @{ Authorization = "Bearer $($api.token)" }
```

- `/v1/health`・`/v1/version`・`/v1/openapi.json` は、トークンなしで答えます。
- トークンは起動のたびには変わりません。漏れたかもしれないときは、設定の「API」の「トークン」の「作り直す」を押します。今のトークンは使えなくなるので、ツールには `api.json` から読み直させてください。
- `api.json` は、バックアップの書き出しには入りません。

## 主な URL

すべての URL と答えの形は、仕様書 `/v1/openapi.json`（OpenAPI）にあります。多くの言語で、ここから問い合わせのコードを作れます。

| URL | 答え |
|---|---|
| `GET /v1/items`・`GET /v1/items/{id}` | 商品の一覧・商品1件 |
| `POST /v1/search` | 式で検索した商品 |
| `POST /v1/search/facets` | 検索の結果の中での、カテゴリ・対応アバターなどの件数 |
| `GET /v1/search/conditions` | 検索の式に使える条件の種類 |
| `GET /v1/avatars`・`GET /v1/avatar-bases` | アバター・共通素体 |
| `GET /v1/files/lookup?path=…` | ファイルの場所から、どの商品の物かを引く（`?hash=` でも引ける） |
| `GET /v1/unitypackages?avatar=…` | 手元の unitypackage（対応アバターで絞れる） |
| `GET /v1/user-tags`・`/v1/attributes`・`/v1/categories`・`/v1/shops` | タグ・属性・カテゴリ・ショップと件数 |
| `GET /v1/modifications`・`/v1/notifications` | 改変・通知 |
| `GET /v1/changes`・`GET /v1/events` | 変わったこと（下の「変わったことを知る」） |
| （次のバージョンから）`GET /v1/items/{id}/description` | 商品の説明文（短い説明・見出しごとの文・表示用の HTML） |
| （次のバージョンから）`GET /v1/images/{商品ID}/{名前}?format=png&size=256` | 画像を PNG か JPEG にして、長い辺を縮めて返す（下の「画像の形式と大きさ」） |

対応アバター（`compatibleAvatars`）は、検索の「対応アバター」と同じ数え方です。まだ確認していない推定の対応も入り、消した対応と説明文のリンクは入りません。

## 検索の式

`POST /v1/search` には、`and`・`or`・`not` で組んだ式を送ります。式の1段には、どれか1つだけを書きます。

```json
{
  "where": {
    "and": [
      { "text": "水着" },
      { "condition": { "kind": "avatar", "avatars": ["1234567"] } },
      { "not": { "condition": { "kind": "price", "min": 3000 } } }
    ]
  },
  "sort": [ { "field": "wishCount", "order": "desc" } ],
  "limit": 50
}
```

- `text` は、検索の画面の入力欄と同じ書き方です（`name:`・`OR`・`-` なども使えます）。
- `condition` は、検索の画面の絞り込みの条件1つです。種類と書き方は `/v1/search/conditions` にあります。
- `not` は、絞り込みの条件の「除く」と同じ動きです。価格や日付では、値の分からない商品も外します。
- 非表示の商品と R-18 の商品も返します。返した商品には `isHidden`・`isAdult` が付きます。外したいときは `"includeHidden": false`・`"includeAdult": false` を送ります。
- 続きがあるときは、答えの `nextCursor` を次の問い合わせの `cursor` に入れます。
- （次のバージョンから）Unity へ送れる unitypackage を持つ商品だけにするときは、`"hasUnityPackage": true` を送ります。`false` なら持たない商品だけです。
- （次のバージョンから）一覧の1件には、並べ替えに使う値（`publishedAt`・`acquiredAt`・`wishCount`・`paid`）と BOOTH の値段（`priceText`）も入ります。`acquiredAt` は並べ替えと同じく、購入の日付のうち最も早い日です。
- （次のバージョンから）条件 `unityProject` の場所は、`/` 区切りでも、末尾に区切りがあっても当たります。

## 詳しい情報を足す

商品1件の答えは、既定では結果だけを返します。次の名前を `?include=` に付けると、詳しい情報を足します。

| 名前 | 足す物 |
|---|---|
| `avatarLinks` | 対応アバターをどこから見つけたか、確認したか・消したか |
| `allFiles` | 管理から外したファイルと、上書きで残った古い版 |
| `contents` | zip の中のファイルの一覧 |
| `images` | 画像の全部（既定はサムネイルだけ） |
| `meta` | BOOTH の情報を最後に取った日時 |

例：`/v1/items/1234567?include=avatarLinks,images`

（次のバージョンから）`POST /v1/items/batch` の `include` は、`["avatarLinks", "images"]` のような配列でも送れます。

## 画像の形式と大きさ

（次のバージョンから）画像はふつう WebP のまま返します。WebP を読めない環境（Unity の `Texture2D.LoadImage` など）では、次を付けます。

- `format`：`png` か `jpeg`。JPEG では、透けている所を白で埋めます。
- `size`：長い辺の画素（16〜2048）。元より大きくはしません。`size` だけなら、元の形式のまま縮めます。

例：`/v1/images/1234567/abcdef.webp?format=png&size=256`

## 変わったことを知る

- `GET /v1/changes?since=<通番>&epoch=<名前>`：前に見た時点より後に変わったことを返します。通番と名前は `/v1/version` の `sequence`・`epoch` にあります。答えの `reload` が `true` のときは、変わったことを全部は追えていないので、全部を取り直してください。
- `GET /v1/events`：変わったことを Server-Sent Events で流します。つなぎ直すときは、最後に受けた `id` を `Last-Event-ID` で送ると、続きから流れます。

## 自作の Web ページから使う

ブラウザで開いたページから使うときは、そのページのオリジンを設定の「API」の「許可するオリジン」に追加します（例：`http://localhost:5173`）。追加していないページからの問い合わせは断ります。

- ページは `api.json` を読めないので、トークンはページに手で貼り付けてください。
- トークンは、`fetch` の `Authorization` ヘッダで送ります。
- `<img src>` はトークンを送れないので、画像は `fetch` で `/v1/images/{商品ID}/{名前}` を取り、`URL.createObjectURL` で表示します。
- `EventSource` もトークンを送れないので、`/v1/events` は `fetch` で流れを読みます。

## データを変える

（次のバージョンから）設定の「API」の「ほかのアプリからの変更を許す」をオンにすると、API からデータを変えられます。はじめはオフです。トークンは読むときと同じです。

| すること | URL | 送る物 |
|---|---|---|
| お気に入りにする・外す | `PUT /v1/items/{商品ID}/favorite` | `{"value": true}` |
| 非表示にする・戻す | `PUT /v1/items/{商品ID}/hidden` | `{"value": true}` |
| ユーザータグを付け直す | `PUT /v1/items/{商品ID}/user-tags` | `{"tags": [{"top": "衣装", "subs": ["夏"]}]}` |
| ユーザータグを1つ足す・外す | `POST /v1/items/{商品ID}/user-tags`・`…/user-tags/remove` | `{"top": "衣装", "sub": "夏"}` |
| 属性を付ける・外す | `PATCH /v1/items/{商品ID}/attributes` | `{"values": {"かわいさ": 80, "露出": null}}` |
| メモを書く | `PUT /v1/items/{商品ID}/memo` | `{"memo": "…"}` |
| ユーザータグ・属性を一覧に足す | `POST /v1/user-tags`・`POST /v1/attributes` | `{"top": "衣装"}`・`{"name": "かわいさ"}` |
| 「使った」を記録する | `POST /v1/items/{商品ID}/recent` | `{"kind": "used"}` |
| 通知を既読にする・解消する | `PUT /v1/notifications/{ID}/read`・`POST /v1/notifications/read`・`POST /v1/notifications/resolve` | `{"value": true}`・`{"ids": […]}` |
| Unity へ送る | `POST /v1/items/{商品ID}/send-to-unity` | `{"entry": "…", "project": "…"}`（省けるときは `{}`） |
| Chmonos の画面で商品を表示する | `POST /v1/items/{商品ID}/show` | なし |
| エクスプローラで開く | `POST /v1/items/{商品ID}/reveal` | `{"path": "…"}`（場所が1つなら `{}`） |
| 対応アバターを足す・消す | `POST /v1/items/{商品ID}/avatars`・`…/avatars/remove` | `{"avatarId": "…"}` |
| 確認待ちの対応アバターを確認済みにする | `POST /v1/items/{商品ID}/avatars/confirm` | `{"avatarIds": ["…"]}` |
| 共通素体を足す・消す | `POST /v1/items/{商品ID}/avatar-bases`・`…/avatar-bases/remove` | `{"baseName": "…"}` |
| 改変を作る | `POST /v1/modifications` | `{"avatarId": "…", "name": "普段着"}` |
| 改変のメモ・blueprint ID・Unity プロジェクトを書く | `PUT /v1/modifications/{ID}/memo`・`…/blueprint-id`・`…/unity-project` | `{"memo": "…"}`・`{"blueprintId": "avtr_…"}`・`{"path": "…"}` |
| 改変に使った物を足す・外す・戻す | `POST /v1/modifications/{ID}/members`・`…/members/detach`・`…/members/restore` | `{"itemId": "…"}` |
| 改変に使った物を順に Unity へ送る（次のバージョンから） | `POST /v1/modifications/{ID}/send-to-unity` | `{"choices": [{"index": 1, "entry": "…"}]}`（選ぶ物が無ければ `{}`） |
| Unity へ送るのを止める（次のバージョンから） | `POST /v1/unity/sends/{送りのID}/cancel` | なし |

- 変えた後の商品を答えに返します。続けて GET しても同じ答えです。
- ユーザータグと属性の名前は、一覧にある名前に合わせます。一覧に無い名前は断ります。一覧に足してから付けるときは、URL に `?createMissing=true` を付けます。
- 属性の `null` は「評価していない」に戻します。`0` とは別です。
- Chmonos の編集画面で開いている商品は変えられません（`409`）。編集を終えてから送ってください。
- 取り込みの途中は、変えたことを `202` で受け付け、取り込みが終わってから答えに入ります。
- 「Unity へ送る」は、Unity の取り込み画面が出たところで待たずに返ります（`202`）。結果は `GET /v1/unity/last-send` と、変化の知らせ（`unity`）で分かります。送る物が複数あるときは `entry`、Unity を複数開いているときは `project` で選びます。開いているプロジェクトは `GET /v1/unity/editors` で分かります。
- （次のバージョンから）Unity へ送ると、答えに送りの ID（`sendId`）が付きます。結果は `GET /v1/unity/sends/{送りのID}` で分かります。`state` が `sending` の間は送っている途中です。終わると `imported`（取り込んだ）・`alreadyPresent`（もう入っていた）・`cancelled`（Unity で Cancel した）・`stopped`（止めた）・`editorClosed`（Unity が閉じた）・`failed`（送れなかった）のどれかになります。
- （次のバージョンから）改変に使った物を順に送る前に、`GET /v1/modifications/{ID}/send-plan` で行ごとの送り方を確かめられます。`status` が `choose` の行は、`candidates` から送る物を選んで `choices` に書きます。選んだ物は、その行に記録されます。
- （次のバージョンから）送るのを止めても、Unity に出た取り込み画面は残ります。Unity で「Cancel」を押して閉じてください。
- 「Chmonos の画面で商品を表示する」は、ウィンドウを表示せずに動いているときもウィンドウを開きます。データは変えませんが、画面が切り替わるので、「ほかのアプリからの変更を許す」がオンのときだけ使えます。
- 対応アバターを消しても、商品ページの「消したもの」から戻せます。確認待ちを確認済みにするのは、名前を挙げた物だけです。
- 改変を消す・名前を変える、対応アバターをまとめて確認済みにする、といった操作は、Chmonos の画面で行ってください。
- 許可した Web ページからも変えられます。

## 困ったとき

| 出たこと | すること |
|---|---|
| 「ポート … で待ち受けられませんでした。」 | ほかのアプリがそのポートを使っています。設定の「API」でポートを変えてください。 |
| `--headless` で起動しても、何も起きない | 同じデータで Chmonos が既に動いています。通知領域のアイコンを確かめてください。 |
| `401`（`unauthorized`） | トークンが違います。`api.json` の `token` を送ってください。作り直した後は、新しいトークンに替えてください。 |
| `403`（`writesDisabled`） | 設定の「API」で「ほかのアプリからの変更を許す」をオンにしてください。 |
| `409`（`itemBeingEdited`） | Chmonos の編集画面でその商品を開いています。編集を終えてから送ってください。 |
| `503`（`storeBusy`） | データの保存先を移している途中です。少し待ってから送ってください。 |
| `403`（`originNotAllowed`） | そのページのオリジンを、設定の「API」の「許可するオリジン」に追加してください。 |
| `503`（`storeFormatTooNew`） | 新しいバージョンの Chmonos で使ったデータです。Chmonos を新しくしてください。 |

誤りの答えは `application/problem+json` で、`code` に決まった名前が入ります。
