# experiments/

`BoothZipInspector` 本体の方針（外部NuGet原則不使用・BOOTHへ通信しない）とは切り離した、
「ZIPファイルからBOOTH商品IDを特定する」ための検証用ツール群です。
本体には含めず、手法の有効性を実測するために使います。詳細な調査結果は
[docs/research/id-resolution.md](../docs/research/id-resolution.md) を参照してください。

いずれも **ZIPをディスクへ展開しません**（メモリ上で読み取り）。

| プロジェクト | 目的 | 外部依存 |
|---|---|---|
| `UnityPackageProbe` | `.unitypackage`(tar.gz) 内のアセットパスを読み、`Assets/<作者>/<商品>` の名前空間を集計する。作者名がショップsubdomainと一致することが多く、cookie不要で最も強いシグナル | なし（.NET標準 `System.Formats.Tar`） |
| `ThumbnailHashProbe` | ZIP内サムネイル(`main.png` 等)と商品JSONの画像を dHash で照合する。一致=距離0〜2 / 不一致=21以上 | SixLabors.ImageSharp 3.1.x (Six Labors Split License → 本プロジェクトではApache-2.0) |
| `JapaneseBridgeProbe` | ローマ字/英語トークン → 日本語検索語（かな変換 → Google Transliterate → Jisho）。検索が失敗する「翻訳ギャップ」を埋める | MyNihongo.KanaConverter、Google Transliterate API(非公式)、Jisho API |
| `BoothZipInspectorCli` | ZIPを1つ落とすと、ファイル情報・Zone.Identifier・エントリ一覧・BOOTH手掛かりを表示する。中身は `BoothZipInspector`（ライブラリ）で、ここは入口だけ。アプリの配布物に使わない exe が入らないよう分けた（#46） | なし |
| `BoothIdResolverCli` | ZIPを落とすと商品URLを表示してクリップボードへ写す。中身は `BoothIdResolver`（ライブラリ）で、ここは入口だけ（#46） | なし |
| `BrowserHistoryProbe` | Chromium系ブラウザ（Brave/Chrome/Edge）の History DB のダウンロード履歴から、配布CDN URL `s{n}.booth.pm/<shop-uuid>/f/<商品ID>/<配布ID>/<ファイル名>` を読み、ローカルZIPとファイル名＋バイト数で照合する。DBは必ずスナップショットにコピーしてReadOnlyで開き、終了時に削除。署名付きURLのクエリは表示しない | Microsoft.Data.Sqlite |

## ZIP 以外も含めた一覧（2026-09-14 に足した）

後から増えた計測の道具。何を測るかと、結果を書いた文書。

| プロジェクト | 何を測るか | 結果の文書 |
|---|---|---|
| `AvatarEvalBench` | 対応アバター検出の今の版と案を、正解付きの試験データ（友人のデータ・リポジトリ外）に当てて適合率と再現率を比べる。`--store` で写しを選ぶ | `docs/history/avatars.md`「正解付きの試験データでの比較」 |
| `AvatarNameBench` | 登録簿の表示名の付け方を、人が付け直した正解の名前と比べる（#54） | `docs/history/avatars.md`「表示名の付け方を変えた」 |
| `CategoryFetch` | BOOTH のカテゴリ表を1度だけ取り、同梱できる形に落とす（計測ではなく取得の道具） | `docs/history/private-items.md` §8 |
| `FallbackSearchProbe` | 候補検索を別の表記で引き直す価値があるか（正解の分かる10ファイル） | `docs/research/id-resolution.md`「§7 候補検索に表記の橋渡し」 |
| `ResolveAccuracyProbe` | 自動検索が、正解の分かるファイルでどれだけ当たるか（上位3件・画面の1位）。`--make-pairs` で写しから正解の組を作り、BOOTH の答えは `--cache` に控えて `--offline` で通信なしに測り直せる（`--no-avatars` でアバター名を外す直しを切って比べる）。使い方は `Program.cs` の冒頭 | `docs/research/id-resolution.md` §15・§16 |
| `QueryVariantProbe` | 上位3件に正解が出なかったファイルで、検索語の変え方ごとの当たりを比べる | `docs/research/id-resolution.md` §15 |
| `ZipOriginProbe` | 未確定を元zipで束ねたときの束の数と、zip名で検索したときの当たり | `docs/research/id-resolution.md` §6-1 |
| `ThesaurusBridgeProbe` | 類義語辞書2つに表記の橋渡しを重ねたときの広がりと、関係の無い物の割合 | `docs/research/fuzzy-search.md` §8・§9 |
| `VRoidProbe` | BOOTH の VRoid カテゴリをアバターとして扱うべきか | `docs/history/avatars.md` §1-3 |
| `PeerProbe` | 画面の部品の UI Automation の木（型・名前・ID・持っている操作）を、アプリを起動せずに書き出す。見えない窓に部品を載せ、別のスレッドから読み上げソフトと同じ側でたどる（計測ではなく確かめの道具。場面は `probes/*.probe.txt`、使い方は `Program.cs` の冒頭）。`-- focus` は、Tab と同じ順にフォーカスを進めて、止まった所と、そのとき出ているボタンを書く。保存先と通信は切り離してある | `docs/dev/wpf.md`「UI Automation」 |
| `BoothGateProbe` | BOOTH への問い合わせの門（PC で1つ）を、2つのプロセスから叩いて間を測る（`run`）。1本のときの上乗せも測る（`bench`）。**相手は手元に立てる作り物のサーバで、BOOTH へは出さない**。門のファイルも引数の作業フォルダに置く | `docs/history/booth-machine-gate-2026-09-30.md` |
| `ScanCacheBench` | 走査の控え（`scan-cache.json`）を読む・書き換える時間と割り当てを、件数ごとに測る（`load`・`update`）。画面のスレッドの代わりの「1本で順に回すスレッド」から「見つからないファイルを探す」を丸ごと呼び、塞がる時間も測る（`find`）。Core の `DataStore` を引数の作業フォルダに向けるだけで、アプリの一式は組まない・通信しない。`make` は、アプリで測るときに自分の写し（名前が `scn` で始まる物だけ）の控えへ作り物を足す | `docs/research/large-files-2026-09-30.md`「走査の控えの読みを測った」 |

友人のデータを使う道具は、結果の名前の出る物を試験データの置き場所にだけ書く（リポジトリに入れない）。

## 実行例

```powershell
dotnet run --project experiments/UnityPackageProbe -- "D:\path\Kipfel_1.2.0.zip"
dotnet run --project experiments/ThumbnailHashProbe -- "D:\path\Kipfel_1.2.0.zip" main.png 5813187
dotnet run --project experiments/ThumbnailHashProbe -- --scan "D:\path\Kipfel_1.2.0.zip"
dotnet run --project experiments/JapaneseBridgeProbe -- tamakurage heartbeat ring tori
```

## 注意

- Google Transliterate と Jisho は非公式/無保証のキー不要APIです。レート制限や仕様変更で止まる前提で扱ってください。
- 日本語をHTTPで送る際は必ずUTF-8でURLエンコードします（Windowsのbash/curlは既定でCP932になり文字化けします）。
- ImageSharp は **3.x も 4.x も同じ Six Labors Split License 1.0** です
  （3.1.12 と 4.1.1 のライセンス文が同一であることを確認しました。以前ここには
  「4.x から変わる」と書いていましたが、事実と違いました）。
  本プロジェクトはApache-2.0で公開しているので、
  Split License の「OSSライセンスのソフトウェアで使う場合」に当たり、
  Apache License 2.0 の条件で利用できます。
