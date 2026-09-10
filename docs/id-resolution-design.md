# ZIP → BOOTH 商品ID 特定 設計書

調査日: 2026-09-06 ／ 対象: `D:\storage\VRChat_*` 配下の実ZIP 18本（1.77 GiB）＋ 購入ライブラリの正解データ
共有ページ版: https://claude.ai/code/artifact/0c2fe20f-2df8-44f8-8fda-7f8bdecf153d
関連: `research/BOOTH_ZIP_LINKING_RESEARCH.md` / `research/BOOTH_ZIP_LINKING_FOLLOWUP.md`（Codex による並行調査。18本の商品IDは本書の結果と全件一致。PDF本文解析・AssetConnect CSV・BOOTH Library Manager の経路はそちらが詳しい）

## 0. 結論（先に読む）

- **cookie不要（公開情報＋ローカル解析のみ）で 18/18 を特定できた。** 当初のファイル名検索単独では 4割・誤爆ありだった。
- **ブラウザのダウンロード履歴DBだけで 10/18 を無人・オフラインで確定できた。** 配布CDNのURL `s{n}.booth.pm/<shop-uuid>/f/<商品ID>/<配布ID>/<ファイル名>` に商品IDが入っている。
- Brave は MotW（Zone.Identifier）にダウンロードURLを書かない（brave-core の意図的なパッチ）。**Chrome/Edge なら Zone.Identifier の HostUrl に上記CDN URLがそのまま残る**ので、本体の `BoothUrlExtractor` に CDN 形式を追加した。
- 精度を上げたのは「検索の改善」ではなく **検証器の追加**（`items/{id}.json`・画像dHash・`.unitypackage`名前空間・PDF本文）と **翻訳ギャップの橋渡し**（かな変換→Transliterate→Jisho）。
- **ログイン（cookie）は最終手段に後退**した。配布ツールでは自動cookie取得を機能にしない（§11）。
- 本体（BoothZipInspector）は .NET 8 の `ZipArchive` 回帰の影響を受けるため（§10）、**net9.0 に変更済み**（テスト24件合格、Milsy/Kipfel/タマクラゲの名前が全て正しく読める）。

## 1. 問題の分解

| 段階 | 内容 | 失敗の症状 |
|---|---|---|
| 発見 (Discovery) | 候補となる商品IDを生成する | 候補がゼロ（検索0件） |
| 検証 (Verification) | 候補が本当に正しいか確かめる | 隣の似た商品を誤採用（RotaMushShortHair、別店のタマクラゲ用小物） |

「危険な誤ヒット」は発見の失敗ではなく検証の欠如。検証器が安価に手に入ったので、発見は多少雑でも安全に自動確定できる。

## 2. 使えるシグナル（強い順）

