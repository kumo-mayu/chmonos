# 外から使う API：内部の情報を出す物と、その一覧の形

> **要点**：今 API が出していない内部の情報を「出す・出さない・後回し」に仕分けた表（2026-11-27）と、出すと決めて作った一覧（recent・folders・excluded・unresolved・取り込みの進み具合・変換の組・保存した検索）の形。
> URL の一覧と商品の答えの形は [local-api-shapes.md](local-api-shapes.md)、共通の決まりは [local-api.md](local-api.md)。2026-11-27 に local-api-shapes.md から移した（1ファイル200行の決まりのため）。

## 出す物を決めた（2026-11-27・ユーザ判断「おすすめを採用します」）

今 API が出していない内部の情報を1つずつ仕分けた。物差しは [local-api.md](local-api.md) の「出す範囲 B」（読む事は広く出す）・「既定は結果だけ、帳簿は `?include=`」・画面と段取りの内部の状態は出さない（出すと形を変えにくい）。作ったかは表の「出し方・理由」の頭の［実装］で示す。

| 物 | 決め | 出し方・理由 |
|---|---|---|
| 商品のバリエーションの向き先（`variationTargets`・`detectedVariationTargets`） | 出す | ［実装 2026-11-27］既定の `variationTargets` は合わせた結果（人が決めた物が勝つ。`AvatarCompatibilityIndex.EffectiveVariationTargets`・設定「名前から見つけた向き先も使う」に従う）。出どころ別は `include=variationTargets` で `variationTargetSources` |
| 商品の変換扱い（`conversion`） | 出す | ［実装 2026-11-27］既定の `conversion`：人が決めた扱い `mode`（`auto`・`profile`・`notProfile`）・自動の判定 `hasProfileFiles`（画面と同じ `ConversionScan.ProfilesOf`）・つなぎ先 `targets`（状態 `confirmed`・`candidate`。人が外した物は入れず、確認待ちは入れる。対応アバターの `compatibleAvatars` と同じ・ユーザ判断 2026-11-27「揃えた方が良い」）。外した物も含む帳簿は `include=conversionTargets`。扱いと判定を合わせた実質の扱い `effectiveMode`（`profile`・`notProfile`）は画面の「今の扱い」と同じ Core の `ConversionTreatments.Of` で組む |
| 商品の中身の更新の控え（`contentUpdate`） | 出す | ［実装 2026-11-27］`include=contentUpdate` で `contentUpdate`：`foundAt`（見つけていなければ null）・`hasNewerFiles`（`LocalBlock.HasFilesNewerThanUpdate`） |
| 商品の更新を知らせるか（`notifyOnUpdate`） | 出す | ［実装 2026-11-27］既定の `notifyOnUpdate` |
| 商品の再取得の予定（次に取る時・続けて見つからなかった回数） | 出さない | 取り込みの内部の段取りで決まりが変わり得る。最後に取った日時は `include=meta` で出ている |
| `/v1/folders`・`/v1/unresolved`・`/v1/excluded`・`/v1/recent` | 出す | ［実装 2026-11-27：folders・excluded・recent・unresolved。形は下の「内部の一覧」］未確定・外した物は読むだけ（解決は画面に残す）。最近は書く口があるのに読めなかった |
| `/v1/saved-searches` | 出す | ［実装 2026-11-27。形は下の「内部の一覧」］人が作った検索をソフトが使い回す。保存の形から API の条件へ戻す変換を作る |
| `/v1/search-history`・`/v1/stats`・`/v1/volumes`・`/v1/search/explain` | 後回し | 履歴は使い道が薄い。統計は画面の計算を Core へ移す要がある。ドライブは生の組より、読み替えを済ませて今の場所を返す方が役に立つ（[local-api.md](local-api.md)「未決」）。記法を外から使う場面がまだ無い |
| 人が足した変換の組（`conversion-pairs.json`） | 出す | ［実装 2026-11-27］`GET /v1/conversion-pairs`（形は下の「内部の一覧」）。外の変換ツールに大事な情報。組はアバター・素体どうしの物で商品に属さないので、商品の `conversion` には入れず1本の一覧だけにする |
| 取り込みの進み具合（`import-state.json`） | 一部を出す | ［実装 2026-11-27］今の `busy` に「済んだ数／全体」だけを足す。続きに要る中身は出さない。形は下の「内部の一覧」の後の段落 |
| 設定（`settings.json`） | 出さない | 要る物を1つずつ足す（`/v1/display-settings` の形） |
| 画面の状態・編集の位置・登録の待ち列・途中の操作 | 出さない | 画面と段取りの内部で、形が変わる |
| バナー・動画の題・新しい版の確認 | 出さない | 機械の控え。動画の題は YouTube の情報の配り直しになる |
| 控え（`format-backups`・`items/.prev`）・よけた記録・ログ・検索用の控え | 出さない | 中身の守り（ログ）と内部の物 |

