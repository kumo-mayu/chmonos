# AI から使う（MCP の通訳）

> **要点**：`Chmonos.exe --mcp [--store <保存先>]` で起動すると、画面を出さずに **MCP のサーバ（stdio）** として動き、AI（Claude Code・Claude Desktop など MCP の使い手）の問い合わせを
> **今のローカル HTTP API に通訳する**（ユーザ判断 2026-11-13）。通訳は API に話すだけで、保存先を直に読まない・書かない・`app.lock` を取らない・BOOTH へ問い合わせない。
> **最初は読むだけ**（道具15個）。許しは API の「ソフトごとの許し」（[api-clients.md](api-clients.md)）をそのまま使う。
> **コード**：`Chmonos.Mcp/`（`McpProgram`・`ChmonosBridge`・`ChmonosTools`・`AnswerShaper`・`Visibility`・`TokenStore`・`InstanceFinder`）、入口 `Chmonos.App/Program.cs`。試験 `Chmonos.Mcp.Tests/`。

## 決めたこと（2026-11-13・ユーザ判断）

- **形**：今の `Chmonos.exe` に `--mcp` を付けると通訳になる（別の exe を配らない・アプリと版がずれない）。
- **通訳は API に話すだけ**。API の守り（`127.0.0.1` だけ・トークン必須・中身をログに残さない・BOOTH へ問い合わせない・書き込みは `UiCommand`）がそのまま効く。
  使う人が設定の「つないでいるソフト」で取り消せば、AI もすぐ使えなくなる。
- **最初は読むだけ**。書き込みの道具は出さない。後で足すときは、道具の定義に API の書き込みの種類（`Tool.WriteScope`）を書けば、許しを求めるときにその種類も求める（窓で人が選ぶ）。
- **退けた案**（記録）：
  - B「アプリの中に `/mcp` の HTTP の口」：今の許可の窓を MCP の使い手が話せず、トークンを手で設定に写すことになる。AI ごとの見分けが崩れる（どれも同じトークン）。
    ポートが変わると使い手の設定を直す。MCP の規格の改訂のたびにアプリ本体を出し直す。
  - A「別の exe」：配る物が増え、アプリと版がずれる。
- **MCP の部品**：公式の C# SDK の `ModelContextProtocol.Core` 2.3.1（Apache-2.0。2026-11-13 の安定版）。DI と Hosting の付け足し（`ModelContextProtocol`）は使わない（1本の通訳に要らず、引き連れる物が減る）。
  道具は低い層の受け口（`ListToolsHandler`・`CallToolHandler`）で自分の表から出す（引数の形を JSON Schema で手で書く。リフレクションで組まない）。Core と API には MCP を入れない（`Chmonos.Mcp` を App が参照）。

## 起動の分かれ方

- 入口は `Program.Main`（`StartupObject`）。`--mcp` があれば **WPF の `App` に触れる前に** `McpProgram.Run` へ分かれる。無ければ App.xaml の入口（`App.Main`）を呼ぶ（今までと同じ）。
- 通訳では次をどれもしない（画面の起動と取り違えない）：`App` の静的な初期化（`StoreLocation.AllowsUserStore` を立てる）・保存先の確かめ・初回の窓・多重起動の錠（`app.lock`）・
  ログの書き先（`logs/app.log`）・動いている一覧の札。実測（2026-11-13・Debug の exe をパイプでつないで initialize・tools/list・tools/call）：
  WPF のアセンブリ（Presentation*・WindowsBase・System.Xaml）は読まれず、読まれた Chmonos の部品は `Chmonos.exe`・`Chmonos.dll`・`Chmonos.Mcp.dll`・`Chmonos.Core.dll` だけ。専用メモリ約11MB。
  `%LOCALAPPDATA%\Chmonos` は変わらなかった。
- **stdout は MCP の通信だけ**。始めに `Console.SetOut(Console.Error)` で、ほかの部品が `Console.Out` に書いても stderr へ行くようにする（SDK の stdio の運びは生の stdout の流れを開くので影響しない。実測で stdout の行は全部 JSON-RPC）。
  知らせ・例外は stderr に1行（何をしていたか・例外の種類と文。トークン・問い合わせの中身・答えは書かない）。Claude Desktop は stderr を自分のログに残す。
- 使い手が stdin を閉じたら終える。

## 探す

