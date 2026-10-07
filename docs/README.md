# 文書の入口

守ってほしい決め事は `CLAUDE.md`。ここはそれ以外の文書の索引。

## リポジトリの地図

| 置き場 | 中身 |
|---|---|
| `Chmonos.App/` | 画面（WPF）。`Views/`・`ViewModels/`・`Controls/`（`SuggestBox`・`ColumnsPanel` など共通の部品）・`Services/` |
| `Chmonos.Core/` | 画面に依存しない本体。`Booth/`（通信のゲート）・`Commands/`（`UiCommand`）・`Models/`・`Storage/`・`Scanning/`（取り込み）・`Resolution/`（商品IDの特定）・`Search/`・`Services/`・`Images/` |
| `Chmonos.Core.Tests/` | 既定のテスト一式（通信しない） |
| `Chmonos.App.Tests/` | 画面の側（ViewModel・App の Services）の試験。窓を出さず、一時フォルダの保存先と作り物の BOOTH で組む。書き方は `docs/dev/app-tests.md` |
| `Chmonos.Cli/` | データ層を実データで試す足場。配布物ではない |
| `BoothZipInspector/`・`BoothIdResolver/`（と `.Tests`） | zip の手掛かり読み・商品IDの特定のライブラリ（本体が使う） |
| `experiments/` | 採否を決めるための計測・試しの実行ファイル（BOOTH へ実際に問い合わせるものもここ）。一覧は `experiments/README.md` |
| `research/`（直下） | 初期の調査に使ったスクリプトと結果（結果の JSON は無視）。**調査の文書は `docs/research/`** |
| `mock/` | 画面のモック（Claude Design のキャンバス。`*.dc.html` が1画面ずつ）。今の画面とは違う所がある。キャンバスの道具ごと書き出した1枚の HTML は、許諾の無い他社のコードとフォントを含むので置かない（2026-10-07 に履歴からも外した） |
| `tools/wording.mjs` | 画面に表示される文を App と Core から集め、書き方の決まりに外れていそうな所に印を付ける（使い方はスキル `ui-wording`） |
| `tools/SandboxGen/` | 確かめ用の写しを、作り物のデータで組み立てる道具（アプリと同じ道で書く・BOOTH へ問い合わせない）。ソリューションには入れていない。呼ぶのは `ui-check` の `New-ChmonosSandbox -Recipe` |
| `tools/ViewShot/` | 窓を出さずに、画面や部品を作り物のデータで組んで PNG に描く台。前後の画像を比べる `diff` も持つ。見た目だけの確かめはアプリを起動せずにこれで行う（使い方と、確かめられない物はスキル `ui-check`「窓を出さずに描く」） |
| `.claude/skills/`・`.claude/rules/` | 繰り返す手順（`ui-check`・`ui-wording`・`perf-measure`・`feedback-log`・`wrap-up`・`parallel-fix`）と、App・Core のコードを触るときだけ読み込まれる決め事（`screen-and-wording.md`）。写しの保存先の一覧は `ui-check/sandboxes.md`、確かめの道具の一覧は `ui-check/tools.md` |
| `docs/history/author-memos/` | 作者の出発点のメモ（`作業方針メモ.md`・`モック用画面メモ.md`・`Re画面として不足しているもの（重要度順）.txt`）。2026-09-24 に直下から移した（中身は変えていない）。書き直さない |

## 文書の種類

- **`spec/`** — 話題ごとの**今の決め事**。1ファイル200行まで。冒頭に要点・コードの場所・経緯へのリンクがある。**作業の前にまず読む。**
- **`dev/`** — 作るときの落とし穴（[wpf.md](dev/wpf.md)：XAML・一覧・絵のメモリ・XML／[powershell.md](dev/powershell.md)：確かめと作り物のデータのスクリプト／
  [app-tests.md](dev/app-tests.md)：画面の側の試験の書き方・何を試験で確かめて何を画面で見るか・試験を重くしない書き方（本体の試験も）と重い試験の探し方）。書く前に読む。
  [file-lifecycle.md](dev/file-lifecycle.md) は手元のファイルが取り込み・未確定・外す・見つからない・移動・一時展開でどう記録され変わるかの地図（2026-10-05 にコードから。気になった所の一覧つき）。
  [ui-shots.md](dev/ui-shots.md) は画面の静止画の網羅の表（画面・状態 → 描画台の場面名・描けない物）と、全部を撮って索引付きの zip にまとめる `ViewShot catalog` の使い方。
  [release-handoff-2026-10-07.md](dev/release-handoff-2026-10-07.md) は公開の準備への申し送り（配る形・コード署名・データの置き場の説明・実機で確かめた動き・配ってはいけない物）。
