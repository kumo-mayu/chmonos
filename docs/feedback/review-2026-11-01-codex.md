# Codex の点検（読むだけ）2026-11-01

v1.2.0 を公開してよいかの材料（ユーザ依頼）。直すかはユーザが決める。

- 指示書：`docs/dev/external-audit.md` と harness の共通の雛形（read）に、v1.2.0 で初めて配る物を「特に見てほしい項目」として足した
- コマンド：`codex exec -s read-only -C <リポジトリ> -o <作業用>/codex-review.md -`
- codex-cli 0.160.1・gpt-6.1-sol。点検したコミット `4faf4397`

## まとめ役の確かめ

- リポジトリは変わっていない（`git status` 0件）
- 指摘4件とも、当たるコードを読んで指摘どおりと確かめた（`SelectableText` は用途によらず URL をリンクにする。`RefreshOpenProjectsAsync` は結果を無条件に入れる。`FindImages` は全商品の `ImagesOf(...).ToList()` の後でしか取り消しを見ない。`ScanProjectAsync` は `NullReferenceException` を受けない）
- 第三者のデータ・購入物に由来する名前は報告に無い

まとめ役が別に見つけた物（Codex の指摘にはない）：

- `README.md`・`docs/booth/item-page.md`・`docs/booth/zip-README.txt` に API の記載が無い。「通信とアカウント」に、既定はオフで PC の中でだけ待ち受けることが書かれていない
- `NOTICE` の「使っているライブラリ」に `Microsoft.AspNetCore.OpenApi`・`Microsoft.OpenApi` が無い（`THIRD-PARTY-NOTICES.txt` にはある）
- `README.md` の機能の説明に、プロジェクトの中身で絞る条件が無い

まとめ役の測定（2026-11-01。写しの写し・`cleanTest - コピー`・Release）：調べる処理は 206 件で 70 ms、782 件で初回 4.7 秒・2回目 0.6 秒、20,000 件で 9.4 秒・6.6 秒（割り当て約 1.9GB・裏のスレッド）。

## 対応

| 指摘 | 対応（ユーザが決める） |
|---|---|
| 名前の中の URL が外部リンクになる | |
| 開いている Unity の古い確認結果が新しい結果を上書きする | |
| 取り消した画像の調べが読み続ける | |
| 画像の控えの null 行で調べが終わらない | |
| README・BOOTH の原稿・zip の README に API の記載が無い（まとめ役） | |
| NOTICE のライブラリの抜け（まとめ役） | |

以下、Codex の報告そのまま。
v1.1.0 からの差分を中心に、コードを読むだけで点検しました。**新規の指摘は「中」4件です。今回確認した範囲では、公開を止めるべき「致命」「重い」の問題は確認できませんでした。**ただし、ビルド・試験・実機での再現確認は行っていません。

ファイルの変更・作成・削除、外部通信、アプリ起動は行っていません。禁止されたデータ・購入物にも触れていません。

## 重い順の要約

| 指摘 | 重大度 | 起こりやすさ | 公開を止めるべきか |
|---|---|---|---|
| 商品名などに含まれた URL が、意図しない外部リンクになる | 中 | 条件が重なると当たる | 公開後でよい |
| 開いている Unity の古い確認結果が、新しい結果を上書きする | 中 | 条件が重なると当たる | 公開後でよい |
| 「読み直す」で取り消しても、大きな画像一覧の読み取りが続く | 中 | 条件が重なると当たる | 公開後でよい |
| 画像の控えに null の行があると、「調べています」のままになる | 中 | 狙わないと当たらない | 公開後でよい |

## 特に見てほしい項目

### [中] 商品名・ショップ名の URL が外部リンクとして動く