- 呼び出しのたびに札の置き場（[running-instances.md](running-instances.md)）を読み直す（Chmonos が後から起動した・起動し直した・ポートを変えた、を次の呼び出しで拾う）。
  置き場は通訳が自分で決める（`CHMONOS_RUNNING_DIR`、無ければ `%LOCALAPPDATA%\Chmonos.Shared\running`）。`RunningInstances.Directory` は App の印を見るので使わない。札を消す・片付けるのは Chmonos 本体の役目（通訳は置き場に書かない）。
- 生きている札（`RunningInstances.IsAlive` が false でない物）だけ数える。`--store` があればその保存先だけ（場所は完全なパスに揃え、末尾の区切りを落とし、大文字小文字を無視して比べる）。
- 答えられないときは落ちずに、道具の答え（`isError`）で次にやることを言う：
  - 無い：「Chmonos が動いていません。Chmonos を起動して、設定の「API」で「APIを使う」をオンにしてから、もう一度試してください。」
  - API がオフ（札の `port` が null）・`--store` の保存先が無い・2つ以上（保存先を並べ、「起動の引数に --store <保存先のフォルダ> を足して」）。
  - つながらない（閉じた直後など）・API が断った（problem+json の `detail` を添える。例：404・503 `formatTooNew`）。
- `api.json` は読まない（保存先の中にある。トークンは人が許して受け取った物だけ使う）。

## 許し

- 流れは [api-clients.md](api-clients.md) のとおり。名乗る名前は MCP の initialize の `clientInfo`（`title`、無ければ `name`）に「（MCP）」を添えた物（例「claude-code（MCP）」）。
  制御文字・向きを変える字を除き、64字に切り詰める（API の `invalidName` に当たらない）。空なら「AIの道具（MCP）」。書き込みの種類は求めない（今は空）。
  許しの窓の exe の欄には `Chmonos.exe` が出る（API に接続を開くのは通訳のプロセス）。
- **待つ秒数：25秒**（`McpOptions.DefaultApprovalWait`）。API の `wait` は30秒までなのでその中に収め、少し短くした（プラグインの見本と同じ）。Claude Code の道具の時間切れは既定で約28時間（[公式] https://code.claude.com/docs/en/mcp ）で、当たらない。
  ほかの使い手の時間切れは確かめていない。並んで呼ばれた道具は、錠が空いた後は待たずに1回だけ見る（待ちを重ねない）。
  待ちきれなければ「Chmonos に許可の窓を出しました。Chmonos の窓で［許可］を押してから、もう一度試してください。」。求めの `requestId` は保存先ごとに覚えておき、次の呼び出しで同じ求めの続きを待つ（求め直すと窓が重なる）。
  求めが消えていた（404。Chmonos の起動し直し・10分取りに来なかった）ら、その呼び出しの中で1回だけ求め直す。
- 断りはそれぞれの文：拒否（`denied`）・窓を出せない（503 `approvalUnavailable`：「Chmonos の画面を開いてから」）・止められている（403 `requestBlocked`：設定の「止めているソフト」で外す）・429。
- 許しの求めは通訳の中で1本ずつ（使い手は道具を並べて呼べる）。
- **同じ名前の通訳が2本**（Claude Code のセッションが並ぶ）：Chmonos は重ねた求めを1つにまとめ、トークンは片方にだけ渡す（もう片方は `delivered`）。
  `delivered` の側は控えを読み、先に受け取った側が書くのを2秒まで待って使う。
- **401**：控えを読み直し、ほかの通訳が後から受け取った別の値があればそれを使う。無ければ控えから捨てて求め直す（同じ名前の通訳どうしが互いの行を置き換え合わないように）。

## トークンの控え

- 置き場：`%LOCALAPPDATA%\Chmonos.Shared\mcp-tokens.json`（BOOTH の門・動いている一覧と同じ「PC で1つ」の置き場。差し替えは `CHMONOS_MCP_TOKENS`）。
  保存先の外にしたのは、通訳が保存先に書かないため（書き出し・引越し・戻すに混ざらない）。
- 形：`{ "version": 1, "tokens": [ { "store", "client", "token", "savedAt" } ] }`。**保存先ごと・使い手の名前ごと**に1行（保存先が違えば許しも違う。使い手は「つないでいるソフト」に別々に並ぶ）。
- 平文（`api.json` と同じ。自分の Windows ユーザーのフォルダで、同じユーザーで動く道具は保存先も読める。守れる範囲は api-clients.md「なぜ」）。
- 書くときは名前付きの錠（控えの場所から作った名前）で1本にし、一時ファイルから付け替える（通訳はセッションごとに動き、同じファイルを書き合う）。壊れた控え・ヘッダに載らない字のトークンは無かった物にする。