- **`history/`** — 決めたときの経緯（grill・当時の数字・訂正）。書かれた時点の記録なので、今の決め事と食い違う所がある（そのときは spec が正）。
- **`research/`** — 調査と計測の記録。
- **`feedback/`** — 画面への意見。`open.md` が未対応・記録のみ、`done-2026-09.md`・`done-2026-10.md` が直した記録（09 は U番号・D番号。約420KB なので見出しを Grep して節だけ読む）、`ui-flows-2026-09-15.md` が画面と状態ごとの操作の動線と、意図と違う動き・その画面に無い機能の一覧（UI 案を練るときの材料）、`ui-consistency-2026-09-20.md` が語・メニュー・窓・入力・空表示の**揃っていない所**の一覧（同じく材料。W/R/M/B/D/E/I/V の番号で指す）、`behavior-2026-09-20.md` が**動き**の変な所の一覧（中断・待ち・同時に走ったときの取り違え・保存の競合・裏の作業・データの決め事。L/C/N/P/G/X/J/Q の番号で指す）、`review-2026-09-23.md` がその後の全体の点検と直した記録（判断をもらった2件を含む）、`review-2026-09-29.md` が直しの漏れの洗い出し、`review-2026-09-30-store-incident.md` が道具が友人の写しを開いて動かした件（2026-09-30）、`overnight-2026-09-29.md`・`overnight-2026-09-30.md` が夜の作業の報告、`notice-placement-2026-10-03.md` が操作の知らせの置き場の洗い出し（メモ20-②③）、`manual-check-2026-09-28.md`・`manual-check-2026-10-02.md`（この2つは役目を終えた）・`manual-check-2026-10-03.md`・`manual-check-2026-10-04.md`・`manual-check-2026-10-04-night.md` が、人が見ないと決められない物の確認項目。
- **`features.md`** — 機能の一覧（何・なぜ・状態・出典）。1行が長いので行を Grep して直す。

## spec（今の決め事）