- 場所：[SelectableText.cs:124](/D:/work/ClaudeCode/chmonos/Chmonos.App/Controls/SelectableText.cs:124)、同ファイル:249・278・309、[SelectableLink.cs:74](/D:/work/ClaudeCode/chmonos/Chmonos.App/Controls/SelectableLink.cs:74)、[ItemView.xaml:546](/D:/work/ClaudeCode/chmonos/Chmonos.App/Views/ItemView.xaml:546)
- 条件：商品名やショップ名に `https://example.invalid/` のような URL が含まれている状態で、その部分を押す。手で付けた名前でも成立する。
- 起きること：名前を選択したり、アプリ内のショップを開いたりする部品の中に、ブラウザを開く別の動作が混ざる。
- 根拠：今回置き換えた名前の表示も `SelectableText.Text` を使う。`Refresh` → `Build` → `AppendLine` は表示の用途を区別せず URL を `Hyperlink` に変え、`RequestNavigate` → `OpenInBrowser` → `Shell.OpenUrl` へ進む。`SelectableLink` 自体の命令とは独立した外部リンクになる。商品ページのショップ名には「外部BOOTHには飛ばさない」という意図も明記されている。
- 確かさ：確か。外部リンクが作られる経路を確認した。マウス操作時に親の命令も重ねて実行されるかは未確認のため、二重実行までは断定しない。
- 試験：`ItemCopyPathTests` は `IsClick` の判定を試しているが、名前に URL を含めた場合は試験の穴。`ExternalLinkGuardTests` の http/https 制限では、この用途の取り違えは防げない。
- 直し方の方向：本文の URL をリンクにする表示と、名前をそのまま選べる表示を分ける。名前用の `SelectableLink` の中には外部リンクを作らない。
- 起こりやすさ：条件が重なると当たる
- 公開を止めるべきか：公開後でよい

### [中] 開いている Unity の確認結果が、完了した順に上書きされる

- 場所：[SearchViewModel.UnityContents.cs:107](/D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:107)、同ファイル:116・139・283・304
- 条件：Unity の開閉と、Chmonos への切り替えや「読み直す」が重なる。先に始めた確認が遅れ、後から始めた確認より後に完了する。
- 起きること：「今開いているプロジェクト」が古い一覧へ戻り、閉じたプロジェクトの商品で絞ったり、現在開いているプロジェクトの商品を外したりする。次の確認まで食い違いが残る。
- 根拠：複数の入口が `RefreshOpenProjectsAsync().Forget()` を呼ぶ。同メソッドは毎回 `Task.Run` で調べ、戻った結果を無条件に `_openProjects`・`_unknownOpenProjects` へ代入する。確認の世代番号・直列化・古い要求の破棄がない。最後に `ApplyFilters` がその一覧を使う。
- 確かさ：確か。完了順が逆転した場合に古い結果を採用するコード経路を確認した。実機での発生頻度は未確認。
- 試験：`SearchUnityContentsTests.Unityを開いて窓に戻ると_今開いているプロジェクトで絞り直す` は1回の更新を確認している。2回の確認を逆順に完了させる場合は試験の穴。
- 直し方の方向：開始時に世代番号を付け、最新の確認結果だけを採用する。候補一覧を読む処理についても、読み直し前の結果を後から採用しないよう揃える。
- 起こりやすさ：条件が重なると当たる
- 公開を止めるべきか：公開後でよい

### [中] 取り消した画像調査が、大きなフォルダを読み続ける

- 場所：[UnityProjectItemScan.cs:69](/D:/work/ClaudeCode/chmonos/Chmonos.App/Services/UnityProjectItemScan.cs:69)、同ファイル:74・90、[UnityProjectImages.cs:55](/D:/work/ClaudeCode/chmonos/Chmonos.Core/Services/UnityProjectImages.cs:55)、同ファイル:99・214、[SearchViewModel.UnityContents.cs:268](/D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:268)
- 条件：画像が多い登録フォルダや遅いドライブを調べている間に「読み直す」を押す。
- 起きること：古い調査を取り消した後も、その調査がフォルダや zip の控えを読み続ける。新しい調査も始まるため、連打すると重い読み取りが重なる。UI スレッドの停止までは確認していない。
- 根拠：`ReloadProjectContents` は取消を通知して結果の辞書を消すが、古い処理の終了を待たずに再調査する。一方、`FindImages` は全商品の `ImagesOf(...).ToList()` が終わってから取消を確認する。`ImagesInFolder`・`ListProject` の再帰列挙にはトークンがなく、単体画像の `ShaOf` もファイル全体の同期ハッシュを途中で取り消せない。
- 確かさ：確か。取消が届かない処理区間を確認した。
- 試験：`SearchUnityContentsTests.調べている間は進み具合を出し_結果は覚え_読み直すで取り直す` は、最初の調査が終わった後の再調査を試している。調査中の取消と連続再調査は試験の穴。
- 直し方の方向：商品・フォルダ・ファイルを列挙する途中にも取消を確認する。ハッシュにも取消を渡し、同じプロジェクトの重い調査が重ならないようにする。
- 起こりやすさ：条件が重なると当たる
- 公開を止めるべきか：公開後でよい