作る順（軽い物から）：① 済み：商品の欄（2026-11-27） ② 済み：excluded・recent・folders（2026-11-27） ③ 済み：unresolved・取り込みの進み具合（2026-11-27） ④ 済み：変換の組（2026-11-27） ⑤ 済み：保存した検索（2026-11-27）。どれも項目を足すだけで `/v1/` のまま。


## 内部の一覧（2026-11-27）

上の「出す物を決めた」の ②・③・④・⑤。どれも読むだけで、どのトークンでも読める。写しから組み（要求のたびにファイルを読まない。unresolved だけは別で、表の通り）、`cursor`・`limit`（1〜200・既定100）で分けて `{ items, total, nextCursor }` で返す。続きの印は最後の1件の並べる鍵で、別の URL の印は `invalidCursor`。

| URL | 1件の形 | 並び・決まり |
|---|---|---|
| `GET /v1/recent` | `{ itemId, itemName, addedAt, usedAt, viewedAt }` | 商品 ID の順（3種類の日時のどれで並べても1つに決まらない。種類で絞って新しい順は検索の条件 `recent`）。値は条件「最近」と同じ `RecentActivity.Times`（同じ商品の行が2つなら種類ごとに新しい方）。写しに無い商品の行（消した直後・起動時の整理の前）も返し、`itemName` を null |
| `GET /v1/folders` | `{ itemId, itemName, path, state, missingSince, fileCount, totalBytes, unityPackages, registeredAt }` | 場所の順（大文字小文字を区別しない→区別する）・商品 ID・商品の中の位置。「登録したフォルダ」は商品として登録したフォルダ（`LocalFolders`。最初の案 2026-10-12 の言葉。設定の取り込み元の履歴・監視するフォルダは設定なので出さない）。形は商品の詳細の `folders` と同じ `ItemMapper.Folder`、`state`（`present`・`missing`）は `files/lookup` と同じ `ItemMapper.FolderState`（取り込みが見た `missingSince` だけ・ディスクは見ない） |
| `GET /v1/excluded` | `{ hash, paths, excludedAt, source }` | 画面（設定の「除外したファイル」・Core の `LoadExcluded`）と同じ、外した日時の新しい順・同じ日時は記録の後ろが先。印は記録の位置を持つので、間に戻すと飛ぶ・重なることがある（保管した通知と同じ）。`paths` は全部（画面は1つ目だけ）、`source` は外した操作の出どころの英字の値（`resolve`・`resolveArchive`・`resolveBulk`・`folderView`、表に無い文は `other`、文の無い記録は null。ユーザ判断 2026-11-27・案 A）。記録は日本語の文のまま書き続け（配った版と同じ）、文と値の対応は Core の `ExcludeSources` の1か所。記録は見張りの部分 `excluded`（`/v1/changes` にも出る）で、壊れて読めなければ前の記録で答える |
| `GET /v1/unresolved` | `{ hash, paths, sizeBytes, modifiedAt, firstSeenAt, archiveBroken, zoneHostUrl, zoneReferrerUrl, candidateItemIds, samePathItemIds, notOnBooth: { itemId, checkedAt } \| null }` | 最初の場所の順（大文字小文字を区別しない→区別する）・ハッシュ・記録の位置。**zip の中の一覧（`contents`）は出さない**（記録の大半で、8万件で 35MB になった）。`candidateItemIds`・`samePathItemIds` は推定で、決めるのは画面。自動検索の控えは `hash` で `/v1/unresolved/candidates` を引く。記録は大きくなり得るので写しに持たず、初めて頼まれたときに読み、大きさと日時が同じ間は使い回す。変わると見張りの部分 `unresolved` で知らせる。壊れて読めなければ前に読めた物で答え、一度も読めていなければ 503 `unresolvedUnreadable` |
| `GET /v1/conversion-pairs` | `{ from: { avatarId, baseName }, to: { avatarId, baseName }, tool, memo }` | 人が足した順（記録の位置。画面の並び「追加が古い順」と同じ）。向きは元→先の1方向（`from` に対応する衣装を `to` に着せられる・[conversion.md](conversion.md)「人が足す組」）。端はアバターの商品 ID `avatarId` か共通素体の名前 `baseName` のどちらかで、もう一方は null（商品の `conversion.targets` と同じ名前・持ち方。名前は付けず、アバターの名前は `/v1/avatars` で引く）。`tool`・`memo` は無ければ null。記録の行はそのまま全部返す（端が空の行も。計算に使わない行かは端を見て分かる）。組で着せられる衣装は、検索の条件 `avatar` の `viaConversion: true` の答えに既に混ざる（画面の「変換経由を含める」と同じ `ConversionIndex`）。この一覧はその元になる組そのもの。記録は見張りの部分 `conversionPairs`（`/v1/changes` にも出る）。足す・外すは画面だけ |
| `GET /v1/saved-searches` | `{ name, folder, summary, text, sort, view, keepSort, keepView, search: { where, textOptions }, unconverted: [{ kind, reason }], inCycle }` | 人が決めた並び（記録の位置。画面の一覧と同じ）。`search` は **`POST /v1/search` の本文にそのまま入れられる**式（`SavedSearchExport`。`ConditionKinds.ToState` の逆向き。**`search` の中は値のある欄だけ**を書く＝答えの「null も省かない」決まりの例外。条件1つに null の欄が約40並び読みにくいため・ユーザ判断 2026-11-27。外側の欄は null も書く）：検索文字列は `text` の葉、条件は種類と値を API の条件の形で、全部を `and` でつなぐ（何も絞らなければ `where` は null）。照らす単位は条件 `savedSearch` と同じ（効かせていない・絞らない条件は入れない。範囲・日付の同じ種類は `or`、除くは `not`）。入れ子の保存した検索は中を開かず `savedSearch` の条件のまま。`textOptions` は文字がある時だけ（かなの区別も明かに書く）。戻した式で探した答えが条件 `savedSearch` と同じことを試験で確かめる（`SavedSearchApiListTests`）。**書けない物は式から落とし、`unconverted` で言う**（落とした分だけ答えが画面より広い）：`unsupportedKind`（API に無い種類：中に入っているか見る「Unityプロジェクト」・知らない種類）・`unsupportedValue`（頭の無い対応アバターの値など）・`recordlessRecent`（「最近」の記録の無い商品も表示）・`searchAlternates`（別表記。API は辞書を持たず広げない）。`sort` は表示順の名前（画面の言い方。null の記録は「入手日が新しい順」）で、API の `sort` には変えない。`view` は `card`・`list`。表示順・形を含めずに保存した物は `keepSort`・`keepView` が true で `sort`・`view` は null。検索文字列を含めずに保存した物は `text` が空（文字の無い保存と見分けない。持っていない）。`inCycle` はたどると自分に戻る物（画面も条件も何にも当てない）。記録は見張りの部分 `savedSearches`（`/v1/changes` にも出る）。保存・上書き・並べ替えは画面だけ |

取り込みの進み具合（③）：`/v1/version` に `progress: { done, total } | null` を足した。`busy` が `import` の間だけ、取り込みが①の1件ごとに書く `import-state.json` の `done`・`total`（商品の情報を取れた数／取ろうとしている数。画面の「n / m 件」と同じ）をそのまま返す（`ImportProgress`）。走査の途中（0 / 0 で「取る物が無い」と見分けられない）・取る物が無い・始めた時より前に書かれた記録（前に中断した回の数）・読めない記録は null。SSE の `busy` には載せない（始まりと終わりだけ）。
