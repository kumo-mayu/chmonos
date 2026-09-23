# 文書の入口

守ってほしい決め事は `CLAUDE.md`。ここはそれ以外の文書の索引。

## リポジトリの地図

| 置き場 | 中身 |
|---|---|
| `BoothAssetManager.App/` | 画面（WPF）。`Views/`・`ViewModels/`・`Controls/`（`SuggestBox`・`ColumnsPanel` など共通の部品）・`Services/` |
| `BoothAssetManager.Core/` | 画面に依存しない本体。`Booth/`（通信のゲート）・`Commands/`（`UiCommand`）・`Models/`・`Storage/`・`Scanning/`（取り込み）・`Resolution/`（商品IDの特定）・`Search/`・`Services/`・`Images/` |
| `BoothAssetManager.Core.Tests/` | 既定のテスト一式（通信しない） |
| `BoothAssetManager.Cli/` | データ層を実データで試す足場。配布物ではない |
| `BoothZipInspector/`・`BoothIdResolver/`（と `.Tests`） | zip の手掛かり読み・商品IDの特定のライブラリ（本体が使う） |
| `experiments/` | 採否を決めるための計測・試しの実行ファイル（BOOTH へ実際に問い合わせるものもここ）。一覧は `experiments/README.md` |
| `research/`（直下） | 初期の調査に使ったスクリプトと結果（結果の JSON は無視）。**調査の文書は `docs/research/`** |
| `mock/` | 画面のモック（Claude Design のキャンバス。`*.dc.html` が1画面ずつ）。今の画面とは違う所がある |
| `tools/wording.mjs` | 画面に出る文字列を XAML と C# から集める（文言の総点検に使う） |
| `.claude/skills/`・`.claude/rules/` | 繰り返す手順（`ui-check`・`perf-measure`・`feedback-log`・`wrap-up`・`parallel-fix`）と、App・Core のコードを触るときだけ読み込まれる決め事（`screen-and-wording.md`）。写しの保存先の一覧は `ui-check/sandboxes.md` |
| 直下の `作業方針メモ.md`・`モック用画面メモ.md`・`Re画面として不足しているもの（重要度順）.txt` | 作者のメモ。読むだけ |

## 文書の種類

- **`spec/`** — 話題ごとの**今の決め事**。1ファイル200行まで。冒頭に要点・コードの場所・経緯へのリンクがある。**作業の前にまず読む。**
- **`dev/`** — 作るときの落とし穴（[wpf.md](dev/wpf.md)：XAML・一覧・絵のメモリ・XML／[powershell.md](dev/powershell.md)：確かめと作り物のデータのスクリプト）。書く前に読む。
- **`history/`** — 決めたときの経緯（grill・当時の数字・訂正）。書かれた時点の記録なので、今の決め事と食い違う所がある（そのときは spec が正）。
- **`research/`** — 調査と計測の記録。
- **`feedback/`** — 画面への意見。`open.md` が未対応・記録のみ、`done-2026-09.md` が直した記録（U番号・D番号。約300KB なので見出しを Grep して節だけ読む）、`ui-flows-2026-09-15.md` が画面と状態ごとの操作の動線と、意図と違う動き・その画面に無い機能の一覧（UI 案を練るときの材料）、`ui-consistency-2026-09-20.md` が語・メニュー・窓・入力・空表示の**揃っていない所**の一覧（同じく材料。W/R/M/B/D/E/I/V の番号で指す）、`behavior-2026-09-20.md` が**動き**の変な所の一覧（中断・待ち・同時に走ったときの取り違え・保存の競合・裏の作業・データの決め事。L/C/N/P/G/X/J/Q の番号で指す）、`review-2026-09-23.md` がその後の全体の点検と直した記録（判断をもらった2件を含む）。
- **`features.md`** — 機能の一覧（何・なぜ・状態・出典）。1行が長いので行を Grep して直す。

## spec（今の決め事）

