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

GUID → 控えの番号だけを持つ表（`PackageGuidIndex`。GUID は 128 ビットの数で持つ。zip の中の絵と共通の `HashKeyedIndex`）。初めて引くときに全部の控えを読んで作り、控えのフォルダの印が変わったら作り直す。
パスは当たった控えだけを読み直して取り、商品は要求ごとの写しで引く。50件をまとめて引いた時間とメモリ（Release・同じ台本・GC の後の増え）：

| 写し | 控え | GUID | 毎回全部読む | 番号だけ持つ（作る・引く・メモリ） | パスまで持つ（メモリ） |
|---|---|---|---|---|---|
| `ui` | 80（413KB） | 3,188 | 27ms | 42〜46ms・5ms・+198〜232KB | +1.3MB |
| `ui` の控えを25倍にした作り物（GUID を書き換えた） | 2,000（10MB） | 79,700 | 349ms | 355〜453ms・26〜28ms・+4.7MB | +34MB |

`stress-realcat` は控えが5つしか無く測らなかった。引く時間の多くは、控えのフォルダの印を見る列挙と、当たった控えを読み直す分。パスまで持つと7倍のメモリになるので、番号だけにした。

## Unity プロジェクトを調べた結果（`unityscans/<場所のハッシュ>.json`）

| URL | 要求 | 答え |
|---|---|---|
| `GET /v1/unity-scans?project=` | `project` は省ける | `[{ project, savedAt, items: [{ itemId, itemName, present, total, ratio }], images: [{ itemId, itemName, projectPath, sourceName, kind }], vpm: [{ name, displayName, version, folder, inManifest }] \| null, vpmHistory: [{ name, displayName, changes: [{ from, version, removed }] }] \| null }]` |

- 画面の条件「Unityプロジェクト」と「使っている商品」が調べた結果の控え（`UnityScanStore`）。**答えの正本ではない**：プロジェクトの中はアプリの外で変わり、画面は見せた後に調べ直す。API は保存先の外を調べないので、控えをそのまま返し、いつの物かを `savedAt`（控えのファイルを書いた日時）で添える。
- 全部を並べる読み口は Core に足した（`UnityScanStore.LoadAll`）。1つずつは `Load` と同じ確かめ（中の場所とファイルの名前が合うか）を通し、手で写した控えは読まない。
- `items` は画面の並び（入っている割合の高い順）。`present`・`total` は照合（`UnityProjectMatcher`）の数で、共有の部品は数えない。`ratio` は `UnityProjectMatch.Ratio`。もう無い商品の行も消さず、`itemName` を null にする。
- `images` の `kind` は `sameImage`（大きさと中身が同じ）・`sameName`（名前だけ同じ）。画面の「名前が同じ物も含める」の切り替えは API では当てない（区別を潰さずに返す）。
- `project` は条件 `unityProject` と同じ `UnityProjectPath.Normalize` で揃え、大文字小文字を区別せずに照らす。
- `vpm` は同じ時に読んだ `Packages/` のパッケージ（plan 40・[unity-vpm.md](unity-vpm.md)）。**null は読んでいない**（この欄を足す前の控え）で、空の配列（1つも無い）と分ける。`folder` が null の物は manifest にだけあり、`inManifest` が false の物は手で置いた埋め込みの物。
  全部を読めなかった回は前の `vpm` を残すので、`savedAt` より前に読んだ物のことがある。
- `vpmHistory` は VPM ごとの履歴（2026-11-12・[unity-vpm.md](unity-vpm.md)「履歴」）。外した物も残る。`changes` は古い順で最後の行が今の状態、`from` は Chmonos が調べて気付いた日時（入れた時ではない）。
  外した行は `removed: true` で `version` が null。終わりの時は次の行の `from`。**null は履歴なし**（この欄を足す前の控えで、まだ調べ直していない）。

## ファイルのハッシュ（`scan-cache.json`）

| URL | 要求 | 答え |
|---|---|---|
| `POST /v1/files/lookup` | `{ "hashes"?: [ … ], "paths"?: [ … ] }`（合わせて1〜200件） | `{ hashes: [{ key, owners }], paths: [{ key, owners }] }`。`owners` は `GET /v1/files/lookup` の答えと同じ（見つからなければ空）。誤り 400 `invalidLookup`・`invalidIds`・`invalidLimit` |
| `POST /v1/files/locations` | `{ "hashes": [ … ] }`（1〜200件） | `{ found: [{ hash, path, sizeBytes, modifiedAt, owner }], missing: [hash] }`。誤り 400 `invalidHashes`・`invalidIds`・`invalidLimit` |

- まとめて引く道は、1件ずつの GET と同じ関数（`Catalogs.LookupFiles`）を送った順に当てるだけ（同じ問いの答え方を2つ持たない）。外した物も状態つきで返すのも同じ。
- 場所を引く道は、取り込みがハッシュを数えた控え（`ScanCacheEntry`：場所・大きさ・更新日時・ハッシュ）を読む。場所は最後に数えたときの物で、今そこに在るかは見ない。ドライブ文字は読み替えない（[local-api.md](local-api.md)「未決」）。
- **出す範囲**：控えには登録したフォルダの中で数えた全部の物が載るが、出すのは**商品のファイル（`owner: item`）と、商品が決まっていないファイル（`owner: unresolved`）の場所だけ**。
  外したファイル（使う人が「資産ではない」とした物）と、どこにも属さない物は出さず、`missing` に入れる（控えに無い物と見分けない。出さない物があることを答えから推せないように）。
  守りは今の `GET /v1/files/lookup` と同じ（`127.0.0.1` だけ・トークン必須・送られたハッシュも答えもログに残さない）。商品が決まっていないファイルの場所は、未確定の自動検索の控え（下）の `target` でも出る物と同じ範囲。
