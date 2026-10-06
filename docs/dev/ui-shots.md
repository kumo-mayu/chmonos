# 画面の静止画の網羅 — どの画面のどの状態を、どの場面が撮るか

`tools/ViewShot` の場面で、画面ごと・状態ごと・部品ごとに静止画を撮る。全部を一度に撮って見て回るときは `catalog`。
台の仕組みと場面の足し方は `ui-check` スキル（`.claude/skills/ui-check/SKILL.md`「窓を出さずに描く」）。2026-10-05 にこの表を作った。

```powershell
dotnet run --project tools/ViewShot -- catalog                       # 全部（明・暗、一部は幅 900 も）→ %TEMP%\chmonos-shots\catalog-<日時>\ と .zip
dotnet run --project tools/ViewShot -- catalog --only search-,catalog-module- --out <置き場>   # 名前の頭で絞る
dotnet run --project tools/ViewShot -- catalog --changed --from <前の回の置き場>   # 触った画面だけ撮り、残りは前の回の画像を写す
dotnet run --project tools/ViewShot -- catalog --changed HEAD~2 --dry              # 選ばれる場面を出すだけ（撮らない）
dotnet run --project tools/ViewShot -- diff <前の回の置き場> <今回の置き場> --out <違いの置き場>   # 画面ごとのフォルダごと比べる
```

- 画面ごとのフォルダ（`01-search` … `18-parts`）に `<場面>-light|dark[-w幅].png`。直下に `index.html`（見出し・縮小・押すと原寸・明暗の切り替え・名前で絞る）・`summary.txt`（時間の内訳と重い場面）・`times.tsv`（場面ごとの内訳）・`results.json`（次の回の `--from` が読む）
- 失敗した場面があっても止めずに続け、索引で赤く印を付ける。「落ち着かなかった」「知らせの窓が出ようとした」「アプリのログ」は注意として載せる
- 1場面が 240 秒（`--timeout`）を超えたら止めて次へ。並べる数は `--jobs`（既定は CPU の論理数の 3/4。16 の PC で 12）。重い場面から先に始める（`--from` があれば前の回の時間で並べる）
- 2026-10-05 夜の通し：329 場面・718 枚・**2.9 分**（2回とも。12 本）・zip 約89MB。速くする前は 326 場面・710 枚・9.5 分（4 本。ほかの担当と同時で、昼は 8.3 分）
- **`--changed [基準]`**：基準（既定は master との分かれ目）から今の作業の木までに触ったファイルで場面を選ぶ。ファイル名の語で画面のフォルダに当てる（`ImportView.xaml`・`ImportViewModel.cs` → `05-import`。表は `CatalogChanged.cs`）。
  場面のファイル（`Scenes.*.cs`）は書いてある場面の名前で当てる。語に当たらない App の画面のファイル・色の表・共通の部品・主の窓・台そのもの（`Stage`・`SceneContext`・`Program`・`Fake`・`Isolation`）は全部。
  Core・試験・文書は、語に当たった物だけ。`Backdoor.cs` は見ない（今ある入れ方を変えたら `--only` で選ぶ）
- **`--from <前の回の置き場>`**：撮らなかった場面の画像と注意を前の回から写し、索引を全部の場面で作り直す（写した場面には「前の回の画像」と出る）。1場面だけ撮り直して約11秒
- 場面が自分で書く測った値（流れの位置・件数）は索引に薄く出すだけで、注意には数えない
- 撮らない場面：重さを測るための場面（`perf-*`・`card-info-perf`・`drag-edge-scroll-measure`・`shop-300`。見た目は同じ画面のほかの場面で撮っている）
- 幅 900（窓の最小）でも撮るのは、`empty-*` と、主な画面の代表（`Catalog.cs` の `NarrowToo`）。全部を2つの幅にすると時間と枚数が倍になるため
- 置き場が空でないと断る（古い画像が zip に混ざらないように）。撮った物はリポジトリに入れない

## 表の読み方