| シグナル | 入手元 | cookie | 実測 |
|---|---|---|---|
| **ブラウザ履歴DBの配布CDN URL** | `History` の `downloads` + `downloads_url_chains` | 不要（ローカル） | 履歴が残る 2026-03 以降の 11 行すべてに商品ID。ローカル18本中 **10本** をファイル名＋バイト数で確定。2025-12〜2026-01 取得分（Milfy, Milsy, SDN, FREYSIA, Tori）は履歴が既に無い |
| Zone.Identifier `HostUrl` | NTFS 代替データストリーム | 不要 | 18/18 に存在。**Brave はオリジンのみ**（`accounts.booth.pm` / `booth.pm` / `hox3.booth.pm`）。Chrome/Edge は CDN のフルURL（=商品ID入り）を書く |
| ZIP内テキストのBOOTH商品URL | `.url` / readme | 不要 | 自己参照あり（hotogiya）。**依存ツール lilToon (3087170) を指す偽陽性**（Milfy, Wendy）→ 既知依存の除外か裏取り必須 |
| `.unitypackage` 名前空間 `Assets/<作者>/<商品>` | tar.gz をメモリ上で読む | 不要 | 12本中8本で作者名がショップsubdomainと一致（Kaerimichi, FREYSIA, SHOP HEILON, hotogiya, Piyo_crafts, BekoShop, FUKA, TinmeshiTei） |
| `.unitypackage` 内の Readme 本文URL | 同上（`<guid>/asset` を pathname と突き合わせ） | 不要 | 1/18（HeartBeat: 本体 5316535 ＋関連 4792153 ＋作者サイト shop.beko.ooo）。他は URL なし |
| PDF本文（利用規約・説明書） | PdfPig で本文抽出（Codex `research/PdfProbe`） | 不要 | 7/18 にPDF。ショップURL: `hotogiya.booth.pm`, **`mk22.booth.pm`（Milfy：名前空間PLUSONEでは辿れない）**, `kaerimichi.booth.pm`。固有名: Kuuta/くうた, Milfy/ミルフィ, Wendy/ウェンディ, HEILON, タマクラゲ+Piyo。ショップ共通規約だけの場合はショップ絞り込みにのみ使う |
| サムネイル dHash（64bit） | `main.png`, `BOOTH_thumbnail_*.png` ↔ JSON `images` | 不要 | 一致 0〜2 / 不一致 21〜37。同梱率 2/18 |
| 無料 variation の `file_name` | `items/{id}.json` | 不要 | exact一致で100%確定（Milsy 2本, ScreenSpaceHUD）。有料は `downloadable: null` |
| ファイル名接頭辞/末尾 = subdomain | ファイル名 | 不要 | hotogiya, iixiona, FREYSIA で直ヒット |
| BOOTH内検索 | `booth.pm/ja/search/{q}` | 不要 | 日本語名はrank1、英語/ローマ字×日本語商品名は0件 |
| Web検索（キーレス） | DDG/Bing | 不要 | DDGは1回で202ボット判定、Bingは商品ページを返さず → 実運用不可 |
| ライブラリのファイル名一覧 | `accounts.booth.pm/library` | **必要** | 購入/ギフトは全ファイル名表示、無料DLタブは非表示（商品ページ経由で取得可） |
| 購入時刻 ↔ ZIP更新時刻 | 購入履歴 | **必要** | 9件中7件が1〜2分以内に一致 |
| BOOTH Library Manager / KonoAsset のローカルDB | `%APPDATA%\pm.booth.library-manager\data.db` 等 | 不要 | このPCには存在せず（未検証）。利用者が使っていれば `registered_items.booth_item_id` を読み取り専用で取り込める（Codex 追補参照） |
| AssetConnect の CSV エクスポート | 利用者が明示的に出力 | 不要 | `URL,timestamp,boothID,title,fileName,...`。cookie を扱わずに済む取り込み手段（Codex 追補参照） |

## 3. スコアボード（18本）

| ZIP | ID | 履歴DB | cookie不要で確定した決め手 |
|---|---|:-:|---|
| Kipfel_1.2.0 | 5813187 | ✓ | 検索1位＋main.png dHash **0** |
| Wendy_ver1.01 | 7841391 | ✓ | 検索2位→thumbnail dHash **2**（1位候補は25で棄却）＋名前空間 Kaerimichi＋PDFに kaerimichi.booth.pm |
| Milfy_v1.5.0 | 6571299 |  | 検索1位＋名前空間 PLUSONE=タグ＋PDFに mk22.booth.pm |
| Milsy-ミルシィ- / Milsy_material | 7717360 |  | 無料JSONの file_name 完全一致 |
| SDN_Fullpack1.0 / SDN_Material1.0 | 7805765 |  | 検索全滅→ Zone HostUrl=hox3 →カタログ4件→「シンプル(S)だぼ(D)ニット(N)」＋variation「フルパック」 |
| FREYSIA.101 | 6142784 |  | 名前空間 FREYSIA/FREYSIA.101（101=No.101） |
| Tori_v1_1_1 | 5927710 |  | 名前空間 TinmeshiTei→tinmeshi→『Bird/鳥』、とり→鳥で検索1位 |
| hotogiya_Kuuta_ver1.03 / SchoolSweater | 4897493 / 5303781 | ✓ / ✓ | 接頭辞=subdomain→カタログ3件、内部URL自己参照、PDFに hotogiya.booth.pm |
| Kuuta_Shounen…iixiona | 7267854 | ✓ | 末尾トークン iixiona=subdomain |
| F_撫で音ギミック ×2 | 5764664 | ✓ / ✓ | 日本語名でBOOTH検索1位 |
| fullset_tamakurage.v1.06 | 6871614 | ✓ | tamakurage→たまくらげ→タマクラゲ で検索1位 |
| HeartBeatGimmick_v3.0.3 | 5316535 | ✓ | unitypackage内 Readme に本体URL／Jisho heartbeat→心音→「心音ギミック」1位 |
| Sig_Ring_07_ver2 | 3565798 | ✓ | Jisho ring→指輪→「指輪モデル」候補15件→JSONの name「Ⅶ」/ description「Ring ModelⅦ」で一意 |

