# Chmonos の外部の点検（プロジェクトの部分）

harness の `external-audit` スキルが、共通の雛形（`read.md`・`attack.md`）の `{{…}}` にこの節を入れて Codex に渡す。
節の名前は変えない。2026-10-06 に作った読むだけの指示書（`external-audit-prompt.md`）から、共通の部分を除いて移した（2026-10-21）。
attack はまだしていないので、attack だけの節は置いていない。するときに足す。

## アプリの種類

Windows のデスクトップアプリ

## アプリ名

Chmonos（クモノス）

## このアプリ

BOOTH（pixiv のダウンロード販売）で買った VRChat 向けのアセット（zip・unitypackage）を、手元で管理する Windows アプリです。
WPF / .NET 10。2026-10-07 に v1.0.0 を公開しました（GitHub の Releases と BOOTH）。

- `Chmonos.Core/`：保存・取り込み・BOOTH への問い合わせ・検索・ID の特定（UI を持たない）
- `Chmonos.App/`：WPF の画面と ViewModel
- `*.Tests/`：xUnit の試験
- `tools/`・`experiments/`：道具と計測
- 決め事：`CLAUDE.md`（最初に読む）、`docs/spec/*.md`（入口は `docs/README.md`。最初に読む）、`docs/dev/file-lifecycle.md`

保存は全部 JSON で、保存先の場所は `%LOCALAPPDATA%\Chmonos\location.json` が指します（環境変数 `CHMONOS_HOME` が最優先）。

## 触ってはいけない場所

- `%LOCALAPPDATA%\Chmonos`（本番の保存先と `location.json`）
- `%LOCALAPPDATA%\Chmonos-sandboxes\` の下の全部（友人のデータの写しが混ざっている。第三者のデータ）
- `%LOCALAPPDATA%\Chmonos-fixtures\`・`%LOCALAPPDATA%\Chmonos-vm\`
- リポジトリの直下の `DLforTest/`・`VRChat_Avatar/`・直下の `*.zip` と画像（購入した実物のアセット。開かない・中を読まない・どこへも送らない）

## 走らせてよいコマンド

- `dotnet build` と `dotnet test`（既定の一式。通信しない・作り物のデータだけ）
- BOOTH（booth.pm・pximg）へは問い合わせない。`experiments/` の実行ファイルの多くは本物の BOOTH へ問い合わせるので走らせない

## 狙う所

### A. データを失う・壊す（いちばん重い）

- 書いている途中で落ちる・電源が切れる・ディスクが一杯（`JsonStore` の置き換え・一時ファイル・控え `.prev`）
- 読んでから書くまでの間に、ほかの道（取り込み・裏の取得・画面の操作）が同じ記録を書く（`ItemRepository.SaveLocalAsync`・`ChangeLocalAsync` の錠、`StoreWriteGate`）。古い写しで上書きして、人が入れた値（タグ・メモ・購入記録・対応アバター）が消える道
- 引越し・書き出し・戻す（`StoreMover`・`BackupArchive`）を途中で止める・失敗する・同じ場所を選ぶ・入れ子の場所を選ぶ・`location.json` が書けない
- 「消す」「外す」「片付ける」「既定に戻す」が、狙っていない物まで消す（ごみ箱へ送る `UnpackedFolderRemover`・展開したフォルダ・控え・画像）
- 手で直した JSON・前の版が書いた JSON・途中で切れた JSON・欠けた配列（null）を読んだとき（CLAUDE.md の「欠けた配列を空として受ける」）
- 同じ PC で2つ起こした・別の保存先で2つ起こした（`BoothMachineGate`・保存先の錠）

### B. ファイルの扱い（見つからない・移動・名前）

- ファイルやフォルダが移動・改名・消された・ドライブが外された・ネットワークの場所が落ちた（「見つからない」の記録 `missingSince`・ディスクの通し番号 `volumes`）
- 長いパス（260字超）・UNC・末尾の空白やドット・予約名（CON など）・全角や絵文字・正規化の違う同じ名前（NFC/NFD）・大文字小文字だけ違う名前
- シンボリックリンク・ジャンクション・読み取り専用・権限の無いフォルダ・使用中で開けないファイル
- 同じ中身の zip が別の場所にある・同じ場所に別の中身が置かれた（ハッシュと場所の同一性）

### C. 外から来る物を信じすぎる（悪意のある・壊れた入力）

- **zip**：パスの抜け出し（`../`・絶対パス・ドライブ名）・とても大きく膨らむ zip・入れ子の zip・壊れた中央ディレクトリ・暗号付き・同じ名前の重複・巨大な数の項目（`BoothZipInspector`・一時展開・unitypackage の中身の読み取り）
- **unitypackage**（tar.gz）：パスの抜け出し・巨大・壊れた形
- **BOOTH から取った HTML・JSON**：想定外の形・巨大な文字列・スクリプト・リンク（説明文を保存して画面に出す所 `items/{id}.h2.html`・説明文の中の URL を開く所）・429 や Retry-After の無茶な値・リダイレクト
- **画像**：壊れた画像・巨大な寸法・形式の偽装（メモリ・UI スレッドで固まる）
- **ダウンロード元の記録（Zone.Identifier）**：書き換えられた HostUrl/ReferrerUrl で、別の商品として登録させる
- **貼り付け・ドロップ**：巨大な文字列・URL の形の偽装（booth.pm に見える別のホスト）・ファイルとフォルダの混在
- **外のアプリを起こす所**：`Process.Start` に渡す引数（Unity のプロジェクトの場所・unitypackage の場所・`vcc://`・`unityhub://`）に、空白・引用符・`&`・改行を含む名前が来たとき、意図しない命令や引数になる道（`UnityLaunch`・`VccLaunch`・`AlcomLaunch`・`DesktopAppLaunch`）
- **レジストリから読んだ値**（アンインストールの一覧・関連付け）を、そのまま実行するパスとして使う所
- OSC（`VrcOsc`）に送る値・送り先