「状態 → 場面」。`（900）` は幅 900 でも撮る場面。**未** は静止画で見る価値があるがまだ場面が無い物、**描けない** は台では描けない物（理由は下の節）。
明るい色と暗い色は全部の場面で撮るので、表には書かない。

## 検索（01-search）

| 状態 | 場面 |
|---|---|
| 空の保存先 | `empty-search`（900） |
| 1件 | `catalog-search-one` |
| 数件・長い名前・絵の無い商品 | `search-cards`（900） |
| 最初に開いた（既定の条件3つ） | `search-first-open`（900） |
| 何も当たらない | `catalog-search-no-hits`（900） |
| 多数（2000件）・カードの札 | `card-info`・`card-info-small`・`card-info-sorted`・`card-info-chosen` |
| 乗せたときの札（ViewModel で乗せた状態） | `card-info-peek`・`card-info-peek-small` |
| カードの下の段（幅 160・200・228） | `card-badges-160`・`card-badges-200`・`card-badges-228` |
| R-18 の名前の頭の印（カード・リスト） | `search-cards-adult`・`search-list-adult` |
| リスト | `card-info-list`・`search-sort-name-list`・`search-updated-list` |
| 並べ替えの区切り（カテゴリ・ショップ・入手日・名前） | `search-sort-category(-list)`・`search-sort-shop(-list)`・`search-sort-acquired(-list)` |
| カードの大きさ最小・最大 | `search-sort-shop-small`・`search-sort-shop-large` |
| フォーカスの枠 | `search-sort-shop-focus`・`search-sort-name-focus` |
| 選んだ（下の帯） | `search-selected-updated`・`search-selected-quiet` |
| 札「更新あり」・壊れたzip・見つかりません・BOOTHに無い | `search-updated`・`search-broken-zip`・`search-missing-file`・`search-not-on-booth` |
| 保存した条件（0・3・12件・閉じた姿） | `search-saved-none`・`search-saved-some`・`search-saved-many`・`search-saved-closed` |
| カレンダー（日付の条件） | `calendar` |
| 読み込み中（最初の1コマ。明るい色だけ） | `catalog-search-first-frame` |
| カードの右クリック・「条件を追加」のメニュー・「…」 | **描けない**（ポップアップ。右クリックは U3 の担当） |

## 検索の条件（02-search-modules）

種類（`SearchModuleKind`）ごとに **足した直後** `catalog-module-<種類>` と **値を入れた姿** `catalog-module-<種類>-set` を撮る。
種類の一覧から場面を作るので、種類を足すと場面も増える。値を入れた姿は、一覧の型は候補の先頭2つ、範囲は下と上、日付は始めと終わり、選ぶ物は2つ目。

| 状態 | 場面 |
|---|---|
| 27種の足した直後 | `catalog-module-category` … `catalog-module-updated`（27場面） |
| 24種の値を入れた姿 | `catalog-module-*-set`（改変・Unityプロジェクトは作り物に選べる物が無いので無し。編集状況は下の2つ） |
| 編集状況（未入力のみ・両方） | `search-edit-status`・`search-edit-status-both` |
| 除く・畳んだ姿・AND の要約 | `search-exclude` |
| 同じ種類を2つ・隣に並べた後 | `search-many`・`search-many-grouped` |
| 価格の外れ値（払った額・BOOTHの価格） | `search-price-outliers-paid`・`search-price-outliers-booth` |
| 改変の2段（改変2つを AND・アバター2体を AND）・アバターだけの枠 | `search-modification-two-level`・`search-modification-avatar-only` |
| ユーザータグの AND の中の「小分類なし」（警告の色） | `search-user-tag-no-sub-and` |
| 長いパスの札（ファイルの場所・Unityプロジェクト）と候補 | `search-long-paths`・`suggest-path-long` |
| ギフト・見つからないファイルの補助のチェック | `search-choice-flags` |
| 最近（一か月の帯・7〜28日前・新しい順に並べた後は窓ごと／一週間の「それより前」の1本・除く） | `search-recent`（900）・`search-recent-week`（900）。場面は `01-search` に入る |
| 改変の候補（アバター → 改変・語を区切って当てる・札） | `suggest-modification-alias`（候補の欄だけを描く） |
| 候補の一覧が開いた所（打ちかけ） | **描けない**（候補はポップアップに出る） |

