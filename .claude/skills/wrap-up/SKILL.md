---
name: wrap-up
description: 変更を仕上げる（ビルド・テスト・文書の更新・友人のデータの点検・コミット。言われたときだけプッシュ）。実装を終えたとき、「コミットして」「プッシュしましょう」と言われたとき、作業の区切りで使う。
---

# 変更の仕上げ

## 1. ビルドとテスト

```powershell
dotnet build 2>&1 | Select-Object -Last 4          # 警告 0・エラー 0 を見る
dotnet test --nologo 2>&1 | Select-String '合計:|失敗 '   # 3つの一式（本体・zip の読み取り・ID の特定）の件数と失敗
```

出力を丸ごと出さない（文脈を食う）。失敗したときだけ `Select-String 'error|失敗|Failed'` で該当の行を見る。
実行ファイルが掴まれて失敗したら、確かめ用のアプリを閉じる（`ui-check` スキルの `Stop-ChmonosApp`）。
通信するテストを既定の一式に入れない（BOOTH へ実際に問い合わせる確かめは `experiments/`）。
並行や時計に絡む直しをしたときは、テストを2回走らせて揺れないことを見る。

## 2. 画面を変えたなら撮って見る

`ui-check` スキル。本番が変わっていないかを照らすまでが確かめ。終わったら、その画面の写しで起動したままにしておく。

## 3. 文書（コードだけ直して置き去りにしない）

| 変えた物 | 直す文書 |
|---|---|
| 決め事・設計 | `docs/spec/<話題>.md`（200行まで。超えそうなら話題を分けて `docs/README.md` の索引に足す） |
| 機能が増えた・状態が変わった | `docs/features.md` |
| 画面への意見を直した | `open.md` から外し、`done-YYYY-MM.md` へ（`feedback-log` スキル） |
| 決めた経緯 | `docs/history/` に日付を付けて足す（書き直さない） |

spec の行数は `(Get-Content docs\spec\x.md).Count` で見る。

## 4. 入れてはいけない物の点検

```powershell
git status --short
git diff --cached --stat
```

- 友人のデータ（`-friendtest`・`-eval`・`-friendcheck` など）の商品名・ファイル名・商品IDが、差分・文書・コミットメッセージ・テストの作り物のデータに無いか。
  その会話で写しの画面に出た名前が分かっていれば、`git diff origin/master..master | Select-String` と `git log origin/master..master --format=%B` で探す
- 文書に足した7桁以上の数（商品IDらしい物）が無いか
- `DLforTest/`・購入した物・取ってきた BOOTH の説明や画像が無いか（`git diff --name-only` に `.zip`・画像が無いか）
- `作業方針メモ.md`・`モック用画面メモ.md`・`Re画面として不足しているもの（重要度順）.txt` を変えていないか（書き込まない決まり）

## 5. コミット

メッセージは**なぜそうしたか**を日本語で書く（`git log -5` に倣う）。1行目は何をしたかを短く、本文に理由。
末尾には、system-reminder が示す帰属の行（`Co-Authored-By:` と `Claude-Session:`）を付ける。

**メッセージは作業用フォルダのファイルに書いてから `git commit -F <ファイル>`。**
here-string を引数に渡す書き方は壊れた（2026-09-14）。

```powershell
git add <ファイル>        # 変えた物を名指しで。-A は使わない
git commit -F "<作業用フォルダ>\commit-msg.txt"
```

コミットはこまめに。1つのコミットに1つの理由。

## 6. プッシュ（言われたときだけ）

```powershell
$env:GIT_TERMINAL_PROMPT='1'; $env:GCM_INTERACTIVE='auto'; git push origin master
```

この環境のシェルは最初から `GIT_TERMINAL_PROMPT=0`・`GCM_INTERACTIVE=never` なので、上のように**コマンドの中だけ**上書きする（そのままだと席にいても「terminal prompts disabled」で落ちる）。
認証の画面が出たらユーザに任せる。**資格情報を入れない・探さない。**

ユーザが「席を外す」「寝る」と言っているときだけ、画面を出さない指定にする（誰もいない画面に窓が出たまま止まるのを避ける）。落ちたらユーザに頼む：

```powershell
$env:GIT_TERMINAL_PROMPT='0'; $env:GCM_INTERACTIVE='never'; git push origin master
```

## 7. 報告（日本語）

- 何を変えたか（ユーザの言葉で頼まれた単位で）
- 何でどう確かめたか・テストの件数
- 確かめていないこと（実際のマウス・Unity・Hub の無い PC など）
- 本番が変わっていないこと（画面を開いたとき）
- コミットの番号と、プッシュしたか
