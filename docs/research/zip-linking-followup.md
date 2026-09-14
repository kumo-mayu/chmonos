# BOOTH ZIP紐付け調査・追補

> **要約（2026-09-14 に足した。中身は書かれた時点のまま。`zip-linking.md` の続き）**
> - **目的**：前回の18本を、公開情報とローカルの解析だけでどこまで自動で結べるか。
> - **方法**：公開商品JSON（16商品）・PDF本文（`research/PdfProbe`・PdfPig）・SHA-256 を集め、`research/Compare-CatalogEvidence.ps1` でオフライン照合した。
> - **結果**：自動で候補を付けられたのは 18本中3本。競合0・自動確定0。PDF は 7/18 にあり、商品固有名の証拠になる（直接の商品URLは0件）。有料商品の配布ファイル名は、未ログインの JSON では取れない。
> - **結論**：既存 zip を無人で100%確定する方式は無い。証拠を役割付きで保存し、confirmed／likely／candidate／unresolved／conflict に分ける。取り込み経路の候補は、BOOTH Library Manager のローカルDB（読み取り専用）と AssetConnect の CSV（利用者が出した物）。
> - **今の決め事**：`docs/spec/id-resolution.md`
> - **節**：結論／今回増えた証拠／有望な取り込み経路（1 BLM・2 AssetConnect・3 利用者主導のエクスポート・4 公開JSON）／ローカル解析の改善（PDF・.NET 8 の zip）／判定ルール／データモデル案／実装順序／追加した再現物

調査日: 2026-09-06。前回と同じ `D:/storage/VRChat_*` 配下18 ZIPを読み取り専用で再調査した。

## 結論

実現可能性は高い。ただし、既存ZIPを100%無人で商品IDへ確定する単一方式は見つからなかった。実装は次の2経路に分けるのが堅実である。

1. 新規ダウンロードは、ダウンロード時に商品ID・配布ファイルID・実ファイル名・SHA-256を記録する。この経路は高い確度で自動確定できる。
2. 過去ZIPは、BOOTH Library Manager、利用者が明示的に出力したダウンロード履歴、公開商品情報、ZIP内部証拠を統合し、確定・候補・保留に分ける。

候補集合を人手で先に特定した今回の18 ZIPでは、全てに商品候補を付けられている。ただし、新しい自動照合プローブが公開情報だけで名前候補を付けたのは2 ZIP、同一SHA-256から候補を継承できたものを含めて3 ZIPだった。残り15 ZIPはPDF、unitypackage内部パス、商品説明、ショップ等の複合照合または本人のライブラリ情報が必要である。したがって18/18を自動精度の実績として扱ってはいけない。

## 今回増えた証拠

| 証拠源 | 実測結果 | 用途 |
|---|---:|---|
| 公開商品JSON | 候補16商品を16/16取得 | タイトル、ショップ、タグ、バリエーションの索引 |
| 公開JSON内の配布ファイル名 | 3商品で取得 | ローカルZIP名との完全一致候補 |
| PDF解析 | 7/18 ZIP、31文書、184ページ、エラー0 | 商品名・ショップ・作者の独立証拠 |
| PDF内の直接商品URL | 0件 | URL抽出だけでは不足することの確認 |
| 全ZIPのSHA-256 | 18/18 | 既知ファイルとの完全一致、重複検出 |
| 同一SHA-256の別名ZIP | 1組 | 誤ったファイル名を越えて候補を継承 |

無料商品 `7717360` の公開JSONでは、次の対応を実際に取得した。

| 種別 | 値 |
|---|---|
| 商品ID | `7717360` |
| バリエーションID | `12924073` |
| 配布ファイルID | `8091370`, `8091672` |
| 配布名 | `Milsy_material.zip`, `Milsy-ミルシィ-.zip` |

この2つはローカル名と完全一致する。ただし、名前の完全一致だけでは同名再利用や内容更新を排除できないため、現在のプローブは「候補」とし、自動確定は0件にしている。`VRChat_clothes/SDN_Material1.0.zip` は後者とバイト数・SHA-256が完全一致するため、ファイル名より内容を優先して `7717360` の候補を継承できる。

有料商品の例 `6571299` と `7805765` は、未ログインのJSONではバリエーションを得られても `downloadable` が空だった。公開JSONだけで有料配布ファイル一覧を作る設計にはできない。無料ファイルのダウンロードURLも未ログインではサインインへ302リダイレクトされたため、アプリが勝手に認証情報を集める方式は採用しない。