| ファイル | 話題 |
|---|---|
| [data-model.md](spec/data-model.md) | 保存するファイルの一覧、商品の `booth`／`local`、手元のファイル、書き方、所持の数え方 |
| [architecture.md](spec/architecture.md) | `UiCommand` を通す物、持ち主を宣言した書き込み、スレッドとディスク、画面の履歴、partial の分け方、試験 |
| [import.md](spec/import.md) | 取り込みの周回と梯子、積む・取り消す・中断、未確定の一覧の合わせ方、⑦ 取り直し |
| [background-and-network.md](spec/background-and-network.md) | BOOTH への通信の決め事、優先度、起動時の裏の作業、失敗の書き残し（`logs/app.log`） |
| [id-resolution.md](spec/id-resolution.md) | 商品IDの手掛かり、ファイル名からの自動検索、未確定の画面、「この商品から外す」 |
| [private-items.md](spec/private-items.md) | BOOTH に無い商品（仮ID `local-`）、人が入れる名前・カテゴリ・ショップ、「IDを変更」 |
| [search.md](spec/search.md) | 絞り込み、文字列の構文、表記をまたぐ辞書、結果のカード |
| [item-page.md](spec/item-page.md) | 商品ページ、画像（自分で足す・サムネイルの指名）、手元のファイル、購入記録、編集画面 |
| [avatars.md](spec/avatars.md) | 対応アバターの検出、登録簿、共通素体、相性の3段階、所有アバター |
| [modifications.md](spec/modifications.md) | 改変の記録（構成物・画像・Unity プロジェクト）、改変の画面、「Unityで選択」 |
| [unity.md](spec/unity.md) | Unity への受け渡し（窓を名指しして送る・連続送り・入り先・プロジェクトタブ・VCC） |
| [folder-view.md](spec/folder-view.md) | フォルダビューの根の決め方、右に出す物、ドライブ文字の読み替え、速さ |
| [ui-rules.md](spec/ui-rules.md) | 文言、入力、ナビ、窓と画面の状態、画面の幅、ドロップ、キー |
| [ui-dialogs.md](spec/ui-dialogs.md) | 別の窓（ダイアログ）の Enter・Esc、ボタンの並びと名前、取り返しの一文、確認と失敗のアイコン |
| [ui-input.md](spec/ui-input.md) | 候補の付け方、Enter で決める範囲、名前の変え方、メモの保存、編集画面の保存の分かれ方 |
| [ui-empty-and-errors.md](spec/ui-empty-and-errors.md) | 空の表示の3通りの書き分け、押せない理由の出し方、黙って捨てない失敗、利用者に見せない文 |
| [tags.md](spec/tags.md) | タグの管理・属性の管理（大分類／小分類、名前の変更・統合・削除、並べ方、小分類の中を探す、メモ） |

## history（経緯）

| ファイル | 中身 |
|---|---|
| [grill-1-overview.md](history/grill-1-overview.md) | 最初の詳細検討（商品とファイル、要確認の一本化） |
| [grill-2-ui-and-data-model.md](history/grill-2-ui-and-data-model.md) | モック後の UI とデータの形（ファイル構成・購入記録・画面の決まり） |
| [avatars.md](history/avatars.md) | 対応アバターと共通素体の実測・操作の一覧・再測定と採用した案 |
| [ui-revision.md](history/ui-revision.md) | 使ってみて出た指摘（絞り込み・文字列・辞書・ドロップ・購入記録・外す・初期設定・監視） |
| [import-order.md](history/import-order.md) | 取り込みの順序（梯子）を決めた理由 |
| [import-concurrency.md](history/import-concurrency.md) | 取り込み中もアプリを使える（優先度・持ち主・印・⑦） |
| [private-items.md](history/private-items.md) | 非公開商品の取り込み（38問） |
| [user-images.md](history/user-images.md) | 自分で足す画像 |
| [modifications.md](history/modifications.md) | 改変の記録と改変の画面の刷新 |
| [unity-handoff.md](history/unity-handoff.md) | Unity への受け渡しの実測（シェル・メニュー・ログ監視・プロジェクトタブ・VCC） |
| [folder-view.md](history/folder-view.md) | フォルダビューの根の決め方の試しと速さ |
| [tech-debt-2026-09-14.md](history/tech-debt-2026-09-14.md) | 技術的負債の洗い出しと直した記録 |
| [search-redesign.md](history/search-redesign.md) | 検索画面の刷新（ユーザの案の原文・実装前の照合・決めること） |
| [zip-prototype-instructions.md](history/zip-prototype-instructions.md) | zip から商品IDを当てる試作の指示書 |
| [zip-inspector-readme.md](history/zip-inspector-readme.md) | その試作（BoothZipInspector・BoothIdResolver）の使い方。元は直下の README（2026-09-18 に移した） |

## research（調査）

| ファイル | 中身 |
|---|---|
| [id-resolution.md](research/id-resolution.md) | zip → 商品IDの特定（18本・319本での実測） |
| [zip-linking.md](research/zip-linking.md)・[zip-linking-followup.md](research/zip-linking-followup.md) | 並行調査（PDF・CSV・他ツールのDB） |
| [fuzzy-search.md](research/fuzzy-search.md) | 曖昧検索・類義語の調査（入れない推し） |
| [ui-wording.md](research/ui-wording.md) | UI 文言の総調査と用語 |
| [memory-budget.md](research/memory-budget.md) | メモリの上限と測り方 |
| [antivirus.md](research/antivirus.md) | セキュリティソフトに怪しまれない作り |
| [youtube-terms.md](research/youtube-terms.md) | YouTube の絵とタイトルを手元に置いてよいか（絵は置かない・題は30日） |
| [booth-terms.md](research/booth-terms.md) | BOOTH・pixiv の規約とアプリの通信・名前（通信は許される範囲・名前は公開前に見直す） |
| [modification-canvas.md](research/modification-canvas.md) | 改変キャンバス（未実装） |