注: `VRChat_clothes/SDN_Material1.0.zip` は `Milsy-ミルシィ-.zip` と SHA-256 が一致（誤名称コピー）。Codex の候補表とも18本すべて一致。

## 4. 検索の特性

- BOOTH内検索は複合語・バージョン番号に極端に弱い（`Kuuta_ShapekeyAddon` 0件、`Kuuta` 60件）。最長のラテン語トークン1語で検索し、絞り込みは検証側でやる。
- 失敗の本体は **翻訳ギャップ**（tamakurage↔タマクラゲ、HeartBeat↔心音、Ring↔指輪、Tori↔鳥）。
- **Windows の bash/curl は日本語を CP932 で送る。** 日本語クエリは必ず UTF-8 でエンコードする（PowerShell/C# は問題なし）。
- ENページ: `/en/items/{id}.json` の `name` は日本語のまま。EN検索も改善なし。`description` に英名（"Ring ModelⅦ"）が入ることがあり **検証に使える**。

### アバターの商品IDで検索する（2026-09-09 ユーザのメモ、未検証）

**VRChatユーザの常識として、対応商品を探すときはアバターの商品IDを検索欄に入れる。**
出品者が説明文に対応アバターのURL（`booth.pm/ja/items/{ID}`）を貼るので、
IDの数字列が本文に含まれ、そのまま引っかかる。

フォールバックに使えるかもしれない。**ただし向きが逆**なことに注意する：

| 使い方 | 何が手に入るか |
|---|---|
| 「このアバターに対応する衣装」を探す | **BOOTH全体から**候補が出る。手元のライブラリの話ではない |
| 手元のzipのIDを当てる | 直接は使えない。zipの名前にアバター名が入っている場合の**補助**にはなる |

つまりこれは**ID解決より「まだ持っていない商品を探す」に効く**手段。
ただし対応アバターの検出（h2のリンク）と材料が同じなので、
**検出が拾えなかった宣言をこの検索で補える**可能性がある。

**未検証。**実際に何件返るか、精度がどれくらいかを測ってから採否を決める。

### 2026-09-09 決定：アプリ内では「IDから引く」を2箇所に足す

BOOTH全体を検索しに行く話とは別に、**手元のデータだけで成り立つ入口**を先に作る。
通信が要らないので、測るまでもなく損が無い。

| 場所 | 何ができるか | 対象 |
|---|---|---|
| 検索の追加式の条件 | アバターの商品IDを入れて絞る | **登録簿にあるアバター**だけ |
| アバターの管理 | IDや名前の候補からアバターを引く | 取り込んだ商品から分かっているもの |

「VRChatユーザはアバターの商品IDで探す」という習慣に、**アプリ内でも同じ手が使える**
ようにするのが狙い。名前を思い出せなくてもIDなら分かる、という場面がある。

#### 2026-09-10 実装

**検索**：絞り込みパネルの「対応アバター」を、案内文から**入力欄**に変えた。
候補は `名前（商品ID）` の1行で持つ。候補の絞り込みは1本の文字列への部分一致なので、
**1行に両方入れておけば名前でもIDでも同じ欄から引ける。**
選ぶと既存のアバター絞り込み（素体経由の扱いを含む）にそのまま乗る——
新しい軸を作らないので、アバター管理からの導線と結果が食い違わない。

絞っている間は名前と「外す」を出す。これまでは絞りを外す手段が「条件をクリア」しか無かった。

