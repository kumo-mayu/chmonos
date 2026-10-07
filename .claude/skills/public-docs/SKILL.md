---
name: public-docs
description: Chmonos を使う人に向けた文書（公開用の README・BOOTH の商品ページの原稿・配布 zip の README.txt・使う人向けの更新履歴）を書く・直す・点検する。公開の準備で説明文を書くとき、「自然な日本語に」「AIっぽい」と言われたとき、BOOTH のページを直すときに使う。画面の文言は ui-wording、spec などの開発の文書はこのスキルの対象外。
---

# 使う人に向けた文書を書く

読む人は技術に詳しくない見込みが高い（BOOTH で買ったアセットを整理したい人）。開発の文書（`CLAUDE.md`・`docs/`）の言葉づかいは持ち込まない。

外から取ってきた3つのスキルを、役目を分けて使う。どれも自分からは読み込まれない設定にしてあるので、使うときに `Read` で開く。

| スキル | 受け持ち | 開く物 |
|---|---|---|
| `user-guide-writing`（英語） | 文書の型。前提を先に・手順ごとに「できたかの確かめ」・困ったときは手順のそば | `SKILL.md` の Step 1・6 と `references/mode-structures.md` |
| `natural-japanese` | 文体と機械の点検 | `SKILL.md`、型は `references/doctypes/guide.md`（README）。決まりは `references/writing-constitution.md` |
| `humanizer-ja` | 書き終えたあとの点検表 | `SKILL.md` の1〜16・18・19 |

言葉は外のスキルに任せない。**画面に出ている語をそのまま使う**（読む人は画面の文字を頼りに操作する）。

**地の文の語彙は、文書ごとに分ける**（ユーザ指示 2026-10-07）。
- GitHub の README：**一般的な語彙**を使う（ウィンドウ・ドラッグ＆ドロップ・取得・保存先・削除・有効／無効・バックグラウンド・Project ウィンドウなど）。
  やさしく言い換えすぎると、かえって分かりにくい（「ふつうの窓」「落とす」「置き場」「外のアプリ」は使わない）
- BOOTH の原稿・zip の README：一般的な語彙を基本に、専門の語（ハッシュ・Zone.Identifier・API など）は避けるか一言で説明する
- 事実を簡単にしすぎない（例：BOOTH に API はある。言えるのは「商品の JSON は公式に文書化された API ではない」まで）

## 手順

### 1. 読む人と型を決める

`user-guide-writing` の型から1つ選ぶ。はじめの手順（getting-started）・1つの作業のやり方（how-to）・よくある質問（faq）。1つの文書に全部を詰めない。

### 2. 事実を集める

- できること・できないこと：`docs/features.md`（状態が「実装済み」の物だけ書く）と `docs/spec/`
- 画面の語：`docs/spec/ui-terms.md`。ボタンの名前は実際の文言を `node tools/wording.mjs` か XAML の Grep で確かめる
- 公開の決まり：`docs/dev/release-handoff-2026-10-07.md`

### 3. 書く

`natural-japanese` の文体の決まり（クイックのモード）で書く。書いてから `humanizer-ja` の点検表で見直す。

### 4. 機械で点検する

```bash
uv run --no-project .claude/skills/natural-japanese/scripts/lint.py --genre business <ファイル>
```

- **BOOTH の原稿は、囲み（```）を外した写しを scratchpad に作ってから当てる。**囲みの中は読み飛ばされ、0件になる（2026-10-07 に Paramroom の原稿で確かめた。囲みありは0件、外すと23件）
- 「Q.」「A.」・URL の行頭の繰り返しは無視してよい
- 指摘は「見る所」で、全部を直す指示ではない。読んで引っかからない文はいじらない
- `uv` が PATH に無いときは `%LOCALAPPDATA%\Microsoft\WinGet\Packages\astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe\uv.exe`

### 5. 事実を落としていないか照らす

直す前後で、数字・条件・できないことが消えていないかを1つずつ照らす。

## 使わない決まり

外のスキルには、この用途に合わない決まりがある。

- **体験・意見・動機を作らない。**`humanizer-ja` の「声を入れる」「17. 体温のない結論」、`natural-japanese` の採点の「人間味・体温」は使わない。作った理由などの作者の声が要るときは、ユーザに聞く。`docs/history/author-memos/` から引くのは、ユーザが良いと言ったときだけ
- **くだけすぎない。**「思ってます」のような話し言葉の結びにしない。です・ます体で書く
- `natural-japanese` の `semantic.py`（約1GBを取ってくる）とフルのモードの採点の繰り返しは、頼まれたときだけ

## 書かなければならないこと

- 非公式であること：「非公式のアプリです。ピクシブ株式会社（BOOTH・pixiv の運営）が作成・配布しているものではありません」と、「BOOTH」「pixiv」はピクシブ株式会社の登録商標であること（`docs/research/booth-terms.md` §3）
- データの置き場と消し方：既定の置き場は `%LOCALAPPDATA%\Chmonos`。exe を消してもデータは残るので、消すときはこのフォルダも消す。C ドライブが小さいときは、初めて起動したときの窓か、設定の「データ」の「場所を変える」で移せる
- 配った形での起動の仕方：exe と `assets` フォルダを一緒に置く（exe だけでは動かない）。SmartScreen とスマート アプリ コントロールの警告への対処
- BOOTH へは1本ずつ、間を空けて問い合わせること（取り込みに時間がかかる理由として、使う人の言葉で書く）

## 書いてはいけないこと

- 友人のデータの写しにある商品名・ファイル名・商品ID・ショップ名。例は作り物の名前で書く
- 購入した実アセット（`DLforTest/` など）の名前
- 画面の画像は、作り物のデータの写し（`ui`・`small` など。`.claude/skills/ui-check/sandboxes.md`）で撮る。BOOTH の実際の商品の名前やサムネイルが写った画面は使わない（商品情報の権利は作者にある。BOOTH 個別規約第13条）

## BOOTH の商品ページ

Paramroom を出したときの原稿と決まりが `D:\work\ClaudeCode\avatar-image-pad\docs\booth\`（`item-page.md`・`images.md`・`zip-README.txt`）にある。型はそれに倣う。

- BOOTH では Markdown が効かない。本文は素の文字で書く。`## ` 1つが BOOTH の「見出し付きセクション」1つになる
- 外部サイトへ案内するだけの商品は、BOOTH の規約で禁止されている。zip を BOOTH にも置く
- 値段は Paramroom と同じ。無料版（¥0）と支援版（中身は同じ、¥500）の2つのバリエーションに、同じ zip を付ける（ユーザ判断 2026-10-07）
- 「購入前の注意」に、できないことを先に書く
- 登録と差し替えはユーザが自分で行う。こちらは原稿と zip を用意するまで