## 表示の設定（2026-11-13・ユーザ判断「mcpの2つは今のまま安全側でOK」）

- **使う人の表示の設定に AI の答えも従う**：非表示にした商品はいつも、R-18 は設定の「R-18 を表示しない」のとき、AI に渡さない。設定は呼び出しのたびに読む。
  - 検索：API の `includeHidden: false`・`includeAdult: <設定>` で絞る。
  - 商品1件・説明文・unitypackage の中身（item_id）・アバター1体：隠す物なら「非表示にしているので答えません」「R-18 で…表示しないことにしているので答えません」。
  - 商品への参照（2026-11-13 の点検 M1）：隠す商品の ID（非表示と、隠す R-18 を検索で引いた物）を指す物を、欄の形ごとに落とし、落とした行と参照の数を `omittedByDisplaySettings` に添える。どの道具の答えにも当てる（検索の要約・商品1件・アバター・ファイルの逆引き・unitypackage・GUID の逆引き・改変・Unity を調べた結果・通知・共通素体）。
    - 一覧の行の `itemId`・`avatarId`（通知・ファイル・包み・改変の使った物・対応アバター `compatibleAvatars`・`avatarLinks.avatars`・改変のアバター）：行ごと落とす（ID と名前の組なので、ID だけ消すと名前が残る）。
    - ID の文字列の配列（`avatarIds`・共通素体の `members`）：その ID だけ抜く。
    - 改変1件のアバターが隠す物なら断る（改変の名前もアバターの名前を含むことが多い）。
    - 共通素体は名前でまとめた物なので、`members` と素体の商品 `itemId`（null にする）だけを消して行は残す。見せてよい仲間が残らなければ行ごと落とす（素体の名前は隠したアバターの名前と同じことが多い）。アバターの `baseName`・商品の `avatarLinks.bases` の素体の名前は残る。
  - 隠す商品の ID を集めきれなければ（200件×100頁で続きが残る・答えに `items` が無い・ID が読めない・`nextCursor` が変）、ID の一覧が要る道具は「集めきれなかったので答えません」と断る（fail-closed。点検 M2）。
  - 分類（タグ・カテゴリ・ショップの件数）は商品を名指ししないので落とさない。
- AI が隠した物を見る引数は付けない（見せたければ、使う人が Chmonos の設定を変える）。［決定］「AI には全部見せる」「引数で頼んだときだけ見せる」は採らない（ユーザ判断 2026-11-13）。

## 道具（読むだけ・15個）

名前は英語の snake_case、説明は日本語（使う人と商品の名前が日本語で、答えの欄の言葉と揃う）。説明と例は作り物の名前だけ。どれも `readOnlyHint: true`・`openWorldHint: false`。

| 名前 | 中身（API） |
|---|---|
| `chmonos_status` | つないでいる保存先・ほかの札・名乗る名前（API に問い合わせない。許しを求めない） |
| `search_items` | 探す（`POST /v1/search`。`query` は検索欄と同じ文字列、`where` は式、`sort`・`has_unity_package`・`limit` 1〜50 既定20・`cursor`） |
| `get_search_conditions` | 条件の種類（`/v1/search/conditions`）と式の書き方の短い説明 |
| `get_item` | 商品1件（`include`：avatarLinks・allFiles・contents・meta） |
| `get_item_description` | 説明文を見出しごとの素の文で（`offset`・`max_chars` 500〜6000 既定3000。HTML は返さない） |
| `list_avatars`・`get_avatar` | 登録簿（`query`・`owned_only`） |
| `list_avatar_bases` | 共通素体 |
| `lookup_file` | ファイル・フォルダの場所かハッシュから商品（`/v1/files/lookup`） |
| `list_unitypackages` | 手元の unitypackage（`avatar`・`query`） |
| `find_unitypackage_assets` | 包みの中身（`item_id`・`path_contains`）か GUID の逆引き（`guids` 200件まで） |
| `list_modifications` | 改変（`id` で1つ・`avatar`・`query`） |
| `get_unity_scans` | Unity プロジェクトを調べた結果の控え（`project`） |
| `list_notifications` | 通知（新しい順・`unread_only`） |
| `list_classifications` | `kind`：user_tags・attributes・categories・booth_tags・shops（`query`） |

- アバター・素体・ショップから商品を引く専用の道具は作らない（API と同じく `search_items` の `where` で書く）。
- 出さなかった物：画像の本体（AI は手元の絵を開けない）・SSE・変化の一覧・プラグインの口（MCP の使い手は1回ずつ問い合わせる）。