「素体経由の対応も含める」を出すのは、**そのアバターが共通素体に属しているときだけ**
（2026-09-10 ユーザ指摘）。属していなければ経由する先が無く、どちらに倒しても結果が変わらない。
効かない選択肢を並べると、切り替えても結果が変わらないのを見て「壊れている」と読まれる。
同じ理由で、条件の要約にも素体を持つときしか「（素体経由を含む）」と書かない。

**アバターの管理**：一覧の検索が拾う語を広げた。
表示名と商品IDだけだったのを、**BOOTHの正式名と別名**にも広げている。
表示名は短くしてあるので、正式名の一部で探すと当たらない
（実データで「Kuuta3D」が「くうた」に当たらなかった）。
案内文も「アバター名で探す」から「名前かIDで探す」に変えた——
IDで引けることが読めなかった。

## 5. 日本語ブリッジ（全部キーレス）

| 段階 | 手段 | 結果 |
|---|---|---|
| ローマ字→ひらがな | NuGet `MyNihongo.KanaConverter` (`"tamakurage".ToHiragana(UnrecognisedCharacterPolicy.Skip)`) | たまくらげ / とり / ゆびわ。非日本語(kipfel→きへ)は崩れる → 候補の一つとして扱う |
| ひらがな→漢字/カナ | Google Transliterate `google.com/transliterate?langpair=ja-Hira|ja&text=` | タマクラゲ / 心音 / 指輪 / 鳥 |
| 英語→日本語 | Jisho `jisho.org/api/v1/search/words?keyword=` | ring→指輪, heartbeat→心音/心拍, bird→鳥, gimmick→ギミック, sweater→セーター |
| 翻訳API (gtx) | — | ブロックされる。不採用 |

`WanaKanaSharp` 0.2.0 は `NotImplementedException` のスタブなので使わない。

## 6. Zone.Identifier の仕様（Chromium / brave-core ソースより）

