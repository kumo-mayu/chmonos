# ローカルZIPとBOOTH商品IDの紐付け調査

調査日: 2026-09-06。対象: `D:/storage/VRChat_`で始まる4フォルダ内の18 ZIP。

## 結論

実現可能。ただし「ZIPから必ず商品IDを直接読み出せる」方式ではなく、出所・配布ファイル情報・内部の作者/商品名・既知ファイルとの一致を統合する方式が適する。

今回、18 ZIPすべてに商品候補を付けられた。これは手作業のWeb調査を含む候補付けであり、18/18の自動識別精度を実証したものではない。購入履歴、配布元ファイルの厳密なバイト数、再ダウンロードしたファイルとのハッシュ比較は未検証。

既存ZIPの移行には「ローカル解析＋本人の配布ファイル一覧との照合＋候補確認」、新規ダウンロードには「取得時に商品IDを記録」が推奨構成。

## 実ファイルで確認した結果

| 項目 | 結果 |
|---|---:|
| VRChat_clothes | 4 ZIP |
| VRChat_model | 10 ZIP |
| VRChat_texture | 1 ZIP |
| VRChat_tool | 3 ZIP |
| Zone.Identifierが存在 | 18/18 |
| Zone.Identifier内に商品ID | 0/18 |
| ZIP直下階層の対象テキスト内にBOOTH商品URL | 3/18 |
| 上記のうち本体の商品を示すもの | 1/18 |
| 上記のうち依存シェーダーlilToonを示すもの | 2/18 |
| unitypackage内部から追加の商品URL | 1 ZIPで発見（本体と関連商品） |
| unitypackage内部のアセットパスを取得 | 17/18 ZIP |
| 別名だがSHA-256が一致するペア | 1組を確認 |

実行した操作は列挙、読み取り、メモリ内での解凍、2ファイルのSHA-256計算。元ZIPやADSの書き換え、ファイル移動・削除、Unityへのインポート、同梱コードの実行は行っていない。ブラウザ履歴・Cookie・購入履歴にはアクセスしていない。Web照合には商品名・作者名などの検索語を使い、アセット本体はアップロードしていない。

### 1. ADSの存在だけでは商品を特定できない

18個すべてにADSがあったが、`HostUrl`は `https://accounts.booth.pm/`、`https://booth.pm/`、`https://hox3.booth.pm/` のようなドメインまでの情報で、末尾にNUL文字が含まれていた。`ReferrerUrl`は得られなかった。

したがって、このサンプルで商品URL用の正規表現を改善しても、ADSから商品IDは復元できない。一方、`hox3.booth.pm`はショップを絞る手掛かりになった。NUL文字は表示用の解析時に除去すればよく、元ストリームを変更する必要はない。

### 2. 単一の商品URLが見つかっても本体とは限らない

`Milfy_v1.5.0.zip`と`Wendy_ver1.01.zip`のBOOTHリンクは、どちらもlilToonの商品ID `3087170`。発見元も `lilToon - lilLab - BOOTH.url`、`lilToon_shader_DL.url` だった。