## 有望な取り込み経路

### 1. BOOTH Library Managerのローカル索引

BOOTH公式はWindows向けBOOTH Library Managerのアーリーアクセスを案内している。[公式告知](https://booth.pm/announcements/893)

公開されている閲覧ツールの実装では、既定のDBは `%APPDATA%/pm.booth.library-manager/data.db` で、SQLiteをReadOnlyで開き、`registered_items.booth_item_id` と `booth_items.id` を結合している。保存先は `preferences.item_directory_path`、各商品の実フォルダは `registered_item_id` 配下として解決される。[読取実装](https://github.com/32ba/unity-booth-library-manager-viewer/blob/master/Editor/BoothDatabaseReader.cs)

つまりBLMを利用済みなら、フォルダ名を商品IDだと仮定する必要はなく、DBの明示的な関連を取り込める。製品実装ではDBを直接常用するより、次を守る。

- 利用者がBLM連携を有効にした場合のみ読む。
- ReadOnly接続を使い、スキーマバージョンと列の存在を検査する。
- `registered_item_id` とBOOTH商品IDを別のIDとして保存する。
- BLMの保存フォルダ内ファイルをハッシュ化し、散在している既存ZIPと比較する。
- DBロック中はリトライせず、整合した読み取りスナップショットを使う。

今回、このPCのBLM DBにはアクセスしていない。上記は公開ソースから確認した実装可能性である。

### 2. AssetConnectの明示的なCSVエクスポート

AssetConnectはBOOTHの商品ページ、ライブラリ、注文、ギフトのダウンロードを記録し、CSVを出力できる既存のブラウザ拡張である。[公式サイト](https://assetconnect.sakurayuki.dev/)、[公開リポジトリ](https://github.com/yuki-2525/AssetConnect)

公開ソースでは、ダウンロード行と同じ商品コンテナから `boothID` とファイル名を取得し、CSVに `URL,timestamp,boothID,title,fileName,free,registered` を出力している。[ライブラリ側の記録処理](https://github.com/yuki-2525/AssetConnect/blob/main/download-log/booth_library.js)、[CSV出力処理](https://github.com/yuki-2525/AssetConnect/blob/main/storage-management/storage-overview.js)

今回追加したオフライン照合スクリプトは、このCSVを任意入力として読める。ブラウザプロファイルやCookieを直接読む必要がなく、利用者が自分で出力したファイルだけを取り込むため、最初の連携手段として扱いやすい。

### 3. BOOTHライブラリの利用者主導エクスポート

BOOTHは2026-06-15から無料商品のダウンロード履歴を段階的に提供しているが、告知以前の取得は履歴に入らない。[公式告知](https://booth.pm/announcements/945)

購入・ギフト・無料履歴を補う場合は、ログイン済みページ内で利用者が明示的に実行する拡張機能から、商品コンテナ単位で商品IDとファイル行をJSON/CSVへ出す。既存OSSでもライブラリの各商品ブロックから商品IDとダウンロードURLを集め、注文・ギフトを別経路で補完しているが、注文HTMLは壊れやすいと明記されている。[BoothDownloaderの解析実装](https://github.com/Myrkie/BoothDownloader/blob/master/BoothDownloader/src/Web/BoothPageParser.cs)

セッションCookieをアプリへコピーしたり保存したりする方式は避ける。ページHTMLのクラス名に依存する処理は、DOM fixtureテストと「取得できなければ保留」の動作が必要である。

### 4. 公開商品JSON

`https://booth.pm/ja/items/{商品ID}.json` は、既知IDから商品名、ショップ、説明、画像、タグ、バリエーションを得る補助経路として使える。今回の観測例は [7717360.json](https://booth.pm/ja/items/7717360.json)。Avatar ExplorerやAssetConnectも同形式を利用している。[Avatar Explorerの説明](https://github.com/puk06/VRC-Avatar-Explorer/blob/main/docs/05-booth-integration.md)、[AssetConnectの取得処理](https://github.com/yuki-2525/AssetConnect/blob/main/background/background.js)

ただし、公開された外部開発者向けAPI契約として確認できていないため、キャッシュ、間隔制御、スキーマ検証、失敗時の再試行/保留が必要である。これはSHA-256から商品IDを引く逆引きAPIではない。

## ローカル解析の改善

PDFは単なるURL検出より有効だった。例として、Kuuta、Milfy、Wendyの規約には各商品固有の名称があり、タマクラゲの操作説明にも商品名と作者があった。一方、SchoolSweaterとMilsyの一部規約はショップ共通表記であり、商品確定には使えない。PDF証拠は次のように分類する。

- 商品固有名とショップが両方一致: 強い候補証拠。
- 商品固有名だけ一致: 中程度。表記ゆれを表示して確認させる。
- ショップ共通規約だけ一致: ショップ候補の絞り込みにのみ使う。
- PDFに現れた別商品名/URL: 依存・対応・作例の可能性を残す。

PdfPigでは `page.Text` を直接使わず、レイアウトを考慮した `ContentOrderTextExtractor` を使うよう変更した。[PdfPig](https://github.com/UglyToad/PdfPig)

また.NET 8は、`ZipArchive`へCP932を指定するとUTF-8フラグ付きの名前にも指定エンコーディングを適用する不具合がある。実際、タマクラゲZIPの7 PDF中4 PDFしか対象にならず、名前も文字化けした。.NET 9でUTF-8フラグが尊重され、7 PDF全てを読めた。[Microsoftの互換性情報](https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/ziparchiveentry-encoding)

製品側のZIP列挙もnet9以降へ上げるか、中央ディレクトリのフラグを尊重する独自デコーダをテストする必要がある。これは表示上の問題だけでなく、拡張子判定による解析漏れにつながる。

## 判定ルール

証拠を加点だけで混ぜず、まず役割と由来を保存する。

| 判定 | 必要条件の例 | 自動処理 |
|---|---|---|
| confirmed | ダウンロード時に記録したIDと完成ファイルのSHA-256、またはBLMの明示的関連＋同一ファイル | 自動確定可 |
| likely | 本人履歴のID＋配布名完全一致＋サイズ整合、または既知配布物とのSHA-256一致 | 確認付き確定 |
| candidate | 公開配布名一致、商品固有PDF名、商品説明とunitypackageパス一致 | 候補表示 |
| unresolved | ショップ名のみ、一般語のみ、依存URLのみ | 保留 |
| conflict | 独立した強い証拠が複数IDを指す | 自動確定禁止 |

URLには `self / dependency / supported-avatar / related / creator / unknown` の役割を持たせる。今回のMilfyとWendyに含まれるlilToon URLのように、最初に見つけた商品URLを本体IDとみなしてはいけない。

同じSHA-256は非常に強いが、その候補元が名前推定だけなら確度も継承元どまりである。「SHA-256一致だからconfirmed」ではなく、「何と一致したか」を記録する。

## 推奨データモデル

ZIPと商品を直接1対1の列だけで結ばず、観測事実を別テーブルにする。

```text
LocalFile(path, size, sha256, observed_at)
CatalogItem(item_id, title, shop_subdomain, source, observed_at)
Distribution(item_id, variation_id, downloadable_id, filename, display_size)
Evidence(local_file_id, candidate_item_id, kind, source, value, observed_at)
Decision(local_file_id, item_id, state, decided_by, decided_at)
```

これにより商品更新、同一商品の複数ZIP、1 ZIP内の複数商品、誤判定の訂正、候補根拠の表示に対応できる。元ZIPは変更せず、決定はアプリDBまたはsidecarへ保存する。

## 実装順序

1. 証拠テーブルと `confirmed/likely/candidate/unresolved/conflict` を先に実装する。
2. AssetConnect CSV importerとBLM read-only importerを追加する。
3. 既存のZIP、unitypackage、PDF解析をcatalog候補へ結合する。
4. 利用者が候補と根拠を確認・修正できる画面を作る。
5. 新規ダウンロード用に、商品ID・配布ID・完成ファイルSHA-256を同時記録する連携を追加する。
6. 公開JSONはキャッシュ付きの補助providerとして扱う。

最小実用版は1〜4で成立する。5を入れると将来分の曖昧さを大幅に減らせる。

## 追加した再現物

- `PdfProbe/`: ZIPを展開せずPDF本文とSHA-256を観測するnet9プローブ。
- `Get-PublicCatalog.ps1`: 手作業で得た有限の候補IDだけを公開JSONから取得する。
- `Compare-CatalogEvidence.ps1`: 公開配布名、任意のAssetConnect CSV、同一SHA-256をオフライン結合する。
- 生成JSONは `.gitignore` 対象。元ZIP、ADS、ブラウザ、BLM DBは変更しない。

今回の実測では `Compare-CatalogEvidence.ps1` が18 ZIP中3 ZIPに候補を付け、競合0、自動確定0と保守的に判定した。残りを無理に自動確定しないことが、この種の管理ソフトでは重要である。