| ファイル | 話題 |
|---|---|
| [data-model.md](spec/data-model.md) | 保存するファイルの一覧、商品の `booth`／`local`、手元のファイル、書き方、所持の数え方 |
| [architecture.md](spec/architecture.md) | `UiCommand` を通す物、持ち主を宣言した書き込み、スレッドとディスク、画面の履歴、partial の分け方、試験 |
| [import.md](spec/import.md) | 取り込みの周回と梯子、積む・取り消す・中断、未確定の一覧の合わせ方、⑦ 取り直し |
| [missing-search.md](spec/missing-search.md) | 取り込み画面の「見つからないファイルを探す」：探す範囲の窓、結び直し・日時の書き方、登録したフォルダの候補と差し替え |
| [background-and-network.md](spec/background-and-network.md) | BOOTH への通信の決め事、優先度、起動時の裏の作業、失敗の書き残し（`logs/app.log`） |
| [id-resolution.md](spec/id-resolution.md) | 商品IDの手掛かり、ファイル名からの自動検索、未確定の画面、「この商品から外す」 |
| [private-items.md](spec/private-items.md) | BOOTH に無い商品（仮ID `local-`）、人が入れる名前・カテゴリ・ショップ、「IDを変更」 |
| [search.md](spec/search.md) | 文字列の構文、表記をまたぐ辞書、結果のカード（絞り込みは下） |
| [search-filters.md](spec/search-filters.md) | 検索の絞り込み：条件の種類と意味・同じ種類を複数・「除く」・見出しのメニュー・編集状況・照らし方と選択肢の件数・入口と履歴 |
| [search-recent.md](spec/search-recent.md) | 検索の条件「最近」：最後にそれをしてからの日数の帯とスライダ2本・帯の範囲（一週間〜全期間）・はみ出しの寄せ方・除くと記録の無い商品・表示順のボタン |
| [search-saved.md](spec/search-saved.md) | 保存した検索：保存する物・「条件を追加」の隣のボタンから開く一覧・保存の小窓・呼び出して置き換える決まり・キーボード |
| [item-page.md](spec/item-page.md) | 商品ページ、画像（自分で足す・サムネイルの指名）、手元のファイル、購入記録、編集画面 |
| [avatars.md](spec/avatars.md) | 対応アバターの検出、登録簿、共通素体、相性の3段階、所有アバター |
| [modifications.md](spec/modifications.md) | 改変の記録（構成物・画像・Unity プロジェクト）、改変の画面、「Unityで選択」 |
| [unity.md](spec/unity.md) | Unity への受け渡し（窓を名指しして送る・連続送り・入り先・プロジェクトタブ・VCC） |
| [folder-view.md](spec/folder-view.md) | フォルダビューの根の決め方、右に出す物、ドライブ文字の読み替え、速さ |
| [ui-rules.md](spec/ui-rules.md) | ナビ、並べ替えの出し方、窓と画面の状態、画面の幅、ドロップ、キー |
| [notifications.md](spec/notifications.md) | 通知の画面（旧「要確認」）：束8つ・行・既読と解消済み・形式の変化の帯 |
| [shops.md](spec/shops.md) | ショップの中：バナーの畳み・1本のスクロール・1行の見出しとメモ・一覧の仮想化・戻る進むでの位置の戻し |
| [ui-colors.md](spec/ui-colors.md) | 色の表（明るい・暗い）の鍵と役割、色を直に書かない決まり、表示の色の切り替え、暗い表のコントラスト、標準の部品の見た目 |
| [ui-writing.md](spec/ui-writing.md) | 画面の文言の書き方（1つの文言に載せるもの・長さ・要素ごとの型・正確さを落とさない） |
| [ui-terms.md](spec/ui-terms.md) | 画面の用語表（内部の言葉・同じ操作の動詞・使わない語・揃える表記・まだ決めていない揺れ）。`tools/wording.mjs` が読む |
| [ui-dialogs.md](spec/ui-dialogs.md) | 別の窓（ダイアログ）の Enter・Esc、ボタンの並びと名前、取り返しの一文、確認と失敗のアイコン |
| [ui-input.md](spec/ui-input.md) | 候補の付け方、Enter で決める範囲、名前の変え方、メモの保存、編集画面の保存の分かれ方、右クリックのメニュー、読み上げの名前 |
| [ui-keyboard.md](spec/ui-keyboard.md) | キーの割り当て（Ctrl+F・Ctrl+Enter・Alt+矢印など）、キーボードだけで進める決まり（並び・止まり直し・条件の2段の止まり・乗せたときだけ出すボタン） |
| [ui-empty-and-errors.md](spec/ui-empty-and-errors.md) | 空の表示の3通りの書き分け、押せない理由の表示、知らせずに捨てない失敗、エラーの型、利用者に見せない文、知らせの置き場の共通の決まり |
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
| [booth-machine-gate-2026-09-30.md](history/booth-machine-gate-2026-09-30.md) | BOOTH への問い合わせの門を PC で1つにした経緯（選んだ仕組み・2つのプロセスで測った間・一時展開の置き場所を保存先ごとに） |
| [dotnet10-2026-09-30.md](history/dotnet10-2026-09-30.md) | .NET 9 から .NET 10 へ上げた記録（上げた物・絵と木の比べ・配る物の大きさ・速さとメモリ・互換性の表・確かめていない事） |
| [rename-chmonos-2026-10-01.md](history/rename-chmonos-2026-10-01.md) | コード・実行ファイル・リポジトリ・写しの置き場の名前を Chmonos に揃えた記録（変えた物・残した物・確かめ） |
| [private-items.md](history/private-items.md) | 非公開商品の取り込み（38問） |
| [user-images.md](history/user-images.md) | 自分で足す画像 |
| [modifications.md](history/modifications.md) | 改変の記録と改変の画面の刷新 |
| [unity-handoff.md](history/unity-handoff.md) | Unity への受け渡しの実測（シェル・メニュー・ログ監視・プロジェクトタブ・VCC） |
| [folder-view.md](history/folder-view.md) | フォルダビューの根の決め方の試しと速さ |
| [tech-debt-2026-09-14.md](history/tech-debt-2026-09-14.md) | 技術的負債の洗い出しと直した記録 |
| [ui-wording-2026-09-24.md](history/ui-wording-2026-09-24.md) | 画面の文言の AI らしさの点検（型と件数・元になった決め事・手本）と、書き方の決まり・用語表・点検の道具を作り直した理由 |
| [app-tests-2026-09-30.md](history/app-tests-2026-09-30.md) | 画面の側の試験の一式を作った経緯（なぜ・どう組んだか・途中で踏んだ物・書いていて見つけた食い違い） |
| [search-redesign.md](history/search-redesign.md) | 検索画面の刷新（ユーザの案の原文・実装前の照合・決めること） |
| [search-modules-2026-10-01.md](history/search-modules-2026-10-01.md) | 検索の条件を同じ種類で複数・「除く」・編集状況にした記録（ユーザ判断・実装で決めた所・照らす重さの前後） |
| [keyboard-groups-2026-10-01.md](history/keyboard-groups-2026-10-01.md) | 並びは Tab で1回、中は矢印にした経緯（キーボードだけで進める） |
| [keyboard-cards-2026-10-01.md](history/keyboard-cards-2026-10-01.md) | カードの一覧も並びに入れ、キーボードで届かない所を塞いだ経緯（担当KEY2） |
| [search-2026-10-02.md](history/search-2026-10-02.md) | 検索の手直し（2026-10-02 のメモ1・2と「更新あり」の件） |
| [saved-search-button-2026-10-05.md](history/saved-search-button-2026-10-05.md) | 保存した検索の置き場を、畳める節からボタンの一覧へ変えた経緯 |
| [zip-prototype-instructions.md](history/zip-prototype-instructions.md) | zip から商品IDを当てる試作の指示書 |
| [zip-inspector-readme.md](history/zip-inspector-readme.md) | その試作（BoothZipInspector・BoothIdResolver）の使い方。元は直下の README（2026-09-18 に移した） |
| [author-memos/](history/author-memos/) | 作者の出発点のメモ（`作業方針メモ.md`・`モック用画面メモ.md`・`Re画面として不足しているもの（重要度順）.txt`。2026-09-07〜08）。grill-1・grill-2 はこれを前提に決め事を確定させた。2026-09-24 に直下から移した（中身は変えていない） |

