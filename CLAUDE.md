# Chmonos（クモノス）— AIへの指示

BOOTH.pmで買った自分のアセットを手元で管理するWindowsアプリ（WPF / .NET 10）。**2026-10-07 に v1.0.0 を公開し、今の公開版は v1.1.0（2026-10-08）**（GitHub の Releases と BOOTH。配る zip は tag から GitHub Actions で組む：`docs/dev/signing.md`）。
名前は **Chmonos**（2026-09-14 に「BOOTH Asset Manager」から改名。`docs/research/booth-terms.md` §4）。
コードの名前空間（`Chmonos.App`・`Chmonos.Core`）・実行ファイル名（`Chmonos.exe`）・リポジトリ名（`chmonos`）・写しの置き場（`%LOCALAPPDATA%\Chmonos-sandboxes\<名前>`）も 2026-10-01 に新しい名前へ揃えた
（`BoothIdResolver`・`BoothZipInspector` は役目の名前なので残す。`docs/history/` の中は書かれた時点の古い名前のまま）。

このファイルは守ってほしい決め事。話題ごとの今の決め事は `docs/spec/`（入口は `docs/README.md`）にあり、**作業の前にその話題の spec を読む。**
App・Core のコードを触るときは、画面と文言の決め事（`.claude/rules/screen-and-wording.md`）と前提（`.claude/rules/domain-facts.md`）も読み込まれる。

日本語で書く決まり（共通の CLAUDE.md）は、英語で届く知らせ（ツールの結果・裏の作業の終わり）の直後の一言・合間の一言・サブエージェントへの指示と報告にも当てる。
英語で書き始めたら、その場で書き直す（英語だと意図を読み違える恐れがある。ユーザ指示。`.claude/settings.json` の `language` と harness のフックでも補っている）。
全プロジェクト共通の決まりは `D:\work\ClaudeCode\CLAUDE.md`、共通の部品（仕上げ・守りのフック）は `D:\work\ClaudeCode\claude-harness` の harness プラグイン。

## 絶対に破らないこと

### 1. BOOTHへの問い合わせは1本ずつ、1.5秒以上空ける

並列化しない。間隔を縮めない。相手（pixiv）のサービスに負担をかけないための決め事で、性能上の妥協ではない。
実装は `BoothClient` のゲート1箇所に集めてある——そこを通さない通信を書かない。
門は PC で1つ（`BoothMachineGate`）。保存先の違うアプリや道具を並べて動かしても、合わせて1本ずつになる。待ちを差し替えた組み立て（試験）を本物の BOOTH に向けない。

優先順位は `BoothPriority` に定義してある。人が押した通信 ＞ 指名された画像 ＞ ①② ＞ ③ ＞ ④ ＞ ⑤ ＞ ⑥ ＞ ⑦。

### 2. 購入した物をアップロードしない

`DLforTest/` と、リポジトリの直下に置いてある無視された zip・画像・`VRChat_Avatar/` は購入した実アセット。動作確認に使うだけで、どこへも送らない。
内容を要約して外部へ出すのも避ける。

### 3. 本番のデータを触らない

本番の保存先は `%LOCALAPPDATA%\Chmonos`。本番の `location.json` は友人のデータの写し（`Chmonos-sandboxes\friendtest`）を指している
（ユーザが普段の作業に使う）。`location.json` は書き換えない（harness プラグインのガードが `.claude/guard.json` に従って書き込みを止め、シェルで触れるときは確認を求める）。

- 確かめの起動は必ず `ui-check` スキルの `Start-ChmonosApp -Store <写し>`（`CHMONOS_HOME` を付け、本番と friendtest では起動を断る）。
  写しの一覧と、画面ごとにどれを使うかは `.claude/skills/ui-check/sandboxes.md`
