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
| `.unitypackage` 名前空間 `Assets/<作者>/<商品>` | tar.gz を展開せずに流して読む（本体は写さない。1GB の物で確保 1MB。2026-09-13 までは本体まで写していて約2GB確保した。読みは裏で行う。アプリの未確定で 1.1GB の zip の自動検索を押すと、直す前は画面が 3.4 秒止まり作業セットが約300MB増えた。直した後は 33ms を超える固まりが0回で、増えは約30MB。交互に2巡測った） | 不要 | 12本中8本で作者名がショップsubdomainと一致（Kaerimichi, FREYSIA, SHOP HEILON, hotogiya, Piyo_crafts, BekoShop, FUKA, TinmeshiTei） |
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

### 6-1. 展開したときの記録で、未確定を元zipごとに束ねる（2026-09-11）

「すべて展開」が中のファイルに書く `ReferrerUrl=<親ZIPのフルパス>` は、**zipを消した後も、展開先を別のドライブへ移した後も残る。**
未確定画面はこれで束ねる（`Core/Scanning/UnresolvedOrigin.cs`）。元zipの束は畳んで出し、「このzipを選ぶ」でその全件を1つの対象にする
（確定・管理から外すが全件に効き、自動検索は元zipの名前で引く）。開けば1件ずつ選べる。元zipが分からないものは従来どおりフォルダで束ねる。

- **書き方の癖**：CP932（システムの ANSI コードページ）で書かれ、末尾に NUL が付く。旧版はこれを UTF-8 として読み、
  日本語を U+FFFD に化かしたまま `unresolved.json` に保存していた。**画面を開くたびに、手元にあるファイルの記録を直した読み取りで読み直す**。
  保存値は、ファイルが動かされて読めないときの予備。名前そのものが化けていれば束ねない（見出しにも検索語にも使えない）
- ブラウザで落としたファイルの ReferrerUrl は商品ページの URL で、zip のパスではない。**ドライブ文字か `\\` で始まり、書庫の拡張子で終わるもの**だけを使う
- 未確定になった zip そのものは、それ自身が元zip。同じzipを展開した中身と同じ束に入る

実測（`experiments/ZipOriginProbe`）：

| データ | 件数 | フォルダで束ねる | 元zipで束ねる | フォルダだと割れていた元zip |
|---|--:|--:|--:|--:|
| 手元の取り込み元（その場で記録を読む） | 235 | 47 束 | **11 束**（234 件）＋フォルダ 1 束 | 9 / 11 |
| 友人の未確定（保存値のみ、旧版の文字化けあり） | 334 | 38 束 | 12 束（97 件）＋フォルダ 14 束 | 3 / 12 |

友人の分は、334 件のうち 306 件が元zipの記録を持っていたが、209 件は名前そのものが化けていたので束ねていない。
友人のPCで開き直せば、ファイルから読み直して正しい名前が取れる。

**検索語はzip名の方がよい。**今の「展開元とみなしたフォルダの名前」は、展開した人が付けた名前や、中の1階層の名前になりやすい。
友人の分では、今の検索語が「アイコン用サンプル画像」「テクスチャ」「アクセサリー」「internal」だったものが、
zip名だと「usakami」「Mofuko」「ことりめがね」「Short Socks Marycia」のような商品名になった。
ただし `pochio_v1.3.2_update (1).zip` → 「pochio v1 3 2 (1)」、`…_ver.1.20.zip` → 「… ver」のように、
版番号とダウンロードの重複番号が残るものがある（検索語の整え方は #59 で直す）。

本物の自動検索（`ZipOriginProbe --search --compare`、本体と同じ `ProposeAsync`）で友人の12束を比べた結果：
**zip名の方が良い 6・同じ 4・どちらも外れ 2・悪化 0。**
良くなったのは、今の検索語では関係の無い商品しか出なかったもの（ポチォの商品・「ことりめがね」の同名商品3件が出た）と、
正解が3位から1位に上がったもの（Sample Eye Texture）。どちらも外れの2束のうち1つは、
ツールに同梱された部品（`base_library.zip`）で、そもそも BOOTH の商品ではなかった。

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

### 3回目（英語の辞書）：測り直したら**使えた**

最初は「英語の経路は使わない」と書いていた。`Sin`→罪業、`Ring`→土俵 のような語を作るからで、
**生成される語の見た目で切り捨てていた。**これは誤りだった——
実際に当たるかどうかは別の話で、測っていなかった。