## 上限（AI の文脈を食いすぎない）

- 1つの答えは **8,000字まで**（`AnswerShaper.MaxAnswerChars`）。Claude Code は道具の答えが1万トークンを超えると警告を出す（[公式] https://code.claude.com/docs/en/mcp 。既定の上限は2万5千トークン）。日本語は1字が1トークン近いので、字数で8千に抑える。
- 字数は JSON にした後の長さで数える（制御文字は `\uXXXX` の6字になる）。道具の答えの共通の出口（`AnswerShaper.Guard`）でも確かめ、超えた答えは切らずに、中身を写さない小さな省略の答え（`omitted`・`message`）に置き換える（2026-11-13 の点検 M3）。出口の網は保険で、各道具は先に収める。誤りの答えも、API の断りの文（`detail`）に長さの決まりが無いので上限で切る（素の文なので切っても壊れない）。
- 文字列は1つ300字まで（末尾に「…（N字のうち先頭M字）」）。入れ子の一覧は30件まで（「…ほか N 件（省きました）」）。それでも長ければ 10件・5件／120字…1件／40字と段々に短くする（いつも JSON として読める形）。それでも収まらなければ省略の答えにする。隠す決まり（`Visibility`・`blueprintId` などを落とす）は削る前に当てる。
- `get_item_description` の `max_chars` は本文の字数の上限で、JSON にして上限を超えるなら本文を更に短くする。`nextOffset` は実際に返した本文の長さから出し、絵文字の組の途中では切らない。
- 絵の場所（`thumbnail`・`imagePath`・`images`）と説明文の `html` は落とす。
- 改変の `blueprintId` も落とす（［決定］ユーザ判断 2026-11-13「今のまま安全側でOK」）。API が返すのは「同じ PC の道具には隠しても守りにならない」から（[local-api.md](local-api.md)・2026-11-02）で、AI に渡すと AI の提供元へ出る。答えの JSON は日本語を `\uXXXX` に逃がさない（字数が6倍になる）。全角空白（U+3000）と絵文字も素のまま返す：`UnsafeRelaxedJsonEscaping` でもこの2つは逃げるので、引用符・`\`・制御文字だけを逃がす自前の `PlainJsonEncoder` を使う（答えは HTML に埋めず AI が読むだけなので `<` `&` も逃がさない）。`get_item_description` の本文の改行は `\n` にそろえる（BOOTH の本文の `\r\n` と、組み立てで足す改行が混ざるため）。
- 検索は API の続きの印を使い、長すぎれば `limit` を半分にして引き直す（通訳の側で切ると続きの印と合わない）。最上位の `items` には入れ子の一覧の上限（30件）を掛けず、1件ずつ削る（掛けると `limit` 31〜50 で切った分を続きで取れない。点検 M4）。1件でも長ければその1件だけを縮め、件数と `nextCursor` は API の物のまま返す（一覧の道具も同じ）。
- 続きの印の無い一覧は通訳が `offset`・`limit`（既定30・100まで）で区切り、`total`・`nextOffset` を返す。上限を超えれば件数を減らす。

## 配る大きさ

1ファイルの exe（`win-x64.pubxml`）が 229,523,419 → 239,564,669 バイト（+10.0MB。2026-11-13 に同じ PC で組んで測った）。
1ファイルにしない形では +2.1MB（`ModelContextProtocol.Core.dll` 1.30MB・`Microsoft.Extensions.AI.Abstractions.dll` 0.68MB・`Chmonos.Mcp.dll` 0.12MB）。
差の残り約8MB は事前翻訳（ReadyToRun）の合成の分と見ている（[推測]。通訳の部品を事前翻訳から外す指定では大きさが変わらなかった）。

## 試験（`Chmonos.Mcp.Tests`）

一時フォルダの保存先で API を `127.0.0.1` に立て（`ApiHost.BuildAsync`）、札の置き場とトークンの控えも一時フォルダに置く（本物の `%LOCALAPPDATA%\Chmonos.Shared` を読まない・書かない）。
使い手は公式の `McpClient` をメモリの流れでつなぐ。道具の数と印・無い／オフ／落ちた札／2つと `--store`・後から起動・許しの流れ（求める→待つ→控える→次は聞かない→待ちきれない→続き→拒否→窓を出せない→401 で求め直す→2本の通訳）・
表示の設定・引数と API の誤り・切り詰め・名前の整え方・引数の読み方・stdout に JSON-RPC だけが流れること。