- 確かめが終わったら `Test-ProductionUntouched` で、本番の `settings.json` の更新日時と `items/*.json` の件数が変わっていないことを確かめ、報告に書く
- アプリの一式（`AppServiceContainer`・`MainViewModel`）を組む試験・道具は、保存先を必ず指定する（`CHMONOS_HOME`）。指定が無いと `StoreLocation.Resolve` が例外で止める。
  `new App()` する道具も同じ（WPF は `Run` を呼ばなくても起動の処理を走らせる。App の側で、自分の実行ファイルから起動されたときだけ進めるよう分けてある）。
  2026-09-30 に道具が指定なしで友人の写しを開き、BOOTH へ取り直しに行った（`docs/feedback/review-2026-09-30-store-incident.md`）。`Test-ProductionUntouched` は `location.json` が指す先も比べる

### 4. 友人のデータは第三者のもの

テストに協力してくれた友人のライブラリの写し（`-friendtest`・`-eval` と、そこから作った写し）は、
リポジトリに入れない・どこへも送らない。コミットメッセージ・文書・ログ・テストの作り物のデータに商品名・ファイル名・商品IDを書かない
（「対応アバター244体の商品」のように、数と傾向だけで書く）。撮った画像も手元に置くだけ。
サブエージェントが写しの画面から拾った名前を、報告から先へ写さない。

## 作りの決め事

### 画面からの書き込みと BOOTH への問い合わせは `UiCommand` 1本

画面から書き込む処理と、BOOTH へ問い合わせる処理を呼ぶ道は `UiCommand` と `CommandHandler` の switch だけ。
新しい操作は `UiCommand` に足す。読むだけの処理（手元の JSON を読む・数える）は画面から直に呼んでよい（ユーザ判断 2026-09-14）。

- 書き込みを通すのは、古い写しで丸ごと上書きする事故を1か所で防ぐため（取り込み元・未確定の一覧が消えていた）。
  変え方を関数で渡し、錠の中で今の値に当てる（`UiCommand.ChangeSettings` など）
- BOOTH への問い合わせを通すのは、人が押した操作を取り込みより先に通す優先度（`BoothClient.Prioritize`）を入口で掛けるため
- 例外：起動時の裏の作業（残りの画像・持っていないアバターの画像・期限の来た商品）は画面が始めるが、人の操作ではないので通さない。
  取り込み画面の「足りない情報を取得」も同じ作業を今始めるだけなので通さない（量が多いので低い段のまま。ユーザ判断 2026-09-28）。
  梯子の低い段で走らせるためで、入口の「人が押した」優先度を掛けてはいけない

詳しくは `docs/spec/architecture.md`。

### item の書き込みは持ち主を宣言して、錠の中で今の値に当てる

`Local` ブロックを丸ごと書き戻すと、読んでから書くまでの間に人が触った入力が消える。
項目を書くのは `ItemRepository.SaveLocalAsync`（どの項目の持ち主かを `LocalOwners` で宣言）、一覧を足し引きするのは `ChangeLocalAsync`（変え方を関数で渡す）。
在るかを見てから作るのは `CreateOrChangeLocalAsync`、消すのは `DeleteAsync`／`DeleteIfAsync`（どれも商品ごとの錠の中）。
`Items.SaveAsync` を直接呼ぶのは、錠の中で新しい商品を作るときだけ。

画面のスレッドから来得る保存・削除は非同期で書く。同期で書き込みの門（`StoreWriteGate`）を待つと、門を開ける側と待ち合って固まる（2026-09-23 に実際に固まった）。

### JSON は人が読める形を保つ

保存するものは全部、アプリを介さず開いて読めて、直せる形にする。
計算で出せる値を JSON に書かない（`[JsonIgnore]` を付ける。`hasProgress` や `isOwnSpending` を書き出してしまう事故が繰り返し起きている）。
読むときは、欠けた配列（`null`）を空として受ける（手で直した JSON で落ちないように）。

### 公開前は、古い形のデータに合わせる変更をしない

データの形が変わったら、合っていないデータの方を問題にする。
古いデータと整合を取るためだけの欄・見分けの決まり・読むときの救済を足さない。
代わりに「どの保存先の、何件が、どう合っていないか」を報告し、直し方（消す・書き換える・取り込み直す）を聞く。本番は勝手に直さない。