測り直した結果、条件が2つ揃えば当たる。

| 語 | 結果 |
|---|---|
| `heartbeat`（**割らずに**引く）→ **心拍** | **1位** |
| `heartbeat`（割らずに）→ **心音** | **2位** |
| `Heart` `Beat` に割ってから引く → 心臓・心頭・ハート・拍・ビート | 全部外れ |
| `Sig Ring` → 指輪（候補の7番目） | **無し** |

**割り方が効く。**`FileNameQuery` はキャメルケースを割る（`HeartBeatGimmick` →
`Heart Beat Gimmick`）ので、辞書に `heartbeat`（1語）として引きに行けていなかった。
割る前の綴りでも引く必要がある。

**並びも効く。**候補は語の位置順（`Sig` が先）に並んでいて、
どちらの語が商品を指しているかを見ていない。`Sig Ring` では「指輪」が7番目に沈んでいた。

ただし `Sig Ring` は**「指輪」で引いても当たらなかった**。
「指輪」の商品はBOOTHに大量にあり、この商品が上位に来ない。
**これは表記の問題ではなく、検索そのものの限界。**

### 結論：読みの経路と、英語の経路（割らずに引く）を順に試す

**+1/10 に対して、10ファイルで+3リクエスト**（5件中2件は別表記が作れず通信ゼロ）。

良い性質が2つある。

- **当てられないときは黙って何もしない。**別表記が作れなければ通信も増えない
- **報告された例がちょうど直る。**ローマ字のファイル名が日本語商品を指す場合が対象

残る3件の理由は表記ではない。

- `Sig Ring`：正しい語「指輪」で引いても当たらない。同種の商品が多すぎる
- `Kuuta` の2件：商品名がラテン文字で、BOOTH側の検索の挙動（ANDと人気順）の問題。
  「くうた」で引くと20位、「ほとぎや」で8位

**見込みは 5/10 → 7/10。**読みの経路で `Tori`→鳥、
英語の経路（割らずに引く）で `HeartBeat`→心拍。

**この試験は検索の段だけを測っている。**実際の候補検索は、これに加えて
zip内の商品URL・unitypackageの作者名前空間・読みの一致（点数付け）も使うので、
端から端までの成功率はこれより高い。

### 実装して測り直した（2026-09-10）

`AlternateQueries` として実装し、同じ10ファイルで通した。**5/10 → 7/10。**

| ファイル名 | 引き直した語 | 結果 |
|---|---|---|
| `Tori_v1_1_1.zip` | **鳥**（読みの経路） | **1位** |
| `HeartBeatGimmick_v3.0.3.zip` | **心拍**（英語の経路） | **1位** |
| `Sig_Ring_07_ver2.zip` | シグ | 無し |
| `Kuuta_ShapekeyAddon.zip` | くうた | 20位（外） |
| `hotogiya_Kuuta_ver1.03.zip` | ほとぎや | 6位（外） |

**増えた通信は4本**（10ファイル中、裏付けの無かった5件に対して）。

実装で直したのが2つある。どちらも**測らなければ気付かなかった**。

- **隣り合う2語をつないだ形も辞書に引く。**`HeartBeatGimmick` を丸ごと引いても
  辞書には無い。`Heart` `Beat` に割ってから引くと 心臓・拍 にしかならない。
  隣り合う `HeartBeat` で引いて初めて 心拍 が出る
- **日本語の表記を先に選ぶ。**`Sig Ring` の英語候補の先頭は ＳＩＧ（全角ラテン文字）で、
  商品名には使われない形だった

残る3件は表記の問題ではない。`Sig Ring` は正しい語「指輪」でも当たらず、
`Kuuta` の2件はBOOTH側の検索の挙動（ANDと人気順）で沈む。

## 15. 候補検索の検索語・並べ直し・登録簿（2026-09-11 実測）

§7 は10ファイルでの話だった。所持の多い友人のライブラリで、**正解の分かる zip 319本**
（取り込みで商品に紐付いたもの）を使って測り直した（`experiments/ResolveAccuracyProbe`、
Zone.Identifier や zip 内の URL が消えた状態を再現。ファイルの中身は読まない）。

### 外れの形（測り直す前）