### D. 外への漏れ・規約

- BOOTH への問い合わせが「1本ずつ・1.5秒以上空ける」（`BoothClient` のゲート・`BoothMachineGate`）を、どこかの道で破る・並列になる・間隔が縮む・ゲートを通らない通信がある
- ログ・通知・例外の文・診断の書き出しに、利用者のファイルの場所・商品の名前などが、意図せず外へ出る形で残る（画面に出す・共有されうるファイルに書く）
- 試験や道具が、指定なしで利用者の本当の保存先（`location.json` の先）を開く・書く道（`StoreLocation.Resolve`・`AllowsUserStore`・`MayWriteLocationFile`）

### E. 画面が固まる・落ちる

- UI スレッドで同期の待ち（`.Result`・`.Wait()`・同期の錠）をして、裏の作業と待ち合う（2026-09-23 に `StoreWriteGate` で実際に固まった）
- 数万件の商品・数千件の通知・数百の未確定・大きな画像での、全件の作り直し・UI スレッドでの重い計算
- 例外が捨てられて（`async void`・`Forget()`）、黙って止まる・中途半端な状態が残る

### F. 環境の違い

- 時計が戻る・夏時間・タイムゾーンの違い・日付の境（「今日」の数え方・予定日）
- 言語の設定（トルコ語の i などの大文字小文字・数字の書式）・高い DPI・狭い画面
- Unity Hub・VCC・ALCOM が無い・壊れて残っている・別の版
- 管理者でない・会社の PC で権限が絞られている

## 既に分かっていること

- 決め事どおりの振る舞いは指摘にしない。例：公開前は古い形のデータに合わせる変更をしない。推定した値は勝手に入れない
- 既に記録した物：`docs/feedback/open.md`・`docs/feedback/review-*.md`・`docs/feedback/behavior-*.md`・`docs/feedback/done-2026-10.md`
- 2026-10-06〜07 に読むだけの点検を21回かけ、10-13 にも段ごとにかけた（36・37 回目）。記録だけにした物の扱いは `docs/dev/release-handoff-2026-10-07.md`

## 重大度

- **致命**：利用者のデータが消える・壊れる・別の物に化ける。BOOTH の問い合わせの決まりを破る。利用者の保存先を意図せず書く。外のアプリを意図しない引数で起こす
- **重い**：画面が固まる・落ちる・人が入れた値が黙って消える・誤った物を消す
- **中**：食い違い・取り違え・回復できるが手間のかかる壊れ方
- **軽い**：まれな条件での見た目・文言・効率

## Codex の設定（任意）

指定なし（既定のモデル）。