## 商品ページ（03-item）

| 状態 | 場面 |
|---|---|
| 全体（絵3枚・ファイル1つ） | `item-page`（900） |
| BOOTHに無い商品 | `catalog-item-local-only`（900） |
| 絵が無い・持っていない・BOOTHから消えた・隠した（メモあり） | `catalog-item-no-images`・`catalog-item-not-owned`・`catalog-item-delisted`・`catalog-item-hidden` |
| 対応アバター29体・共通素体 | `item-page-avatars` |
| 確認待ちの対応アバター（見出しの［すべて確認済みにする］・札にフォーカスして ✓） | `item-page-avatars-unconfirmed`・`item-page-avatars-unconfirmed-focus` |
| 手元のファイルの状態（在る・壊れた・見つからない・外したドライブ・2箇所） | `item-files-states` |
| 長い説明（開いた・畳んだ・流した・最後・縦長） | `item-page-long`・`item-page-long-*` |
| 読み込み中（最初の1コマ） | `item-page-long-first-frame` |
| 流した位置を保つ（次・戻る・進む・開き直す） | `item-page-next-after-scroll`・`item-page-back-*`・`item-page-forward-after-back`・`item-page-reopen-after-scroll`・`item-page-files-*` |
| 欄の下の知らせ（出る前・出た後） | `item-page-notices-none`・`item-page-notices-shown` |
| ユーザータグ（小分類・畳んだ） | `item-page-user-tags`・`item-page-user-tags-folded` |
| BOOTHで変わった所（帯・足しただけ・消しただけ・既読） | `item-page-changes`・`item-page-changes-*` |
| 購入の日付 | `item-page-purchase-dates` |
| フォルダビュー・改変の画面に組み込んだ商品ページ | `folder-item-*`・`modification-item-*` |
| ファイルの行のメニュー・吹き出し | **描けない**（ポップアップ。行の文言は F2 の担当） |

## 編集（04-edit）

| 状態 | 場面 |
|---|---|
| 札（小分類・バリエーション） | `edit-chips`（900） |
| 購入の日付の欄 | `edit-purchase-dates` |
| 長い説明の欄 | `edit-description` |
| 編集する物が無い（空） | `catalog-edit-empty` |
| 未編集の商品3件で開いた所（上の並び・右の欄） | `catalog-edit-start` |

## 取り込み（05-import）・未確定（06-resolve）

| 状態 | 場面 |
|---|---|
| 取り込み：空 | `empty-import`（900） |
| 取り込み：結果（読めない・OneDrive・壊れたzip・長い名前） | `import-result-unreadable`（900）・`import-result-one-broken` |
| 取り込み：取り込んでいる最中（段・件数・棒・中断・残りの見込み・下の1行）・スキャン（棒なし）・減速 | `import-running`（900）・`import-running-scanning`・`import-running-throttled` |
| 取り込み：「探す」の窓と候補 | F3 の担当が足す |
| 未確定：空 | `empty-resolve`（900） |
| 未確定：一覧（壊れたzip・束・行の札・順番待ち） | `resolve-broken-zip`（900）・`resolve-row-badges`・`resolve-bundles`・`resolve-queue` |
| 未確定：聞き直して公開されていた後（画像の枠が残る） | `resolve-not-on-booth-overturned` |
| 未確定：右の欄（1件・束・元zip無し・フォルダ・チェック3件・画像・中身7万件） | `resolve-target-*`・`resolve-unpacked-folder`・`resolve-checked-local`・`resolve-many-contents` |
| 未確定：登録している最中（残りの件数と目安の時間） | `resolve-registering-eta` |

## ショップ（07-shops）・フォルダビュー（08-folder）