上位3件に正解が出たのは **172/319（54%）**。外れは3つの形に分かれた。

1. **余計な語で AND 検索が全滅する。**版番号の残り「1 02」、【マリシア対応】のような括弧の付け足し、
   #タグ、作者の略号「WH_」、ダウンロードの重複番号「(1)」
2. **日本語と英字が続けて書かれて1語のまま**（「撫mofu」「ギミック4」）
3. **シリーズ物が下位に沈む。**番号を捨てた検索語だと、BOOTHの並び（人気寄り）で十数位になる

### 入れたもの（#59）

| 何 | どこ |
|---|---|
| 検索語を整える（上の1・2。「」の中身は商品名なので残す、重複語は潰さない） | `FileNameQuery.Tokens` |
| 検索結果の1ページ（最大60件）を、ファイル名との近さ（語・番号・ショップ名）で並べ直す。**通信は増えない**（検索ページのカードに名前とショップが載っている） | `FallbackResolver.Rerank` |
| 裏付けが出なければ、まず特徴のある1語だけで引き直し、次に別表記（§7） | `FallbackResolver.RetryQueries` |
| 語に直接付いた1〜3桁の番号（「練習用ポーズ集13」）も点数と並べ直しに使う。数字は前後が数字でないときだけ一致とみなす | `FileNameQuery.SeriesNumbers` |
| 登録簿の候補を締める（下） | `RegistryCandidates` |

### 結果（319本）

| | 上位3件に正解 |
|---|---|
| 前（旧い検索語・BOOTHの並び） | 172（54%） |
| 整えた検索語だけ | 193（60%） |
| ＋60件の並べ直し | 214（67%） |
| ＋1語・別表記で引き直し、登録簿も含めてどれかで届く | **242（76%）** |

問い合わせは319本に対して471本（引き直しの分が増える）。

**画面の1位に正解が来るか**（本体と同じ候補検索を、等間隔に抜いた30本で端から端まで。候補ごとに商品JSONも取る）：

| | 1位が正解 | 候補のどこかに正解 |
|---|---|---|
| 前 | 13/30 | 16/30 |
| 後 | **19/30** | **25/30** |

良くなったのは、番号の効いた「練習用ポーズ集11/13/24」（前は同じシリーズの別の巻が1位）、Braid の2本、Rose Step Heels など。

**1本だけ悪くなり、原因を直した。**「やわらか影システム 9.2(for Avatar) PCSS For VRC .zip」は、整えた検索語に英語の「For」が
語として残り、裏付けが出なかったので別表記で引き直したとき、英語の辞書で **for → 対して** になった。「対して」で引いた無関係な商品が
「商品名と一致」「読みで一致（対して）」で5点を取り、正解（3点）を上回った。**for・the・of などの英語の機能語を検索語から落とし**、
正解が1位に戻ったことを確かめた。

「確度が高い」（7点以上）は、前も後も1本も付かない。手掛かり（Zone.Identifier・zip内のURL・unitypackage の作者名）が
消えた状態では、ファイル名だけでは届かない水準にしてあるため（誤って確定しないための設計どおり）。

### 登録簿の候補

登録簿の候補が**外ればかり**だったものが141本あった。当たっていた語の多くは「VR」「si」「シェーダー」「ポーズ」「天使」の
ような短い語・一般的な語、または「_PSD_Mofuko_Makeup」のように、アバター名は入っているが実はそのアバター向けの別商品のもの。

締めたこと：一般的な語（対応アバター検出と同じ一覧）は使わない／ラテン文字は3文字以上で語境界／
登録簿の2項目以上が持つ表記は使わない（どれを指すか決められない）／2文字の読みは、項目の名前を区切った短い一片に当たるときだけ。

| | 1位が正解 | 3件内に正解 | 外ればかり |
|---|---|---|---|
| 前 | 8 | 9 | 141 |
| 締めた後 | 9 | 10 | 104 |

最初は「読みは3文字から」にして、正解だった Eku → エク、Tori → 『Bird/鳥』の4本を落とした。
名前そのものが短い項目にだけ2文字の読みを許して戻した。

「衣装に対応アバターの名前が付いている」形（`くうた_衣装.zip`）は、登録簿の候補として出し続ける。
この候補は「BOOTHに無い商品として登録する」の名前の候補にも回っていて、外すとそちらが空になる。