- Chromium `components/services/quarantine/quarantine_win.cc`: `HostUrl` = `SanitizeUrlForQuarantine(source_url)`。http(s) は認証情報のみ除去し **フルURLを保持**。`source_url` が空なら `request_initiator` の **オリジン**にフォールバック。`ReferrerUrl` はリファラが有効なときだけ書く。
- **brave-core** `patches/components-download-internal-common-download_item_impl.cc.patch`: `RenameAndAnnotate` に渡す source_url と referrer_url を **常に `GURL()`（空）** にしている（`// Never leak download URLs in metadata. See brave-browser#2766`）。`request_initiator` は渡す。
- したがって Brave では HostUrl = **ダウンロードを開始したページのオリジン**（ライブラリ→`accounts.booth.pm`、ショップsubdomainの商品ページ→`hox3.booth.pm`）、ReferrerUrl なし。18本すべてこの形だった。
- Chrome / Edge（無改変Chromium）では HostUrl = 配布CDNのフルURL `https://s{n}.booth.pm/<shop-uuid>/f/<商品ID>/<配布ID>/<ファイル名>?<署名>`。**商品IDがそのまま入る**。本体の `BoothUrlExtractor` に CDN 形式を追加済み（`ZoneIdentifierParser` 経由で拾う）。
- **Chrome で実証（SinAvatarPen_v1.2.2.zip, 82,308 bytes）**: ADS は `ReferrerUrl=https://accounts.booth.pm/`（オリジンのみ。Chromium の referrer policy 由来）、`HostUrl=https://s6.booth.pm/097baf76-…/f/7881802/8269912/SinAvatarPen_v1.2.2.zip?<署名>`。本体インスペクタは `BOOTH商品ID: 7881802` を出力し、Chrome の History DB も同じ item_id / downloadable_id / shop_uuid を返した。この商品は BOOTH 検索では0件だったケース。
- （訂正）当初「BOOTHのDLはFileSaver.js の blob 経由だからオリジンしか残らない」と推定したが、履歴DBの `downloads_url_chains` に `booth.pm/downloadables/{id}` → `s6.booth.pm/...` の通常リダイレクト連鎖が残っており、blob ではない。原因は上記 Brave パッチ。
- ストリームが**消える**操作（Chrome 取得の実ファイルのコピーで実測。元ファイルは無変更）:

  | 操作 | Zone.Identifier |
  |---|:-:|
  | エクスプローラーのコピー＆貼り付け（Shell.CopyHere、D:→C: 別ボリューム）／ Copy-Item | 残る |
  | エクスプローラーの切り取り＆貼り付け・名前変更（Shell.MoveHere）／ Move-Item / Rename-Item | 残る |
  | robocopy（既定 /COPY:DAT）、xcopy | 残る |
  | **エクスプローラーで ZIP を「すべて展開」→ 中のファイル** | 新しい ADS が付く：`ZoneId=3`、**`ReferrerUrl=<親ZIPのフルパス>`**、HostUrl なし。CDN URL は継承されないが、親ZIPのパスから親の ADS を辿れる |
  | **「送る > 圧縮 (zip形式) フォルダー」で作った新ZIP**、およびそれを展開した元ZIP | **消える** |
  | **7-Zip 26.00 で展開 → 中のファイル**（既定設定） | **消える**（7-Zip の「Zone.Id ストリームを伝播」設定次第） |
  | **ZIPを保持したまま中の`.unitypackage`をダブルクリックしてUnityへインポート** | 元ZIPは**完全に無変更**（サイズ・SHA-256・ADS とも同一）。Windowsが作る一時展開コピーは読み込み後に自動削除され、`%TEMP%`に残らない。実データで検証済み（SinAvatarPen_v1.2.2.zip, item 7881802。Unity Editor.log にも `Assets/nHaruka/PenSystem/...` のインポート記録が残り、名前空間解析の結果と一致した） |
  | **Unblock-File** ／ プロパティの「ブロックの解除」 | **消える** |
  | **ZIPに入れて展開**（Compress-Archive→Expand-Archive） | **消える**（展開ツールが親ZIPのMotWを伝播する場合は「親ZIPの出所」が付く） |
  | **プログラムで中身だけ読み書き**（ReadAllBytes→WriteAllBytes、ダウンローダの自前保存） | **消える** |
  | **git-bash / MSYS の `cp`**、Unix 系ツール | **消える** |
  | FAT32 / exFAT（USBメモリ、SDカード）へのコピー、非NTFSへの直接保存 | 消える（ADS を持てない。このPCでは未検証） |
  | クラウド同期（OneDrive/Dropbox/Google Drive）経由の再取得、別PCへの転送 | 消える |
  | GPO「ファイルの添付ファイルにゾーン情報を保存しない」／Attachment Manager 無効 | 最初から書かれない |

## 7. ブラウザ履歴DB（`experiments/BrowserHistoryProbe`）

- 場所: `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\History`（Chrome/Edge も同構造）。SQLite。実行中ブラウザのDBは触らず、**スナップショットにコピーして ReadOnly で開き、終了時に削除**。
- `downloads`: `target_path`, `received_bytes`, `start_time`（WebKit epoch μs）, `tab_url`（`accounts.booth.pm/orders/<注文ID>` または `/library`）, `referrer`。
- `downloads_url_chains`: `chain[0]` = `https://booth.pm/downloadables/<配布ID>`、`chain[1]` = `https://s{n}.booth.pm/<shop-uuid>/f/<商品ID>/<配布ID>/<ファイル名>?<署名付きクエリ>`。
- 照合: `target_path` は移動・改名で追えないため **ファイル名＋バイト数**で照合（`received_bytes` は実サイズと一致した）。同じ `shop-uuid` は同一ショップ（hotogiya の2本で一致）。
- 保持期間: このPCの最古行は 2026-03-15。それ以前の取得分は消えていた。
- 表示するのは BOOTH 由来の行だけ。署名付きクエリは出力しない。

## 8. `items/{id}.json`（公開・cookie不要）

主なフィールド: `name`, `shop.subdomain`, `shop.name`, `images[].resized/original`, `tags[].name`, `price`, `status`, `published_at`, `past_purchase_count`, `description`, `variations[]`。