### 推定した値を勝手に入れない

検出・推定で出た値（対応アバター・素体など）は候補として見せ、人が選んだときだけ入れる。

### 外から使う API は守りを外さない

アプリと同じプロセスが、設定でオンのときだけ（既定はオフ）ローカルの HTTP で答える（`Chmonos.Api`。決め事は `docs/spec/local-api.md`）。
待つのは `127.0.0.1` だけ。トークンを必須にし（`/v1/health`・`/v1/version`・`/v1/openapi.json` を除く）、ログに書かない。`Host` が `127.0.0.1`・`localhost` 以外なら断る。
CORS は使う人が許したオリジンにだけ出す。トークンを URL に入れる形は受けない。問い合わせの中身（購入履歴・検索の言葉）はログに残さない。
API から BOOTH へは問い合わせない。書き込みは設定で許したときだけで、`UiCommand` を通す（別の道で保存先へ書かない）。

## 書き方

### コメントは「なぜ」を書く。日本語で

何をしているかはコードが語る。書くのは判断の理由。数字を書くときは根拠も書く（良い例・悪い例は `.claude/rules/code-comments.md`）。

### PowerShell は落とし穴を先に読む

確かめや作り物のデータのスクリプトを書く前に `docs/dev/powershell.md`（`@($null).Count` が1・空の配列が null・別名とぶつかる関数名）。
作り物の JSON を `ConvertTo-Json` で作らない（空の配列が `null` になる）。

## 確かめ方

`dotnet build`・`dotnet test`（出力の絞り方は `.claude/wrap-up.md`）。テストは本体で約2,300件、ほかに画面（App）・検索（Query）・外から使う API（Api）・zip の読み取り・ID の特定の一式がある（全部で6つ）。全部で約16秒（2026-09-30。ビルドは別）。通信するテストを既定の一式に入れない
（BOOTHへ実際に問い合わせる確認は `experiments/` の実行ファイルで行う）。時計・実マシンのプロセス（Unity が動いているか）に結果が左右されるテストも書かない。
作業の途中は、関係する試験だけを回してよい（`dotnet test Chmonos.Core.Tests --filter "FullyQualifiedName~ImportLadder"`）。**コミットの前と統合のときは全部。**
30秒を超えるようになったら、重い試験を探して直す（探し方と、重くしない書き方は `docs/dev/app-tests.md`「重くしない」）。
App の一式だけは50秒を目安にする（結合の試験の件数で伸びていて、1か所の直しでは縮まない。ユーザ判断 2026-10-14）。

画面の側（ViewModel）にも試験の一式がある（`Chmonos.App.Tests`。書き方は `docs/dev/app-tests.md`）。
**計算で決まる物（文言・ボタンや札の出し分け・一覧に何が並ぶか・押したら何が書かれるか）は試験で確かめ、起動して撮らない。**
App を直したら、その出し分けの試験も足す。画面で見るのは、配置・色・折り返し・操作の手触りだけ。

**画面は「描画を見る」。**UI Automation は「要素があるか」しか答えず「どこにあるか」は答えないので、レイアウトは必ず画像で確かめる。
手順・道具・写しは `ui-check` スキル。UIA で届かない所だけ実入力（`mouse_event`）を使い、使う前にユーザへ告げる。

**判断の前に測る。**このプロジェクトは数字で決めてきた（「取得の76%が画像」→ 画像を切る設定。「365日の間隔だと1ヶ月の再公開を92%取り逃す」→ 30日）。
思い込みで切り捨てない。外部の資源（辞書・API）は実データに当てて数字を出してから採否を決める（置き場は `experiments/`）。
測り方が悪くて外れたこともある（「英語の経路は8件中0件」→ 割らずに引けば1位だった）。速さとメモリは `perf-measure` スキル。

## 覚えておくと早いこと