| 状態 | 場面 |
|---|---|
| ショップ一覧：空・2店・更新あり・更新ありだけ | `empty-shops`（900）・`shops-cards`（900）・`shops-cards-updated`・`shops-cards-updated-only` |
| ショップの中：一番上・半分・流れ去った後・一番下（カード・リスト） | `shop-header`（900）・`shop-scroll-*`・`shop-list-*` |
| ショップの中：メモ無し・長いメモ・切り替え・フォーカス | `shop-compact-memo-none`・`shop-compact-memo-long`・`shop-switch-keeps`・`shop-focus-reveal` |
| フォルダ：空・木（ファイル名・商品名）・選んだ・知らせ | `empty-folder`（900）・`folder-tree-filenames`・`folder-tree-itemnames`・`folder-cards`（900）・`folder-notice-off`・`folder-notice-on` |

## アバター（09-avatars）・改変（10-modifications）

| 状態 | 場面 |
|---|---|
| アバター：空・カード・リスト・持っていない・探す | `empty-avatars`（900）・`avatars-cards`・`avatars-list`・`avatars-cards-unowned`・`avatars-match-note` |
| アバター：選んだ右の欄・知らせ・検出の結果の長い文 | `avatars-detail`（900）・`avatars-notice-off`・`avatars-notice-on`・`avatars-status` |
| 共通素体：一覧・詳細・足す欄・推定・知らせ | `avatars-bases`・`avatars-base-members`・`avatars-inferred-base`・`avatars-base-notice-*` |
| 候補（アバター・改変の呼び方） | `suggest-avatar-groups`・`suggest-avatar-divider`・`suggest-modification-alias` |
| 改変：空（プロジェクト・アバター・改変の見方） | `empty-hub-project`・`empty-hub-avatar`・`empty-hub-modification`（900） |
| 改変：最小の幅・選んだ・縦長・帯の知らせ・複製・逆順 | `modification-hub-narrow`・`modification-selected-*`・`modification-duplicate`・`modification-members-reversed` |
| 改変：知らせ（出る前・後）・プロジェクトの中の結果 | `modification-detail-notice-*`・`modification-hub-notice-*`・`modification-project-found` |
| 改変：Hub・VCC・ALCOM の有無（画面と設定） | `hub-tools-*`・`settings-tools-*` |
| 改変：窓（使ったものを足す・改変に追加・探す・絵） | `pick-member-files-dialog`・`pick-modification-*-dialog` |
| 改変：送る unitypackage を選ぶ窓 | `catalog-dialog-pick-packages` |

## タグの管理（11-tags）・属性の管理（12-attributes）

| 状態 | 場面 |
|---|---|
| 空 | `empty-tag-manage`・`empty-attribute-manage`（900） |
| 選んだ（カード・リスト） | `tag-manage-cards`（900）・`tag-manage-list`・`attribute-manage-cards`（900）・`attribute-manage-list` |
| 小分類（列・1列・足す欄） | `tag-manage-cards-subs-columns`・`tag-manage-list-subs-column`・`tag-manage-sub-add-open`・`tag-manage-sub-existing` |
| 打った（新しい名前・今ある名前・理由・商品の語） | `tag-manage-typed-*`・`tag-manage-match-reason`・`attribute-manage-typed-*`・`attribute-manage-search-items` |
| 手動の並べ替え・知らせ | `tag-manage-manual`・`tag-manage-notices`・`attribute-manage-notices` |
| 行の右クリック（中身を外して描いた） | `tag-manage-row-menu`・`attribute-manage-row-menu` |
| 窓：名前を変える（入力前・統合）・小分類の移動（選ぶ前・後）・属性の統合（ぶつかる・ぶつからない） | `catalog-dialog-rename-tag(-merge)`・`catalog-dialog-move-sub(-none)`・`catalog-dialog-merge-attribute(-plain)` |

## 通知（13-inbox）・統計（14-stats）・設定（15-settings）