### [中] 画像の控えの null 行で、調査が完了しなくなる

- 場所：[ZipImageStore.cs:49](/D:/work/ClaudeCode/chmonos/Chmonos.Core/Storage/ZipImageStore.cs:49)、[UnityProjectImages.cs:84](/D:/work/ClaudeCode/chmonos/Chmonos.Core/Services/UnityProjectImages.cs:84)、[SearchViewModel.UnityContents.cs:208](/D:/work/ClaudeCode/chmonos/Chmonos.App/ViewModels/SearchViewModel.UnityContents.cs:208)、同ファイル:220・225、[FireAndForget.cs:29](/D:/work/ClaudeCode/chmonos/Chmonos.App/FireAndForget.cs:29)
- 条件：`zipimages/<ハッシュ>.json` を手で直し、`images` に null の行を入れる。例は `{"images":[null]}`。その商品を含むプロジェクト調査を始める。
- 起きること：調査結果が出ず、「調べています」が残る。「読み直す」でも同じ控えを読むため繰り返す。控えを修正・削除すれば回復できる。
- 根拠：JSON の読み取りは成功し、`Load` が null 行を含む一覧を返す。`ImagesOf` の `row.Path` で `NullReferenceException` になる。`ScanProjectAsync` の catch はこの例外を受けず、`IsScanning = false` に到達しない。`Forget` はログへ残すだけなので、画面に失敗理由が出ない。
- 確かさ：確か。`EmptyForNull` が補うのは配列そのものの null であり、配列内の null 行は除かれないことも確認した。
- 試験：`UnityProjectImagesTests` は控えの保存・再利用・zip 消失を確認しているが、不正な控えの行と調査状態の後始末は試験の穴。
- 直し方の方向：控えを読む境界で行を検証する。調査状態の終了は例外時にも保証し、読めなかった理由を表示する。
- 起こりやすさ：狙わないと当たらない
- 公開を止めるべきか：公開後でよい

## 自由に探した所

追加の新規指摘はありません。

ImageSharp の復号箇所を検索し、`ImagePipeline`・`ImageConversion`・`ThumbnailLoader` は `ImageLimits` を通ることを確認しました。今回追加された Unity の画像照合は、画像を復号せず、サイズ・CRC・SHA-256 を使っています。据え置き判断を崩す迂回経路は確認できませんでした。

API の認証・Host・CORS・ログの入口、書き込みの `UiCommand` 経路、既定オフ、headless・二重起動の入口も確認しました。配布については、必要ファイルとランタイム告知を workflow が検査する構成を確認しています。これは配布物そのものの検証結果ではありません。

旧版が新しい検索条件を無視する点と、省略可能な追加欄を旧版が保存すると失う点は、仕様の互換方針に沿うため新規指摘にしていません。

## 公開してよいかの見立て

**今回の静的点検だけを理由に、公開を止める必要はないと判断します。**確認した4件は、回復可能な表示・操作・取消の問題です。ただし、試験を実行していないため、v1.2.0 の合格判定までは出せません。

## 確かめていない範囲

- ビルド・既定の試験一式、新しい試験の作成。
- Unity・WPF の実操作、実際のクリック順序、Tab・読み上げの挙動。
- Release workflow の実行、完成した zip、同梱バイナリと許諾文の実物照合。
- v1.1.0 → v1.2.0 → v1.1.0 の実データ往復、電源断・容量不足の実験。
- API・Query を含む差分全体の全分岐。入口と関連経路を重点的に読み、全行の網羅はしていません。
- 禁止された保存先・第三者データ・購入物は、一覧取得も含めて確認していません。