本体の候補はそれぞれ [Milfy: 6571299](https://booth.pm/ja/items/6571299)、[Wendy: 7841391](https://booth.pm/ja/items/7841391)。Wendyの商品説明には `Assets/Kaerimichi/Wendy/Prefab` という導入先があり、実ZIP内unitypackageのパスと一致した。

URLの役割を `本体 / 依存先 / 対応アバター / 関連商品 / 不明` に分ける必要がある。lilToonを一律除外するのではなく、対象ファイルの内容と参照文脈から区別する。

### 3. 外側のファイル名が内容と食い違う実例

次の2ファイルのSHA-256が一致した。

- `D:/storage/VRChat_clothes/Milsy-ミルシィ-.zip`
- `D:/storage/VRChat_clothes/SDN_Material1.0.zip`

```text
SHA-256: 36D0EAB510D7C6EEB15A3BCAD6F43FE551DA71471B14B2DDB8C2E9546AC20B6F
サイズ: 21,487,380 bytes
```

衣装フォルダの`SDN_Material1.0.zip`はMilsyのZIPと同一内容。対して、モデルフォルダの同名ファイルは37,807,588 bytesで、内部に`SDN_Material1.0.unitypackage`が入っている。外側の名前だけで両者を同じ商品にしてはいけない。

[Milsyの商品ページ](https://booth.pm/ja/items/7717360)には `Milsy_material.zip`と`Milsy-ミルシィ-.zip`の両方が配布ファイルとして掲載されている。1商品に複数ZIPがある実例でもある。ページ上のMB表示は丸められているため、厳密なサイズ照合とは区別する。

### 4. unitypackageを読む価値がある

`HeartBeatGimmick_v3.0.3.zip`は既存のテキスト検査では商品URLが見つからなかった。中のunitypackageをgzip/tarとして読み、`pathname`と`asset`を対応させることで、次のReadmeから商品URLが得られた。

```text
Assets/BekoShop/HeatBeatGimmick/Readme.txt
```

- [5316535: なめらか心音ギミック](https://booth.pm/ja/items/5316535): 本体。
- [4792153: 呼吸アニメーション心拍数自動同期化Mod](https://booth.pm/ja/items/4792153): 関連商品。

`SDN_Fullpack1.0.zip`ではURLは得られなかったが、`Assets/ニット/ほみせショップ/...`と対応アバター名が得られた。これをショップの手掛かりと合わせ、[シンプルだぼニット: 7805765](https://booth.pm/ja/items/7805765)を候補にできた。

### 5. BOOTH以外のリンクにも識別力がある

`Sig_Ring_07_ver2.zip`のReadmeにはBOOTH URLがないが、`signyamo.blog/3dmodel_terms_of_use/`へのリンクがある。作者の手掛かり、`Ring_07`、Blender 3.1、使用シェーダーを商品説明と照合すると、[指輪モデル_Ⅶ: 3565798](https://booth.pm/ja/items/3565798)が有力。商品ページのver2更新記録とも整合する。

作者サイト、作者のSNS、説明書ドメインも検索候補の生成に含める。ただし共通ライセンステンプレートのURLは作者の証拠として扱わない。

## 18 ZIPの商品候補一覧

以下のパスは`D:/storage/`からの相対パス。「強」は本体URL、公開配布ファイル名と内部情報などの裏付けがあるもの。「有力」は作者・商品名・内部構造とWebの照合による推定。「補助推定」は同系列ファイル等による裏付けで、配布ファイル一覧での追加確認を勧めるもの。いずれも配布元とのバイト単位一致を保証しない。

| ZIP | 商品ID・リンク | 判断材料 | 判定 |
|---|---|---|---|
| VRChat_clothes/hotogiya_Kuuta_SchoolSweater.zip | [5303781](https://booth.pm/ja/items/5303781) | 同梱の商品名付き.urlと内部Prefab | 強 |
| VRChat_clothes/Milsy_material.zip | [7717360](https://booth.pm/ja/items/7717360) | 公開ページの配布ファイル名と内部パッケージ名 | 強 |
| VRChat_clothes/Milsy-ミルシィ-.zip | [7717360](https://booth.pm/ja/items/7717360) | 公開配布名、SHOP HEILON、商品名 | 強 |
| VRChat_clothes/SDN_Material1.0.zip | [7717360](https://booth.pm/ja/items/7717360) | 上のMilsy ZIPとSHA-256一致 | 強（Milsy判定から継承） |
| VRChat_model/FREYSIA.101.zip | [6142784](https://booth.pm/en/items/6142784) | 内部FREYSIA.101と商品説明の内容物名 | 有力 |
| VRChat_model/fullset_tamakurage.v1.06.zip | [6871614](https://booth.pm/ja/items/6871614) | タマクラゲ、Piyo_crafts、各アクセサリーのPrefab | 有力 |
| VRChat_model/hotogiya_Kuuta_ver1.03.zip | [4897493](https://booth.pm/ja/items/4897493) | 作者HOTOGIYAとKuuta本体Prefab | 有力 |
| VRChat_model/Kipfel_1.2.0.zip | [5813187](https://booth.pm/ja/items/5813187) | Assets/MOCHIYAMA/Kipfel | 有力 |
| VRChat_model/Milfy_v1.5.0.zip | [6571299](https://booth.pm/ja/items/6571299) | Assets/PLUSONE/Milfy。本体とlilToonを区別 | 有力 |
| VRChat_model/SDN_Fullpack1.0.zip | [7805765](https://booth.pm/ja/items/7805765) | hox3、ほみせショップ、ニット、対応アバター群 | 有力 |
| VRChat_model/SDN_Material1.0.zip | [7805765](https://booth.pm/ja/items/7805765) | hox3とSDNの素材パッケージ | 補助推定 |
| VRChat_model/Sig_Ring_07_ver2.zip | [3565798](https://booth.pm/ja/items/3565798) | 作者の規約URL、Ring_07、Blender/シェーダー情報 | 有力 |
| VRChat_model/Tori_v1_1_1.zip | [5927710](https://booth.pm/ja/items/5927710) | Assets/TinmeshiTei/Avatar/Tori。商品説明のTori命名とも整合 | 有力 |
| VRChat_model/Wendy_ver1.01.zip | [7841391](https://booth.pm/ja/items/7841391) | 商品説明のAssets/Kaerimichi/Wendy/Prefabと一致 | 有力 |
| VRChat_texture/Kuuta_Shounen__makeup___eye_iixiona.zip | [7267854](https://booth.pm/ja/items/7267854) | 作者iixiona、Shounen、Kuuta、内容種別 | 有力 |
| VRChat_tool/F_撫で音ギミック_どこなで拡張5_10.zip | [5764664](https://booth.pm/ja/items/5764664) | FUKA、どこなで拡張。商品ページにも同梱説明あり | 有力 |
| VRChat_tool/F_撫で音ギミック5_10_Append.zip | [5764664](https://booth.pm/ja/items/5764664) | FUKA、撫で音ギミック、Append | 有力 |
| VRChat_tool/HeartBeatGimmick_v3.0.3.zip | [5316535](https://booth.pm/ja/items/5316535) | unitypackage内部Readmeに本体URL | 強 |

複数ZIPを束ねると14商品候補になる。購入バリエーション、正確なリリース版、配布ファイルIDはこの表からは確定しない。

## 利用可能な方式と実装方針

### A. 本人のBOOTHライブラリ・注文詳細・ギフト・無料履歴との照合

大量の既存ファイルを整理する場合の第一候補。商品ごとに配布ファイル名を集め、ローカル名だけでなく内部の商品・作者情報と突き合わせる。

BOOTH公式は支払い完了メール、購入履歴、ライブラリからのダウンロードを案内している。[公式ヘルプ](https://booth.pixiv.help/hc/ja/articles/231598787--%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89-%E5%95%86%E5%93%81%E3%82%92%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89%E3%81%99%E3%82%8B%E6%96%B9%E6%B3%95%E3%81%8C%E7%9F%A5%E3%82%8A%E3%81%9F%E3%81%84)

[BoothDownloaderの公開実装](https://github.com/Myrkie/BoothDownloader/blob/master/BoothDownloader/src/Web/BoothPageParser.cs)では、本人用ページのHTML内で商品IDとダウンロードURLを同じ商品ブロックから収集し、注文詳細・ギフトにも対応している。こうした実装例はあるが、今回、本人のログイン状態での動作は検証していない。

取得項目の設計案:

```text
item_id
item_title / shop_name / shop_url
variation_id（取得可能なら）
download_file_id（取得可能なら。商品IDとは別物）
original_filename
byte_size（正確に取得可能な場合だけ）
observed_at / source_page
```

最初の取り込み手段には、ログイン済みブラウザで利用者が実行する拡張機能からJSONを書き出し、C#側で読む構成がよい。商品のブロックとその中のファイル行を対応させる。商品リンクとダウンロードリンクをページ全体で別々に収集して順番で結ぶ方法は避ける。

ファイルサイズがMBに丸められているなら補助情報として保存する。厳密サイズが必要なら、本人がアクセス可能な配布先のHTTP応答等で別途検証する必要があり、HEAD対応やContent-Lengthの存在は未確認。

無料ダウンロード履歴は2026-06-15にリリース。記録対象はリリース以降のダウンロードのみ。2026-07-14の追記では非公開商品も履歴に残るがダウンロードはできないと案内されている。古い無料ZIPの全件照合元にはならない。[公式発表](https://booth.pm/announcements/945)

制約: 古い版の削除・差し替え、ファイル名変更、同名ファイル、同一ファイルの複数商品への添付、未取得ページがある。ライブラリ内で候補が1件でも、BOOTH全体での一意性を保証しない。ギフトと無料履歴も別経路として扱う。

### B. ブラウザのダウンロード履歴

ZIPにURLが残っていなくても、ブラウザ側に残っている可能性がある。

Chromeの公式拡張APIには、保存先ファイル名、元URL、最終URL、referrer、開始時刻、バイト数等がある。`downloads.search`で既存履歴を調べられる。[Chrome downloads API](https://developer.chrome.com/docs/extensions/reference/api/downloads)

Chromiumの履歴DB実装には`downloads.target_path`、`tab_url`、`tab_referrer_url`、`referrer`と、リダイレクト経路を持つ`downloads_url_chains`がある。最終CDN URL以外に、元の商品ページや注文ページが残っている可能性がある。[Chromium実装](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/components/history/core/browser/download_database.cc)

照合案: 保存パス→ファイル名＋サイズ→開始時刻の順でローカルファイルと結ぶ。移動・改名後は曖昧さを残す。商品IDがURLになければ、配布ファイルID/URLをAの対応表に結ぶ。注文IDやダウンロードURLの数字を商品IDとして採用しない。

履歴削除、別PC、シークレット利用、ブラウザ差で情報が得られない場合がある。今回のPCの履歴は読んでいない。履歴DB方式ではブラウザ終了後のコピーか整合性のあるSQLiteスナップショットを使い、ライブDBとWALを不整合にコピーしない。

### C. ZIP・unitypackage・説明書からの証拠収集

今回の実測で効果を確認。次の順で読む。

1. ADS、外側の名前、ZIP中央ディレクトリのエントリ名。
2. Readme、.url、.md、JSON等。BOOTH URLに加えて作者サイト等も収集。
3. unitypackage内部の`pathname`。作者/商品名のあるパスと主要Prefabを優先。
4. unitypackage内のReadme等の本文。
5. PDF本文・リンク注釈。画像だけの説明書にはOCRを追加候補とする。

Unityへのインポートは不要。gzip/tarをストリームで読み取れることを実サンプルで確認した。Unity GUIDはBOOTH IDではなく、既に商品IDと結び付けたアセットとの照合用の特徴量にする。共通シェーダーや共有素材のGUID一致だけでは商品を判定しない。

今回のPDF検査は、2 MiB以下のPDF内にそのまま現れるURL文字列だけを探す簡易処理。PDF本文の完全な抽出・リンク注釈パーサー・OCRは未実施。「PDFに手掛かりがない」とは判断できない。

### D. 商品名・作者名・内部パスでWeb検索

今回、多くの候補を得た方式。外側の名前、内部の固有名、作者名から検索語を生成する。日本語/ローマ字・区切り・大文字小文字・バージョン表記の違いを吸収する。

ただし数字を一律削除すると `FREYSIA.101`や`Ring_07`のシリーズ番号を失う。版番号と商品番号を区別し、元の文字列も残す。`Kuuta`だけで検索すると衣装と素体が混ざるため、作者名・商品種別・主要Prefab名と併用する。

商品ページが見つかったら、タイトルとショップだけでなく配布名、導入先パス、内容物、画像を照合する。追加調査では `https://booth.pm/ja/items/{id}.json` を16商品について取得できた。無料商品の一部では、商品ID、バリエーションID、配布ファイルID、配布ファイル名を同時に得られる。一方、未ログイン時の有料商品では `downloadable` が空だった。このJSONは公開APIとしての互換性保証を確認できておらず、通常は「既知ID→情報取得」のためのものなので、任意のZIPやSHA-256からIDへ逆引きできるAPIとは別問題。詳細は追補レポートを参照。

### E. ハッシュ・内部ファイルの一致

今回の改名されたMilsyで有効性を確認。

- 完全なZIPのSHA-256: 改名された同一ファイル、別フォルダの複製に強い。
- ZIP内の主要ファイルのSHA-256: 外側だけ再圧縮された場合に有効。
- 内部パス＋サイズ＋CRCの組: 安価な候補絞り込み。確定の根拠には暗号学的ハッシュ等を追加。
- 主要Prefab/FBX/テクスチャの複数一致: 既知版から更新版の候補推定に使える。ただし共有アセットと改変を考慮する。

ハッシュから未知の商品IDを計算することはできない。本人の既知ファイル、取得時の記録、必要な候補だけの再取得などで「ハッシュ→商品候補」の索引を育てる。同じ配布ファイルが複数商品に使われる場合は複数候補を保存する。

配布元と同じ版を取得できればSHA-256比較は強い根拠になるが、最新版との不一致は別商品である証拠にはならない。今回、BOOTHからZIPを再取得する検証はしていない。

### F. 既存管理ソフトのデータを利用

BOOTH公式もBOOTH Library Managerを提供し、2025-12-24にWindows向けアーリーアクセス開始を発表している。[公式案内](https://booth.pm/announcements/893)

既に登録済みならBLMやKonoAssetのローカルデータから既知の商品リンク・ファイル対応を取り込む方法がある。[KonoAssetの公開リポジトリ](https://github.com/siloneco/KonoAsset)、[BLM/KonoAssetデータを読み取るALManagerの作者説明](https://booth.pm/ja/items/8580186)が参考になる。

BLMに登録していない昔のZIPが自動で逆引きできることまでは、公式案内から確認できない。既存データを読む場合も読み取り専用で、DB形式の変更を吸収するアダプターを分ける。今回、これらのローカルDBは調べていない。

### G. 画像・OCR・AIで未解決候補を絞る

商品サムネイル、同梱プレビュー画像、テクスチャ上のロゴ、説明書のOCRから作者・商品名を取り出す補助方式。画像の類似度やAIは候補の順位付けに使い、IDの確定は配布名・作者・内部構造等で裏付ける。

同じアバターを使った衣装サムネイル同士は似やすい。アバター本体と衣装の混同に注意する。今回、この方式の精度検証はしていない。

### H. これからのダウンロード時に記録する

新規分について最も確実性を上げやすい。ブラウザの商品画面で選択したファイル行と商品IDを取得し、実際に開始・完了したダウンロードと結び付ける。`chrome.downloads`のイベントはこの連携の材料になる。[公式API](https://developer.chrome.com/docs/extensions/reference/api/downloads)

保存するのは商品ID、取得可能なら配布ファイルIDとバリエーション、元ファイル名、サイズ、SHA-256、取得日時、参照した商品ページ。商品ページのDOMとダウンロードイベントを対応させ、同時ダウンロードを時刻だけで結ばない。

アプリ側DBまたはZIP横のsidecar JSONに記録する。ZIP本体を改変する必要はない。署名付きURLは期限切れや秘密情報の問題があるため、恒久的な識別キーにしない。

## 推奨するアプリの判定設計

まずローカル解析を行い、必要な場合に本人のファイル一覧・履歴・公開商品情報を追加する。

```text
ZIP/ADS/内部パス → 証拠一覧 ─┐
本人の配布ファイル一覧 ─────┤
ブラウザの取得履歴 ─────────┼→ 商品候補＋判定根拠 → 確認・保留 → 索引に保存
既知ハッシュ／既存管理DB ───┤
公開ページ検索 ─────────────┘
```

判定状態は「確定済み」「有力候補」「複数候補」「不明」を区別する。確定には利用者の確認、または信頼できる取得記録・既知ファイルとの一致等を使う。複数の弱い証拠を足しただけで自動確定にしない。外側の名前と内部パス名は同じ由来のことが多く、独立した二重の証明として数えない。

各根拠には、発見元のパス、見つかった文字列、候補ID、URLの役割、取得時刻を保存する。修正前の判定も保持すると誤判定を追跡できる。

商品とファイルは別エンティティにする。1商品に複数ファイル・複数版・複数バリエーションがあり、同じファイルが複数商品に属する可能性も許容する。今回のMilsy、SDN、撫で音ギミックが複数ファイルの実例。

## 次の検証の優先順位

1. 既存プロトタイプへunitypackageの内部パス/Readme解析と、BOOTH以外の作者URL収集を追加する。
2. 本人のライブラリ/注文/ギフト/無料履歴から配布ファイル対応表を読み取り、今回の18個で照合する。
3. ブラウザのダウンロード履歴が残っている場合に、ADSで不足した出所を補えるか検証する。
4. 上記で残る不明・衝突だけを検索/画像/手動確認へ回す。
5. 取得時記録で新規ダウンロードの紐付け漏れを防ぐ。

評価は「正しい候補が上位に入る率」「自動確定したものの正解率」「不明率」「1ファイル当たりの時間」を分ける。今回の18個は調査用の小さな集合なので、成功率を一般化しない。今回調査で見つけた商品IDをプログラムに埋め込んで同じ18個を当てても、識別方式の検証にはならない。

## 再現用の調査スクリプト

`research/Inspect-BoothSample.ps1`を追加した。既存のビルド済みInspector DLLを使用し、PowerShell 7.4以降で動作する。元ファイルは読み取りのみで、結果を標準出力のJSONとして返す。

```powershell
pwsh -NoProfile -File .\research\Inspect-BoothSample.ps1 -IncludeUnityPackages
```

今回PowerShell 7.6.5で18 ZIPを読み取り、unitypackage内部パス/URLを取得できた。既存アプリの本体コードは変更していない。

調査スクリプトは製品用の汎用アーカイブ検査器ではない。既存のテキスト抽出条件に加え、unitypackageは1エントリ2 MiB、本文読み取り予算32 MiB、宣言されたエントリ長の合計2 GiB、5万エントリの制限を設けている。上限による未読・PDF解析の制限があるため、URL未検出をURL不存在の証明にはしない。製品化時には実際の展開バイト数、時間、入れ子深度、キャンセルも制限する。