| 状態 | 場面 |
|---|---|
| 通知：空・束と行・読めない商品・知らせ・行の差分・指して開く | `empty-inbox`（900）・`inbox-rows`（900）・`inbox-unreadable`・`inbox-notices(-none)`・`inbox-lines`・`inbox-target` |
| 統計：空・8件 | `empty-stats`（900）・`stats`（900） |
| 統計：60件・12店 | `catalog-stats-many` |
| 設定：全体（除外 0・3・5000件）・欄を開いた形 | `settings-excluded-*` |
| 設定：長いパス・区切り・保存先の作業中・書き出し・既定に戻す・知らせ | `settings-long-paths`（900）・`settings-sort-dividers-*`・`settings-store-blocked`・`settings-backup-exported`・`settings-reset-all(-done)`・`settings-notes-*` |
| 設定：カードに出す属性 | `card-info-settings` |

## ナビと下の帯（16-nav-bands）・小窓（17-dialogs）・部品（18-parts）

| 状態 | 場面 |
|---|---|
| ナビ：低い窓・畳む・送る・フォーカス | `nav-low-*`・`nav-focus` |
| ナビ：数の札（未確定・未編集・通知） | `catalog-nav-badges` |
| 下の帯：一時展開・バックアップ（中・数える・済み・失敗）・戻す | `band-unpacking(-two)`・`band-backup-*`・`band-restore-running` |
| 主の窓の知らせの帯（隠した・離れたページの操作・取り込みを始められない・監視をやめた） | `catalog-notice-hidden`・`catalog-notice-changed-away`・`catalog-notice-import-not-started`・`catalog-notice-folder-removed` |
| 知らせの窓（OK/キャンセル・はい/いいえ・エラー） | `notice-okcancel-long`・`notice-yesno-warning`・`notice-error-short` |
| 2択・3択の窓 | `catalog-dialog-choice-two`・`catalog-dialog-choice-three` |
| 検索の保存・名前を変える（今ある名前） | `catalog-dialog-saved-search-save`・`-taken`・`-rename` |
| 商品IDを変える窓（確かめる前） | `catalog-dialog-change-item-id` |
| 初回の窓（長いパス・短いパス・環境変数） | `first-run-*` |
| 名前の「_」 | `underscore-in-names` |

## 時間の内訳と速くした所（2026-10-05 夜に測った）

速くする前の通し（326 場面・4 本・9.5 分）の場面ごとの合計 2107 秒の内訳：描画が止まるのを待つ 1197 秒（57%）・作り物を書く・組む・描く残り・**プロセスの起動と App の組み立ては 94 秒（4.5%）**。
場面ごとにプロセスを起こし直す手間は小さいので、1つのプロセスで続けて描く作りにはしなかった（静的な状態を場面ごとに戻す手間と、前の場面を引きずる危うさに見合わない）。直した所：

| 直した所 | 前 → 後 |
|---|---|
| 並べる本数（4 → 論理数の 3/4）。待ちの間は CPU が空く | 8 本 3.4 分・12 本 2.9 分・16 本 2.7 分（16 本では流す位置が決まりきらない場面が出た） |
| 回り続ける棒（長さの無い進み具合）を比べる画素から外す | 棒を出す 3 場面が待ちの上限を使い切っていた：1場面 27 秒 → 3.5 秒 |
| 作り物の絵（WebP）を控えて写す | 8件の場面で作り物を書くのが 1.5 秒 → 0.4 秒 |
| `card-info` の 2000 件を裏のスレッドで並べて書く（1件ずつディスクまで書き切るため） | 作り物を書くのが 20 秒 → 3.3 秒（絵は同じ） |
| 前に落ち着いた絵と同じ所から始まる待ちは 0.15 秒で抜ける（変われば 0.4 秒に戻す） | 場面ごとの合計 1842 → 1630 秒 |
| `avatars-list`・`avatars-cards` の流しながら行を数える1歩は 0.1 秒待ち（行の型を数えるだけ） | 75 秒 → 47 秒 |
| 重い場面から先に始める | 最後に `card-info`（35秒）が1本だけ残っていた |

