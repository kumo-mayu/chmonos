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
