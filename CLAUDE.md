# Chmonos（クモノス）— AIへの指示

BOOTH.pmで買った自分のアセットを手元で管理するWindowsアプリ（WPF / .NET 10）。公開中（GitHub の Releases と BOOTH。配る物の組み方は `docs/dev/signing.md`）。
旧名は「BOOTH Asset Manager」（`docs/research/booth-terms.md` §4）。`BoothIdResolver`・`BoothZipInspector` は役目の名前なので残す。`docs/history/` の中は古い名前のまま。

話題ごとの今の決め事は `docs/spec/`（入口とリポジトリの地図は `docs/README.md`）。作業の前にその話題の spec を読む。
App・Core を触ると `.claude/rules/` の画面と文言の決め事・前提（梯子・所持の定義など）も読み込まれる。
日本語の決まりは、英語の知らせ（ツールの結果・裏の作業の終わり）の直後の一言と、サブエージェントへの指示・報告にも当てる。英語で書き始めたら書き直す（ユーザ指示）。

## 絶対に破らないこと

### 1. BOOTHへの問い合わせは1本ずつ、1.5秒以上空ける

並列化しない。間隔を縮めない。相手（pixiv）のサービスに負担をかけないための決め事で、性能上の妥協ではない。
**`BoothClient` のゲートを通さない通信を書かない。**門は PC で1つ（`BoothMachineGate`）。待ちを差し替えた組み立て（試験）を本物の BOOTH に向けない。
優先順位は `BoothPriority`。

### 2. 購入した物をアップロードしない

`DLforTest/` と、直下の無視された zip・画像・`VRChat_Avatar/` は購入した実アセット。動作確認に使うだけで、どこへも送らない。内容を要約して外へ出すのも避ける。

### 3. 本番のデータを触らない

本番の保存先は `%LOCALAPPDATA%\Chmonos`。その `location.json` はユーザが普段使う保存先を指す（`.claude/guard.json`）。`location.json` は書き換えない。

- 確かめの起動は `ui-check` スキルの `Start-ChmonosApp -Store <写し>` だけ。写しは `.claude/skills/ui-check/sandboxes.md`
- 終わったら `Test-ProductionUntouched` で本番が変わっていないことを確かめ、報告に書く
- アプリの一式（`AppServiceContainer`・`MainViewModel`）や `new App()` を使う試験・道具は、保存先を `CHMONOS_HOME` で指定する（`docs/spec/architecture.md`）

### 4. 友人のデータは第三者のもの

友人のライブラリの写し（`-friendtest`・`-eval` と、そこから作った写し）は、リポジトリに入れない・どこへも送らない。
コミットメッセージ・文書・ログ・試験の作り物のデータに商品名・ファイル名・商品IDを書かない（「対応アバター244体の商品」のように数と傾向で書く）。撮った画像も手元だけ。
サブエージェントが写しの画面から拾った名前を、報告から先へ写さない。

## 作りの決め事

詳しくは `docs/spec/architecture.md`・`data-model.md`。

- 画面からの書き込みと BOOTH への問い合わせは `UiCommand`（`CommandHandler` の switch）1本。新しい操作は `UiCommand` に足す。読むだけなら画面から直に呼んでよい。
  UiCommand を通さない例外は、起動時の裏の作業と取り込み画面の「足りない情報を取得」だけ（人の操作ではないので、入口の「人が押した」優先度を掛けない）
- item の書き込みは持ち主を宣言して、錠の中で今の値に当てる（`SaveLocalAsync`・`ChangeLocalAsync`）。`Local` を丸ごと書き戻さない。`Items.SaveAsync` を直接呼ぶのは、錠の中で新しい商品を作るときだけ。
  画面のスレッドから来得る保存・削除は非同期で書く（同期で `StoreWriteGate` を待つと固まる）
- JSON は人が読める形を保つ。計算で出せる値を JSON に書かない（`[JsonIgnore]`。書き出す事故が繰り返し起きている）。欠けた配列（`null`）は空として受ける
- 公開前は、古い形のデータに合わせる変更をしない。合わないデータの方を「どの保存先の、何件が、どう合っていないか」で報告し、直し方を聞く。本番は勝手に直さない
- 推定した値を勝手に入れない。検出・推定で出た値は候補として見せ、人が選んだときだけ入れる
- 外から使う API の守り（`docs/spec/local-api.md`。`127.0.0.1` だけ・トークン必須・中身をログに残さない・BOOTH へ問い合わせない・書き込みは `UiCommand`）を外さない

## 書き方

- コメントは「なぜ」を日本語で書く。数字には根拠も書く（例は `.claude/rules/code-comments.md`）
- PowerShell の台本や作り物のデータを書く前に `docs/dev/powershell.md` を読む。作り物の JSON を `ConvertTo-Json` で作らない（空の配列が `null` になる）

## 確かめ方

- `dotnet build`・`dotnet test`（6つの一式。出力の絞り方は `.claude/wrap-up.md`）。途中は関係する試験だけでよい（`--filter "FullyQualifiedName~ImportLadder"`）。**コミットの前と統合のときは全部。**
  重くなったときの目安と直し方は `docs/dev/app-tests.md`「重くしない」
- 通信する試験を既定の一式に入れない（BOOTH へ実際に問い合わせる確かめは `experiments/`）。時計や実マシンの状態（Unity が動いているか）に結果が左右される試験も書かない
- 計算で決まる物（文言・出し分け・一覧に何が並ぶか・押したら何が書かれるか）は試験（`docs/dev/app-tests.md`）で確かめ、起動して撮らない。画面で見るのは配置・色・折り返し・手触りだけ
- 画面は描画を画像で見る（UI Automation は位置を答えない）。手順は `ui-check` スキル。実入力（`mouse_event`）は UIA で届かない所だけで、使う前にユーザへ告げる
- 判断の前に測る。思い込みで切り捨てず、数字で決める（例は `perf-measure` スキル）

## 読み方（文脈を節約する）

`Grep` で場所を絞り（見出しは `^#{2,3} `、XAML は `x:Name`・部品の名前、C# は関数名）、`Read` の `offset`/`limit` で前後だけ読む。
特に大きい物：`docs/feedback/done-*.md`（探す時は索引 `done-index.md` を Grep）、`docs/history/` の `unity-handoff.md`・`ui-revision.md`・`avatars.md`、`docs/features.md`、`Views/` の大きな XAML、1,200行を超える ViewModel と `AvatarDetector.cs`。

## 進め方

- 設計を変えたら `docs/spec/*.md`（1ファイル200行まで）と `docs/features.md` も直す（表は `.claude/wrap-up.md`）。経緯は `docs/history/` に日付を付けて足し、history と `docs/history/author-memos/` は書き直さない
- 画面への意見は `docs/feedback/open.md` に記録するだけ。実装はユーザが番号や件名を指して頼んだ物だけ（`feedback-log` スキル）。「洗い出して」「まとめて」も報告までで止め、直すかを聞く
- 確かめ用のアプリは、作業を頼まれたら閉じてよい。ただし「メモ:」「報告:」で始まる発言の間と、見て決めてもらう質問の間は開いておく
- 画面を直してビルドが通ったら、その画面の写しで起動したままにする。「画面はまだ使わないで」と言われたら、閉じずに別のフォルダへビルド
- Unity を実際に動かす確かめは、ユーザが「試して」と言ったときだけ
- 並行で直すときは `parallel-fix` スキル。サブエージェントの重い指摘は、伝える前に自分でコードか画像を見て確かめる
- 仕上げは harness の `wrap-up` スキルと `.claude/wrap-up.md`（友人のデータの点検・push のしかたを含む）