- `variations[].downloadable.no_musics[].name` に **無料variationのexactファイル名**。有料は `downloadable: null`。
- 非公開商品でも応答する（Kipfel）。
- ショップカタログは `https://{subdomain}.booth.pm/items`（HTML, 1ページ目≈18件まで）。`items.json` は 406。検索のJSON APIやsuggest APIは存在しない。

## 9. 検証ロジック

1. 履歴DB or Chrome/Edge の Zone.Identifier に CDN URL があり、ファイル名＋バイト数一致 → 確定
2. 無料variation `name` == ZIP名 → 確定（+100）
3. 画像 dHash 距離 ≤10 → 確定級（+50）／21以上は棄却
4. 名前空間の作者 ≒ `shop.subdomain` ／ PDF内のショップURL → +40
5. 商品名トークン一致（`RotaShortHair` は一致、`RotaMushShortHair` は不一致）→ +20。**同一ショップの兄弟商品はショップでは分離できない**
6. 数字の照合: アラビア数字 ↔ ローマ数字(Ⅶ) ↔ 「No.101」。`name`/`description` の両方を見る
7. variation名 ↔ ファイル名（`フルパック` ↔ `Fullpack`）
8. 内部URLが既知の依存ツール（lilToon 等）なら手掛かりから除外。URLには `self / dependency / supported-avatar / related / creator` の役割を持たせる（Codex）
9. SHA-256 一致で重複/誤名称を検出

## 10. .NET 8 の `ZipArchive` 回帰（実測）

`ZipFile.Open(path, Read, Encoding.GetEncoding(932))` の結果:

| ZIP | 名前のエンコード | .NET 8 (cp932指定) | .NET 8 (UTF-8/既定) | .NET 9 (cp932指定) |
|---|---|---|---|---|
| Kipfel | Shift-JIS（UTF-8フラグなし） | 正常 | 文字化け | 正常 |
| Milsy / タマクラゲ | UTF-8（フラグあり） | **文字化け** | 正常 | 正常 |

.NET 7/8 は指定エンコーディングを UTF-8 フラグ付きエントリにも適用してしまう回帰があり、.NET 9 で修正された（[Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/ziparchiveentry-encoding)）。.NET 8 では両方を同時に正しく読めない。**本体・テストとも net9.0 に変更した**（代替案は中央ディレクトリの汎用ビット11を自前で読んでエントリ毎に切り替えること）。拡張子判定（ASCII）は影響を受けないが、名前ベースの照合と表示に影響する。

## 11. Tier2（ログイン）とセキュリティの判断

- BOOTH公式ヘルプ: 「商品を非公開にした場合でも、購入者はダウンロードできます」／「削除されたファイルは過去の購入者もダウンロードできなくなります」。
- 有料商品のファイル名は購入者のライブラリ／注文詳細でのみ見える。ギフトも同様。無料DLタブは一覧に名前が出ない（商品ページJSONで取れる）。
- Cookie/CDP による自動セッション利用は「借り物の権限」であり、規約リスク・偽装ツールとの区別不能・AV誤検知の点で **配布ツールの機能にしない**。利用者が自分で出力する AssetConnect CSV や、BLM のローカルDB読み取りのような **利用者主導の取り込み**を優先する。

## 12. パイプライン v3

1. ZIP内 booth 商品URL（依存除外）→ 即確定
2. **ブラウザ履歴DB照合**（ファイル名＋バイト数）／ Zone.Identifier の CDN URL → 即確定
3. ローカルシグナル収集: 名前空間 / unitypackage内Readme / PDF本文 / Zone HostUrl / 接頭辞・末尾 / サムネ候補 / バージョン・番号
4. ショップ候補が得られたら **カタログ列挙**（有界探索）
5. 発見: BOOTH検索（日本語はそのまま、英語は最長トークン）＋ ブリッジで日本語化した語
6. 全候補を `items/{id}.json` で採点（§9）
7. 閾値: 高 → 自動確定 / 中 → 候補提示 / 低 → Tier2 案内
8. 確定結果を SHA-256/ファイル名 → ID のキャッシュへ還元。新規DLは取得時に記録する（Codex H）

## 13. 実装の注意