## research（調査）

| ファイル | 中身 |
|---|---|
| [id-resolution.md](research/id-resolution.md) | zip → 商品IDの特定（18本・319本での実測） |
| [zip-linking.md](research/zip-linking.md)・[zip-linking-followup.md](research/zip-linking-followup.md) | 並行調査（PDF・CSV・他ツールのDB） |
| [fuzzy-search.md](research/fuzzy-search.md) | 曖昧検索・類義語の調査（入れない推し） |
| [ui-wording.md](research/ui-wording.md) | UI 文言の総調査と用語（2026-09-09 の時点。今の用語表は spec/ui-terms.md） |
| [memory-budget.md](research/memory-budget.md) | メモリの上限と測り方 |
| [large-files-2026-09-30.md](research/large-files-2026-09-30.md) | 大容量のファイル（大きな zip・中身の多い zip・重い unitypackage・大量のファイル）での固まりとメモリ |
| [store-transfer-2026-10-01.md](research/store-transfer-2026-10-01.md) | 保存先の引越しとバックアップを作り物の2GBで通した（時間・メモリ・一致・途中で止めたときの残り） |
| [item-page-open-2026-09-30.md](research/item-page-open-2026-09-30.md) | 商品ページを開く速さ（説明の長さと UI Automation の相手で変わる内訳・履歴と足跡の書き込みを画面のスレッドの外へ・説明を後で作る試作と View を持ち回す試作の数字） |
| [startup-dotnet10-2026-10-01.md](research/startup-dotnet10-2026-10-01.md) | .NET 10 で起動が遅くなった所（JIT が 1 関数あたり約 1.4 倍）と、縮める手（事前翻訳・PGO・段階の翻訳）の効き目 |
| [search-modules-2026-10-01.md](research/search-modules-2026-10-01.md) | 検索の条件の案（2026-10-01 に実装。変えた所は §12）：同じ種類を複数・「除く」・未編集を項目ごとに・同じ種類を隣に並べる。条件ごとの照らす重さ・照らす順・選択肢の件数の計測 |
| [antivirus.md](research/antivirus.md) | セキュリティソフトに怪しまれない作り |
| [youtube-terms.md](research/youtube-terms.md) | YouTube の絵とタイトルを手元に置いてよいか（絵は置かない・題は30日） |
| [booth-terms.md](research/booth-terms.md) | BOOTH・pixiv の規約とアプリの通信・名前（通信は許される範囲・名前は公開前に見直す） |
| [modification-canvas.md](research/modification-canvas.md) | 改変キャンバス（未実装） |
| [alcom.md](research/alcom.md) | ALCOM（VCC の代わりのツール）の置き場所・外から呼ぶ口・この PC の入り方と連携の案（未実装） |
