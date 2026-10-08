# コード署名（しない）

> **要点**：v1.0.0 はコード署名をせずに配る（ユーザ判断 2026-10-07。前に決めた SignPath Foundation の案はやめた）。
> 代わりに、**GitHub Actions で1回だけ組んだ zip を、GitHub と BOOTH の両方で配る**（`.github/workflows/release.yml`）。
> 署名の無い exe は、SmartScreen とスマート アプリ コントロールが exe ごとの評判で判定する。組み直すと別の exe になって評判が無くなるので、手元で組み直した物を配らない。
> 使う人への説明は README・`docs/booth/item-page.md`・`docs/booth/zip-README.txt` の「Windows の警告」の節。
>
> **経緯**：`docs/dev/release-handoff-2026-10-07.md` §2（選択肢と費用）。下の SignPath の調べは、後で署名を考え直すときのために残す。

## 署名しないと起きること

- **SmartScreen**：初回に「Windows によって PC が保護されました」。「詳細情報」→「実行」で起動できる。評判が貯まると出なくなる
- **スマート アプリ コントロール**：止められると「実行」の道が無い。使う人ができるのは、設定でオフにすることだけ。
  2026年4月の更新（KB5083769）から、オフにした後にオンへ戻せるようになった。それより前は Windows を入れ直さないと戻せない
  （[topedia](https://blog-en.topedia.com/2026/04/smart-app-control-in-windows-11-can-now-be-re-enabled-without-reinstalling/)・[Windows Latest](https://windowslatest.com/2025/12/16/microsoft-confirms-you-can-soon-disable-smart-app-control-without-reinstalling-windows-11)）
- 判定は exe ごとの評判。別の PC（オン）で、1回目の配布の形は通り、組み直した版は止められた（`docs/feedback/manual-check-2026-10-07.md`）。
  組み直さずに同じ exe を配り続けると評判が貯まる見込み（[Dev Genius](https://blog.devgenius.io/smart-app-control-may-blocked-your-app-the-fix-was-to-stop-rebuilding-it-5676f69ec3bc)。公式の文書ではない）

## 配る手順

1. `docs/booth/zip-README.txt` の【未定】を埋める（BOOTH の URL は、BOOTH の商品を非公開で作ると先に分かる）。`Directory.Build.props` の `Version` を確かめる
2. `git tag v1.0.0` → `git push origin v1.0.0`。workflow が組み、Releases に**下書き**として zip と SHA256 を置く（tag と版が違う・【未定】が残る・zip の中身が足りないときは止まる）
3. 下書きの zip を落として起動を確かめ、Releases で公開する
4. **同じ zip** を BOOTH の2つのバリエーションに付ける

直す版を出すときも、手元で組まずに tag から組む。

**マニュアルはバージョンごとに読めるようにする**（2026-10-08・ユーザ判断「この方向で進めてくれ」）。
- master のマニュアルは「公開している最新＋開発中の次のバージョン」。まだ配っていない動きには「（次のバージョンから）」と書く
- 出すとき（tag を打つ前）：「（次のバージョンから）」を「（v1.2.0 から）」のように書き換え、`manual/README.md` の版の表に1行足す
- workflow は tag で組むと、同じコミットに印 `manual-v<版>` を付ける。Releases の本文と zip の README（`{version}` を埋める）はここを指す。BOOTH の商品ページと GitHub の README は master のまま
- 公開した後に、その版の動きのままマニュアルの言い方だけを直したときは、印を動かす：`git tag -f manual-v1.1.0 <直したコミット>` → `git push -f origin manual-v1.1.0`（まだ配っていない動きを含むコミットへは動かさない）
- v1.1.0 は、公開した後の言い方の直し（`f9b4e61d`）を含め、まだ配っていない動き（`a9bf6c47` の引越しの件）を含まない `f9b4e61d` に `manual-v1.1.0` を付けた。v1.0.0 は tag `v1.0.0` のまま

**同じ tag で組み直さない**（2026-10-08・点検24）。workflow は、その tag の Releases が既にあれば組む前に止まる。配り直すときは Releases にある zip を使う。
SDK の版は `global.json` で固め、workflow もそれを入れて組む（`10.0.x` のままだと、その日の最新の SDK で組まれ、同じソースでも別の exe になった）。
手元の SDK が上がっても組めるよう、手元では新しい機能帯へ進むのを許している（`rollForward: latestFeature`）。版を上げるときは `global.json` を書き換える。

**`THIRD-PARTY-NOTICES.txt` に、同梱する .NET ランタイムの許諾文と第三者の表示を入れる**（2026-10-08。exe は自己完結でランタイムと WPF を含む。v1.0.0・v1.1.0 で抜けていた）。
SDK の版を上げると同梱するランタイムの版も変わるので、その版の `LICENSE.TXT`・`THIRD-PARTY-NOTICES.TXT`（NuGet の `microsoft.netcore.app.runtime.win-x64/<版>`）と、
Windows Desktop の `LICENSE`、WPF・Windows Forms の第三者の表示（各リポジトリの tag）に差し替える。workflow は、告知に同梱する版が書いてなければ止まる。

## 後で署名を考え直すとき：SignPath Foundation の調べ（2026-10-07）

担当（サブエージェント）が公式の文書で調べた。要約を通した所があり、**申し込む前に原文で確かめる**。

### 申し込みの条件

出典：[signpath.org/terms](https://signpath.org/terms)・[signpath.org](https://signpath.org/)

- OSI が認めたライセンスで、商用のデュアルライセンスが無い。独自の非 OSS の部品が無い（同梱の辞書は CC BY-SA 4.0。点検しておく）
- マルウェア・PUP（望まれないかもしれないソフト）を含まない
- 署名したい形で**公開済みのリリース**があり、活発に保守されている。機能がダウンロードページで説明されている
- スター数・公開からの期間の下限は、見た範囲では書かれていない（未確認）
- 個人で通るかは公式では未確認。個人の体験記では、単独で保守する人の申し込みが審査待ちだった（[Zenn](https://zenn.dev/shm_7ec/articles/signpath-oss-code-signing)）。審査の日数は未確認
- 申し込みのフォーム（signpath.org/apply）は担当の道具で開けなかった。ブラウザで項目を確かめる

### プロジェクトの側に要る物

- **コード署名の方針（code signing policy）の節**をホームページ（README など）に置く。SignPath.io と SignPath Foundation の名前、役割（コミッター・レビュアー・承認者）と担当者、プライバシーポリシーを載せる。1人で全部を兼ねてよいかは未確認
- 全員が SignPath とリポジトリの両方で多要素認証を使う
- 署名する実行ファイルのメタデータ（製品名・版）を設定し、ビルドごとにそろえる。今は csproj に版が無い（`Version` を足す）
- 利用者のデータを集めるならプライバシーポリシーに書く。Chmonos は集めないが、**人が「自動検索」を押したときにファイル名から作った語を BOOTH の検索に送る**ことと、通信の先（BOOTH・YouTube・新しい版の確認の GitHub）を書いておく
- 説明なしにシステムの設定を変えない。消し方を示す（README の「データの置き場と消し方」）

### 組み方

出典：[docs.signpath.io/trusted-build-systems/github](https://docs.signpath.io/trusted-build-systems/github)・[signpath/github-action-submit-signing-request](https://github.com/SignPath/github-action-submit-signing-request)

- 公開リポジトリから自動で組んだ物だけを署名する。署名を頼むまでのジョブは、GitHub が用意する実行環境で動かす
- 成果物は `actions/upload-artifact` で上げ（zip で渡る）、zip の中の exe を署名する設定を SignPath 側で作る
- action は `signpath/github-action-submit-signing-request@v3`（古い `SignPath/github-actions` は 2026-02-12 にアーカイブ）。SignPath 側で GitHub.com を信頼するビルドの仕組みとして入れ、GitHub App を入れ、API トークンを作る
- 単一ファイルの exe（`PublishSingleFile`）で問題があるかは未確認

### 署名した後の Windows

出典：[signpath.io/knowledge-base/windows-platform](https://signpath.io/knowledge-base/windows-platform)

- SmartScreen の評判は、出回って悪用の報告が無いことで貯まる。署名した直後は警告が出うる。証明書が替わると評判は引き継がれない
- スマート アプリ コントロールは SignPath の文書に記述が無い。署名した版を、スマート アプリ コントロールの入った別の PC で起動して確かめる（handoff §7）