- 数字を一律バージョン扱いしない（`FREYSIA.101`, `Sig_Ring_07`）。
- ショップカタログの件数は1ページ目のみ。18件で頭打ちならページネーションあり。
- Google Transliterate / Jisho は非公式・無保証。落ちても検索側だけで動く設計にする。
- 履歴DBは保持期間・消去・別PCで欠ける。得られたら確定、無ければ他の経路へ。

## 14. experiments/

`UnityPackageProbe`（名前空間＋Readme URL） / `ThumbnailHashProbe` / `JapaneseBridgeProbe` / `BrowserHistoryProbe`。使い方は `experiments/README.md`。PDF本文は Codex の `research/PdfProbe`（net9, PdfPig）。

## 7. 候補検索に表記の橋渡しを使うか（2026-09-10 実測）

手掛かりの無いファイルには、ファイル名から作った語でBOOTH内検索をかけている。
ローマ字のファイル名が日本語の商品を指しているとき当たらないのではないか、を実測した
（`experiments/FallbackSearchProbe`）。

**正解が分かっている10ファイル**で、検索結果に正解が上位3件以内に出るかを見た。
候補のJSONは取っていない——知りたいのは検索の当たりだけで、点数付けは通信の要らない側で測ってある。

### 1回目（今のまま）：5/10

| | ファイル名 | 検索語 | 結果 |
|---|---|---|---|
| ○ | `SinAvatarPen_v1.2.2.zip` | Sin Avatar Pen | 1位 |
| ○ | `Bracelet_tamakurage.v1.01.zip` | Bracelet tamakurage | 1位 |
| ○ | `rurune_v1.1.3.zip` | rurune | 1位 |
| ○ | `Kipfel_1.2.0.zip` | Kipfel | 1位 |
| ○ | `Wendy_ver1.01.zip` | Wendy | 2位 |
| × | `Tori_v1_1_1.zip` | Tori | **無し** |
| × | `Sig_Ring_07_ver2.zip` | Sig Ring | 無し |
| × | `HeartBeatGimmick_v3.0.3.zip` | Heart Beat Gimmick | 無し |
| × | `Kuuta_ShapekeyAddon.zip` | Kuuta Shapekey Addon | 無し |
| × | `hotogiya_Kuuta_ver1.03.zip` | hotogiya Kuuta | 無し |

### 2回目（別表記で引き直す）：+1

- ○ `Tori` → **「鳥」→ 1位**。報告されていた例がそのまま直った
- － `Sig Ring` / `Heart Beat Gimmick` → **別表記が作れないので何もしない**。通信も増えない
- × `Kuuta Shapekey Addon` → 「くうた」20位。くうた関連の商品が多く、目的の物が埋もれる
- × `hotogiya Kuuta` → 「ほとぎや」8位。同上（店名は「ほとぎ屋」で、読みは近いが商品が多い）

### 3回目（語を差し替える）：0/4

他の語を残したまま1語だけ差し替える形も試した。
「くうた Shapekey Addon」「ほとぎや Kuuta」「hotogiya くうた」——**全部外れ**。
BOOTH内検索はスペースをANDで読むので、語を混ぜるほど当たらなくなる。**この形は採らない。**

### 結論：2回目の形だけ入れる

**+1/10 に対して、10ファイルで+3リクエスト**（5件中2件は別表記が作れず通信ゼロ）。

良い性質が2つある。

- **当てられないときは黙って何もしない。**別表記が作れなければ通信も増えない
- **報告された例がちょうど直る。**ローマ字のファイル名が日本語商品を指す場合が対象

外れた3件の理由は表記ではない。
`Sig Ring` と `HeartBeat` は英訳（指輪・心音）が要るが、
**英語の経路は使わない**と決めてある（`Sin`→罪業、`Ring`→土俵 を作るため）。
`Kuuta` の2件は、商品名がラテン文字で、BOOTH側の検索の挙動（AND）の問題。

**この試験は検索の段だけを測っている。**実際の候補検索は、これに加えて
zip内の商品URL・unitypackageの作者名前空間・読みの一致（点数付け）も使うので、
端から端までの成功率はこれより高い。
