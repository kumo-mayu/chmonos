# コード署名（SignPath Foundation）

> **要点**：署名は SignPath Foundation（オープンソース向け・無料）で行う（ユーザ判断 2026-10-07）。
> 申し込むには、GitHub で公開したリポジトリと**公開済みのリリース**が要る。組むのは GitHub Actions の GitHub が用意する実行環境で、手元で組んだ物は署名できない見込み。
> 署名の発行元は **SignPath Foundation** と出る（作者の名前は出ない）。署名しても、評判が貯まるまで SmartScreen の警告は出うる。スマート アプリ コントロールへの効き目は未確認。
>
> **経緯**：`docs/dev/release-handoff-2026-10-07.md` §2（選択肢と費用。そのときは BOOTH が先の前提で、公開リポジトリが条件の SignPath は外れていた）。公開を GitHub、BOOTH の順に変えたので候補に戻り、決めた。

2026-10-07 に担当（サブエージェント）が公式の文書で調べた。要約を通した所があり、**申し込む前に原文で確かめる**。

## 申し込みの条件

出典：[signpath.org/terms](https://signpath.org/terms)・[signpath.org](https://signpath.org/)

- OSI が認めたライセンスで、商用のデュアルライセンスが無い。独自の非 OSS の部品が無い（同梱の辞書は CC BY-SA 4.0。点検しておく）
- マルウェア・PUP（望まれないかもしれないソフト）を含まない
- 署名したい形で**公開済みのリリース**があり、活発に保守されている。機能がダウンロードページで説明されている
- スター数・公開からの期間の下限は、見た範囲では書かれていない（未確認）
- 個人で通るかは公式では未確認。個人の体験記では、単独で保守する人の申し込みが審査待ちだった（[Zenn](https://zenn.dev/shm_7ec/articles/signpath-oss-code-signing)）。審査の日数は未確認
- 申し込みのフォーム（signpath.org/apply）は担当の道具で開けなかった。ブラウザで項目を確かめる

## プロジェクトの側に要る物

- **コード署名の方針（code signing policy）の節**をホームページ（README など）に置く。SignPath.io と SignPath Foundation の名前、役割（コミッター・レビュアー・承認者）と担当者、プライバシーポリシーを載せる。1人で全部を兼ねてよいかは未確認
- 全員が SignPath とリポジトリの両方で多要素認証を使う
- 署名する実行ファイルのメタデータ（製品名・版）を設定し、ビルドごとにそろえる。今は csproj に版が無い（`Version` を足す）
- 利用者のデータを集めるならプライバシーポリシーに書く。Chmonos は集めないが、**人が「自動検索」を押したときにファイル名から作った語を BOOTH の検索に送る**ことと、通信の先（BOOTH・YouTube）を書いておく
- 説明なしにシステムの設定を変えない。消し方を示す（README の「データの置き場と消し方」）

## 組み方

出典：[docs.signpath.io/trusted-build-systems/github](https://docs.signpath.io/trusted-build-systems/github)・[signpath/github-action-submit-signing-request](https://github.com/SignPath/github-action-submit-signing-request)

- 公開リポジトリから自動で組んだ物だけを署名する。署名を頼むまでのジョブは、GitHub が用意する実行環境で動かす
- 成果物は `actions/upload-artifact` で上げ（zip で渡る）、zip の中の exe を署名する設定を SignPath 側で作る
- action は `signpath/github-action-submit-signing-request@v3`（古い `SignPath/github-actions` は 2026-02-12 にアーカイブ）。SignPath 側で GitHub.com を信頼するビルドの仕組みとして入れ、GitHub App を入れ、API トークンを作る
- 単一ファイルの exe（`PublishSingleFile`）で問題があるかは未確認

## 署名した後の Windows

出典：[signpath.io/knowledge-base/windows-platform](https://signpath.io/knowledge-base/windows-platform)

- SmartScreen の評判は、出回って悪用の報告が無いことで貯まる。署名した直後は警告が出うる。証明書が替わると評判は引き継がれない
- スマート アプリ コントロールは SignPath の文書に記述が無い。署名した版を、スマート アプリ コントロールの入った別の PC で起動して確かめる（handoff §7）

## 進め方の案

1. 履歴に出してはいけない物が無いかを点検して、リポジトリを公開する
2. GitHub Actions で配る形（exe ＋ `assets`）を組む流れを作る。csproj に版を入れる
3. 署名なしの最初のリリースを GitHub に出す（申し込みの条件）。README にコード署名の方針の節とプライバシーポリシーを足す
4. SignPath に申し込む。通ったら、署名した版を GitHub と BOOTH に出す