- 梯子（取り込みの順序）・所持の定義・意味検索をしない理由・同梱の辞書は `.claude/rules/domain-facts.md`（App・Core・spec を触ると読まれる）
- ライセンス：コード Apache-2.0 / 文書 CC BY 4.0（`LICENSE` `NOTICE` `LICENSE-docs.md`）
- 写しで起動しても、起動時の裏の作業（⑦ 期限の来た商品）が BOOTH へ問い合わせ、新しいバージョンの確認が1日1回 GitHub へ聞く。
  設定の「APIを使う」がオンの保存先では、外から使う API が `127.0.0.1` で待ち受ける（受けるだけで、BOOTH へは問い合わせない。`docs/spec/local-api.md`）
- リポジトリの中の地図（どのプロジェクトが何か・`mock/`・`experiments/`・直下の `research/`）は `docs/README.md` の冒頭

## 読み方（文脈を節約する）

大きいファイルを丸ごと読まないのは共通の決まり（ここでは1回の会話で読んだ量が窓の2倍を超え、約3割が丸ごとの読み直しだった。2026-09-14）。
`Grep` で場所を絞り（見出しは `^#{2,3} `）、`Read` の `offset`/`limit` で前後だけ読む。編集の直後や、変わっていない範囲は読み直さない。
撮った画像は見たい所だけ切る（`Save-ChmonosShot -Region`）。同じ状態を撮り直さない。

特に大きいもの：`docs/feedback/done-*.md`（月ごとに数百KB）、`docs/history/` の `unity-handoff.md`・`ui-revision.md`・`avatars.md`、
`docs/features.md`（1行が長い）、`Views/` の大きな XAML（`EditView`・`SearchView`・`StatsView`・`Modification*View`・`AvatarsView`・`TagManageView`・`ItemCardResources`）、
1,200行を超える ViewModel と `Chmonos.Core/Services/AvatarDetector.cs`（`ItemService`・`ImportPipeline`・`SearchModules` は 2026-10-08 に担当・段・種類ごとのファイルへ分けた）。
XAML は `x:Name`・部品の名前・コメントで、C# は関数名で Grep する。

## 進め方

- コミットはこまめに（commit・push・メッセージの決まりは共通の CLAUDE.md。メッセージは既存のログに倣う）
- 設計を変えたら `docs/spec/*.md`（1ファイル200行まで）と `docs/features.md` にも書く（どの文書を直すかの表は `.claude/wrap-up.md`）。
  決めた経緯は `docs/history/` に日付を付けて足す。history と作者の出発点のメモ `docs/history/author-memos/` は書き直さない
- 画面への意見は `docs/feedback/open.md` に記録するだけで、実装しない。実装するのは、ユーザが番号や件名を指して頼んだものだけ（`feedback-log` スキル）。
  「洗い出して」「まとめて」と言われたときも、報告までで止め、直すかを聞く
- 確かめ用のアプリは、作業を頼まれたら閉じてよい。ただし「メモ:」「報告:」で始まる発言の間と、見て決めてもらう質問の間は開いておく
- 画面の文言（`ui-wording` スキル）と、画面を変えたら直すマニュアル `manual/`（`public-docs` スキル）の決め事は `.claude/rules/screen-and-wording.md`
- 画面を直してビルドが通ったら、その画面の写しで起動したままにする（ユーザがすぐ触れるように）
- ユーザが「画面はまだ使わないで」と言ったら、アプリを閉じずに別のフォルダへビルドする
- Unity を実際に動かす確かめは、ユーザが「試して」と言ったときだけ
- 作業をサブエージェントに分けて並行で直すときは `parallel-fix` スキル。サブエージェントの報告の重い指摘は、ユーザへ伝える前に自分でコードか画像を見て確かめる

手順は `.claude/skills/` のスキル（`ui-check`・`ui-wording`・`perf-measure`・`feedback-log`・`parallel-fix`・`public-docs`）にある。
仕上げは harness プラグインの `wrap-up` スキルで、Chmonos だけの手順（テスト・文書の表・友人のデータの点検・push のしかた）は `.claude/wrap-up.md`。
`public-docs` の文体・文書の型・機械の点検は、harness プラグインの `ja-docs` スキルに任せる。