落ち着いたとみなす長さ（0.4 秒）は縮めない。止まって見えた後に変わった間（`times.tsv` の `brokenQuiet`）を測ると最大 0.85 秒の場面があり、原因は2つだった（2026-10-06 に直した）：
- **通知の画面などのナビの数（「通知 2」が 1 に変わる）**：ナビの件数の数え直しは、続けて頼まれると 1 秒にまとめて遅れて読む（`MainViewModel.CountsInterval`）。台は `SceneContext.StartAsync` でこれを 0 にして、その場で読ませる（試験の `TestApp` と同じ）。`inbox-*` の約 0.45 秒後の変化が消えた
- **改変の画面を選んで開く場面**：読み込み（0.5〜1 秒）の間の「読み込んでいます…」の絵が止まって見え、窓に載せた最初の落ち着きがそこで止まっていた。載せる前に `UntilHubLoadedAsync`、載せた後に `UntilModificationShownAsync` で、決めた時間でなく済んだ印（`EmptyText` が「読み込んでいます…」でなくなる・詳細の ViewModel が付く）で待つ

`brokenQuiet` は、重い PC（`--jobs 20`）だと 1 周が遅くなって実際より長く出る。変化の有無は、同じ条件で通して `diff` で見る。
速くした前後の画像は、回り続ける棒（`resolve-registering-eta`・`band-backup-counting` が回ごとに数画素違う。台では描けない物）・`catalog-edit-empty`（数え下げで検索へ戻っていた。止めて直した）のほかは同じ。
作り物の保存先の場所は設定の画面の下に出る。以前はプロセス番号が入って設定の 18 枚が毎回違ったので、今は `<場面の名前>\<空き番号>`（1本だけ走る回は毎回 1。同じ場面を同時に描く回は錠ファイルで 2、3 に分かれる。`Isolation.TakeSlot`）にした。
`--jobs` を既定の 12 より増やす（20）と、`empty-search`・`tag-manage-cards`・`catalog-edit-empty` が数画素〜1 本の縦棒だけ回ごとに変わることがある（遅れて出る物を 0.4 秒で取り逃す。既定の本数では出ない）。

## 描けない物（理由）

台は窓の中身を外して見えない窓口に載せて描く（`Stage.cs`）。次は別の窓・Windows・時間に頼るので描けない。アプリを起動して確かめる。

- **ポップアップに出る物**：右クリックのメニュー（タグ・属性の行は中身を外して描いた。カード・ファイルの行は U3 の担当）・プルダウンの開いた一覧・「条件を追加」と「…」のメニュー・候補の一覧・吹き出し
- **マウスを乗せた色・押した色・ドラッグの最中**（ViewModel で作れる「乗せた札」だけは描いた）
- **動く物**：長さの無い進み具合の棒・アニメーション・流している最中。回り続ける棒のある場面（`resolve-registering-eta`・`band-backup-counting`）は撮るたびに数画素違う（台で同じ絵にできない。2026-10-05 ユーザ判断で注記だけ。`diff` ではこの2場面を読み飛ばす）
- **Windows が描く物**：窓の題の帯と枠・ファイルやフォルダを選ぶ窓・起動の瞬間の白い地
- **この PC に左右される物**：Unity Hub の一覧（Hub・VCC・ALCOM の有無は場面で作り物に差し替えた）
- **小窓の外側**：小窓は中身だけ描く（主の窓に重なった姿は描かない）

## 数（2026-10-05）

画面の塊 18（ナビの画面 14 と、ナビと帯・小窓・部品）、状態の行 80（1行に似た状態をまとめた所がある）。
うち場面がある 76（95%）、**未** 0、**描けない** 3、他の担当が足す 1（取り込んでいる最中は 2026-10-05 夜に足した。`Backdoor.ShowImportRunning`）。
この表を作る前は 58 行（73%）で、足した場面 80（条件の種類 51・小窓 13・画面の状態 12・知らせの帯 4。`Scenes.Catalog.cs`）で 17 行を埋めた。
条件の種類は 27 のうち 9 種しか場面が無かった（直したことのある種類だけ）。
