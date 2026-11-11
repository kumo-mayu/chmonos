# 外から使う API：手元に控えた情報を読む

> **要点**：Chmonos が取り込みや画面の作業で保存先に控えた物（unitypackage の中身・Unity プロジェクトを調べた結果・ファイルのハッシュ・zip の中の絵の目録・通知の保管庫・未確定の自動検索）を、API で読めるようにする（2026-11-11・plan 32）。
> ユーザ指摘「手元にGUIDなどを控えているのにそれをAPIで公開していませんね」。ほかのソフトが同じ物を作るには、全部の zip と中の tar.gz を解く必要がある。
> 決め事の全体は [local-api.md](local-api.md)、許しは [api-clients.md](api-clients.md)。

## 共通の決まり

- **どれも読むだけ**。読む許しはどのトークンにもある（書き込みの種類を問わない。新しい種類は足していない）。
- **控えはアプリが書く物で、API は控えを作らない・書き足さない**（zip を解かない・`LoadOrRemember` や `UnityPackagePathStore.Add` のような書く口を使わない・BOOTH に問い合わせない）。控えが無ければ「まだ読んでいない」と答える。
- 画面と同じ読み口と判断を使う（控えを読むのは Core の `Load`、商品へ戻すのは写しの手元のファイル、状態は `FileRef.State` と同じ `ItemMapper.FileState`）。
- 控えのフォルダ・ファイルは見張りの対象（`StoreChanges`）に入れない。読む道ごとに、要求のたびにファイルの印（数・大きさ・日時）を見て、変わっていれば読み直す（見張りの部分を足すと、取り込みの裏が控えを書くたびに知らせが流れる）。
- まとめて引く道の入力は `items/batch` に揃える：配列の中の `null` は 400、件数に上限、答えは `found` と `missing`。商品へ戻せない物（どの商品のファイルでもないハッシュ）は `missing` に入れる。
- 場所（パス）を返す道は、今の `/v1/files/lookup` と同じ守り（`127.0.0.1` だけ・トークン必須・中身をログに残さない）。

## unitypackage の中身（`unitypackages/<zip のハッシュ>.json`）

| URL | 要求 | 答え |
|---|---|---|
| `GET /v1/items/{id}/unitypackage-assets` | なし | `[{ zipHash, fileState, entry, assets: [{ guid, path }] \| null }]`。誤り 404 `itemNotFound` |
| `POST /v1/unitypackages/lookup` | `{ "guids": [ … ] }`（1〜1000件） | `{ found: [{ guid, path, itemId, itemName, zipHash, entry, fileState }], missing: [guid] }`。誤り 400 `invalidGuids`・`invalidGuid`・`invalidLimit` |

- 使い道：Unity のプロジェクトで Missing になった参照の GUID から、入れ直す商品を引く。`zipHash`・`entry` は `send-to-unity` にそのまま渡せる。
- **控えを作るのは zip の中の unitypackage だけ**（`UnityPackageCatalog`）。単体の unitypackage・登録したフォルダの中の物は、どちらの道にも出ない。
- 商品の包みの並びは中身の一覧（`contents`）の順。一覧の無い古い記録は要約（`unityPackages`）の entry。控えに無い包みは `assets: null`（取り込みの裏で読むまで待つ）。外した物・古い版のファイルも状態つきで返す（「このファイルは何か」への答えなので。逆引きと同じ）。
- GUID は前後の空白を除いて小文字に揃える（Unity は小文字で書く。控えに大文字が混じっても同じに引ける）。32桁の16進でなければ 400 `invalidGuid`。
- **同じ GUID が複数の包み・商品にあれば全部返す**（シェーダーの同梱・セットと単品に同じ zip など）。照合（`UnityProjectMatcher`）は「入っている割合」を数えるために共有の部品を除くが、逆引きは「どこから入れ直せるか」の問いなので除かない。
  並びは送った順、同じ GUID の中は商品 ID・zip のハッシュ・entry の順。
- 上限を 1000 件にしたのは、1件は表を1回引くだけで軽く、Missing の参照は1つのプロジェクトで何十〜数百件になるため（`items/batch` の 200 より多い）。

### 持ち方とメモリ（2026-11-11 に測った）

GUID → 控えの番号だけを持つ表（`PackageGuidIndex`。GUID は 128 ビットの数で持つ）。初めて引くときに全部の控えを読んで作り、控えのフォルダの印が変わったら作り直す。
パスは当たった控えだけを読み直して取り、商品は要求ごとの写しで引く。50件をまとめて引いた時間とメモリ（Release・同じ台本・GC の後の増え）：

| 写し | 控え | GUID | 毎回全部読む | 番号だけ持つ（作る・引く・メモリ） | パスまで持つ（メモリ） |
|---|---|---|---|---|---|
| `ui` | 80（413KB） | 3,188 | 27ms | 46ms・5ms・+198KB | +1.3MB |
| `ui` の控えを25倍にした作り物（GUID を書き換えた） | 2,000（10MB） | 79,700 | 349ms | 355ms・28ms・+4.7MB | +34MB |

`stress-realcat` は控えが5つしか無く測らなかった。引く時間の多くは、控えのフォルダの印を見る列挙と、当たった控えを読み直す分。パスまで持つと7倍のメモリになるので、番号だけにした。
