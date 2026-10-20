# Chmonos の仕上げ（harness の wrap-up スキルが読む）

## ビルドとテスト

```powershell
dotnet build 2>&1 | Select-Object -Last 4          # 警告 0・エラー 0 を見る
dotnet test --nologo; if ($LASTEXITCODE) { throw "テスト失敗" }   # 4つの一式（Core・App・zip の読み取り・ID の特定）
```

- 出力を丸ごと出さない。件数は `Select-String '合計:|失敗 '`、失敗したときだけ `Select-String 'error|失敗|Failed'` で該当の行を見る
- 実行ファイルが掴まれて失敗したら、確かめ用のアプリを閉じる（`ui-check` スキルの `Stop-ChmonosApp`）
- 通信するテストを既定の一式に入れない（BOOTH へ実際に問い合わせる確かめは `experiments/`）
- 並行や時計に絡む直しをしたときは、テストを2回走らせて揺れないことを見る

## 画面を変えたなら撮って見る

`ui-check` スキル。本番が変わっていないかを照らすまでが確かめ。終わったら、その画面の写しで起動したままにしておく。

## 直す文書

| 変えた物 | 直す文書 |
|---|---|
| 決め事・設計 | `docs/spec/<話題>.md`（200行まで。超えそうなら話題を分けて `docs/README.md` の索引に足す） |
| 機能が増えた・状態が変わった | `docs/features.md` |
| 画面への意見を直した | `open.md` から外し、`done-YYYY-MM.md` へ（`feedback-log` スキル） |
| 決めた経緯 | `docs/history/` に日付を付けて足す（書き直さない） |

spec の行数は `(Get-Content docs\spec\x.md).Count` で見る。

## 点検（プロジェクトだけの物）

- 友人のデータ（`-friendtest`・`-eval`・`-friendcheck` など）の商品名・ファイル名・商品IDが、差分・文書・コミットメッセージ・テストの作り物のデータに無いか。
  `pwsh -NoProfile -File .claude/check-friend-ids.ps1 -Base origin/master -Head master -Messages 3`（ID は出さず、件数と場所だけを出す）。
  その会話で写しの画面に出た名前が分かっていれば、`git diff origin/master..master | Select-String` と `git log origin/master..master --format=%B` でも探す
- 文書に足した7桁以上の数（商品IDらしい物）が無いか
- `DLforTest/`・購入した物・取ってきた BOOTH の説明や画像が無いか（`git diff --name-only` に `.zip`・画像が無いか）
- `docs/history/` の既存の文書（作者の出発点のメモ `author-memos/` を含む）を書き直していないか（足すのはよい）

## push（頼まれたときだけ）

```powershell
$env:GIT_TERMINAL_PROMPT='1'; $env:GCM_INTERACTIVE='auto'; git push origin master
```

この環境のシェルは最初から `GIT_TERMINAL_PROMPT=0`・`GCM_INTERACTIVE=never` なので、上のように**コマンドの中だけ**上書きする（そのままだと席にいても「terminal prompts disabled」で落ちる）。
認証の画面が出たらユーザに任せる。**資格情報を入れない・探さない。**
ユーザが「席を外す」「寝る」と言っているときだけ、画面を出さない指定（`'0'`・`'never'`）にする。落ちたらユーザに頼む。

## 報告に足す事

- 本番が変わっていないこと（画面を開いたとき）
- 確かめていない事の例：実際のマウス・Unity・Hub の無い PC