- 控えが読めない（手で壊した）ときは無いのと同じにする（作り直せる控えなので答えを止めない）。控えは要求ごとに読む（今の写しの規模で数十KB。大きくなったら持ち方を測り直す）。

## zip の中の絵の目録（`zipimages/<zip のハッシュ>.json`）

| URL | 要求 | 答え |
|---|---|---|
| `POST /v1/zip-images/lookup` | `{ "images": [{ "crc": "16進8桁", "size": バイト数 }] }`（1〜1000件） | `{ found: [{ crc, size, path, itemId, itemName, zipHash, fileState }], missing: [{ crc, size }] }`。誤り 400 `invalidImages`・`invalidImage`・`invalidLimit` |

- 使い道：Unity のプロジェクトに手で置いた画像（名前を変えた物も）が、どの商品の zip から来たかを引く。zip を消した・動かした後も、目録が残っていれば引ける。
- 照らし方は画面の「手で置いた画像を見つける」（`UnityProjectImages`）と同じ：**大きさが同じで CRC-32 が同じ物**を同じ画像とする（大きさだけ・名前だけの一致は返さない）。CRC は zip の目次の値なので、外の道具はファイルの CRC-32 を自分で数えて送る。
- 表は GUID と同じ持ち方（鍵は大きさと CRC を並べた 128 ビット。`HashKeyedIndex`）。`ui` の写しの目録は7つで、測るほどの差が出ないので測っていない。
- CRC は大文字の16進8桁に揃えて返す（目録の書き方）。同じ画像が複数の zip・商品にあれば全部返す。

## 通知の保管庫（`notification-archive/<YYYY-MM>.jsonl.gz`）

| URL | 要求 | 答え |
|---|---|---|
| `GET /v1/notifications/archive` | なし | 保管庫にある月（`YYYY-MM`・新しい順。画面の月を選ぶ欄と同じ） |
| `GET /v1/notifications/archive/{month}?name=&cursor=&limit=` | `limit` は 1〜200（既定 100） | `{ month, items: [通知], total, nextCursor, unreadableLines, unreadableBlocks }`。誤り 404 `monthNotFound`・400 `invalidCursor`・`invalidLimit` |

- 画面の「保管した通知」（plan 24・[notifications.md](notifications.md)）と同じ読み口：月の目録（`IndexAsync`）・商品名で絞る（`Matching`）・1ページ分だけ中身を読む（`ReadEntriesAsync`）。並びも同じ（最後に変わった日時の新しい順・同じ日時は後から保管した物が先）。
- 通知1件の形は `/v1/notifications` と同じ（`Catalogs.NotificationOf` を共有）。保管した物は既読か解消済みだけ。
- 月はある物だけ受ける（送られた文字で場所を組まない）。月をまたいで並べる道は作らない（画面も月ごと。月を流し直す重さが月の数だけ増える）。
- 続きの印は最後に返した1件の（日時・行の位置）。並びはこの2つで一意。中身は約束にしない。
- 目録は最後に読んだ1か月分だけ、月のファイルの大きさと日時が同じ間は使い回す（`ArchivedNotifications`）：ページを送るたびに月を頭から流し直すと、大きな月（容量の既定は 50MB）では1ページごとに全部を解くことになる。目録は位置・ID・題・日時だけで本文を持たない。写しに保管庫のある物が無く、大きさは測っていない。
- 容量を超えたときの整理（閉じるときなど）で月が書き直される。書き直しの後は目録を作り直すので、前の続きの印は同じ（日時・位置）より後ろから続く（行の位置が変わると、飛ぶ・重なることがある。画面も同じ）。

## 未確定の自動検索の控え（`unresolved-searches.json`）

| URL | 要求 | 答え |
|---|---|---|
| `GET /v1/unresolved/candidates?hash=` | `hash` は省ける | `[{ target, files: [hash], searchedAt, candidates: [{ itemId, name, shopName, shopSubdomain, score, isStrong, reasons }] }]` |

- 先回り（plan 21）と人が押した「再検索」が書く控え（`CandidateSearchCache`）をそのまま返す。1件は検索した対象ごと（zip の中身の束は全部が同じ元の zip で引くので、ファイルごとには写さない）。`hash` を渡すと、そのファイルを `files` に含む控えだけ。
- **候補は推定**。API から確定はしない（未確定の解決は画面に残す：[local-api.md](local-api.md)「出す範囲」）。`isStrong` は Core の `ResolutionCandidate.IsStrong`（点数から出す値も返す）。`reasons` は画面に出す文のまま。
- `target` は未確定のファイルの場所で、ハッシュから場所を引く道の「商品が決まっていないファイル」と同じ範囲。
- 並びは対象の場所の順、候補は控えの順（画面と同じ）。控えが読めなければ空で返す。
- 未確定のファイルの一覧そのもの（`/v1/unresolved`）は今回も出さない（場所・中身の一覧・ダウンロード元の記録を持つ大きな記録で、何を出すかを別に決める要がある。plan 32 の範囲外）。
