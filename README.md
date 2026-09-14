# BoothZipInspector

> **ライセンス**：コードは [Apache License 2.0](LICENSE)、設計文書は
> [CC BY 4.0](LICENSE-docs.md)。同梱している第三者のデータは [NOTICE](NOTICE) を参照。
>
> BOOTHへ問い合わせる処理を含みます。**必ず1本ずつ、1.5秒以上空けて**行う作りにしてあり、
> フォークする場合もその作法を残してほしい旨を `NOTICE` に書いています。


Windows上で、BOOTHからダウンロードした可能性のあるZIPファイルの情報を確認するための、最小限のC#コンソールアプリのプロトタイプです。

## 目的

このツールは、ローカルにあるZIPファイルから次の手掛かりを収集して表示します。

- ファイル自体の基本情報（サイズ、日時、SHA-256）
- Windowsが記録するダウンロード元情報（NTFS代替データストリーム `Zone.Identifier`）
- ZIP内のファイル一覧
- ZIP内のテキストファイルに含まれるBOOTH商品URL・ショップURLなどの手掛かり

**この段階の目的は商品IDを必ず特定することではありません。** 後からBOOTH商品との照合機能を作るために、ローカルのZIPからどの程度の手掛かりを取得できるかを確認するためのプロトタイプです。

## 必要環境

- Windows 10 / 11
- .NET 9 SDK（.NET 8 では `ZipArchive` が UTF-8 フラグ付きエントリ名を指定エンコーディングで誤って読む回帰があり、日本語ファイル名が文字化けするため .NET 9 を使います）

`Zone.Identifier`（NTFS代替データストリーム）はNTFSボリューム上でのみ読み取れます。それ以外の環境では「見つかりませんでした」と表示され、処理は継続します。

## ビルド方法

```powershell
dotnet build
```

## テスト実行

```powershell
dotnet test
```

## 使い方

### 1. ZIPパスをコマンドライン引数として渡す

```powershell
dotnet run --project BoothZipInspector -- "C:\Downloads\example.zip"
```

ビルド済みの実行ファイルを使う場合：

```powershell
BoothZipInspector.exe "C:\Downloads\example.zip"
```

Windows Terminal、PowerShell、コマンドプロンプトへZIPファイルをドラッグ＆ドロップすると、そのパスがコマンドラインへ自動的に入力されます（前後に引用符が付くことがありますが、そのまま実行して問題ありません）。

### 2. 引数なしで起動する

```powershell
dotnet run --project experiments/BoothZipInspectorCli
```

次のように表示されるので、ZIPファイルをターミナルへドラッグ＆ドロップしてEnterを押してください。

```text
ZIPファイルをこのウィンドウへドラッグ＆ドロップして、Enterを押してください：
```

入力されたパスの前後の引用符は自動的に取り除かれます（パス内部の空白は維持されます）。

`Ctrl+C` でいつでも終了できます。

## 表示される情報

- **ファイル**: 絶対パス、ファイル名、拡張子、サイズ、作成日時、最終更新日時、SHA-256
- **ダウンロード元情報**: `Zone.Identifier` から取得した `ZoneId` / `ReferrerUrl` / `HostUrl`、およびそこから見つかったBOOTH商品ID。商品ページURL（`booth.pm/ja/items/{id}`、`shop.booth.pm/items/{id}`）に加え、Chrome/Edge が `HostUrl` に書く配布CDN URL（`s{n}.booth.pm/<uuid>/f/{商品ID}/{配布ID}/...`）も認識します。Brave は `HostUrl` にオリジンしか書かないため商品IDは得られません
- **ZIP**: エントリ数、非圧縮/圧縮後の合計サイズ、ファイル一覧（すべて表示、省略なし）
- **BOOTH手掛かり**: ZIP内のテキストファイル（`.txt` `.md` `.json` `.html` `.htm` `.url` `.xml` `.yaml` `.yml`）から見つかったBOOTH商品URL・ショップURL・その他の`booth.pm`関連URLと、それぞれの発見元パス

## 重要な注意事項

- **`Zone.Identifier` は存在しないことがあります。** ファイルがコピーされた場合、NTFS以外のファイルシステムに保存された場合、ブラウザの設定によっては、ダウンロード元情報が失われている、または最初から記録されていないことがあります。その場合は「見つかりませんでした」と表示され、エラー終了はしません。
- **BOOTHの商品IDを必ず特定できるわけではありません。** ダウンロード元情報やZIP内のテキストに手掛かりが一切残っていない場合もあります。このツールはあくまで手掛かりの収集を行うものであり、商品の断定は行いません。
- **ZIPの内容を外部へ送信することはありません。** すべての解析はローカルで完結します。ネットワーク通信は一切行いません。
- ZIP Slip対策として、検査目的でもZIPの内容をディスクへ展開することはありません。ZIP内のエントリはメモリ上でのみ読み取ります。

## 制限事項（このプロトタイプで実装していないもの）

- BOOTHへの通信・ログイン・ライブラリ取得・商品JSON取得
- ブラウザ履歴の解析
- 商品名検索や候補スコアリング
- ZIPの展開・インストール、`.unitypackage` の解析
- GUI、設定保存、自動更新

## プロジェクト構成

```text
BoothAssetManager.sln
BoothZipInspector/              zipを読む中身（ライブラリ。アプリも使う）
  PathNormalizer.cs             D&D入力パスの引用符除去
  FileInspector.cs               ファイル基本情報・サイズ表記
  ZoneIdentifierReader.cs        Zone.Identifier読み取り(I/O)とパース(純粋ロジック)
  ZipInspector.cs                ZIP読み取り(展開なし)、エントリ一覧
  TextDecoder.cs                  BOM/UTF-8/Shift-JISの文字コード判定
  BoothUrlExtractor.cs           BOOTH URL・商品IDの抽出(正規表現)
  BoothClueCollector.cs          手掛かりの重複除去
  Models/                        データモデル
BoothZipInspector.Tests/        xUnitテストプロジェクト
BoothIdResolver/                zip→商品IDの判定（ライブラリ。アプリも使う）
BoothIdResolver.Tests/          xUnitテストプロジェクト
experiments/BoothZipInspectorCli/  上の BoothZipInspector をコマンドで動かす入口（Program.cs）
experiments/BoothIdResolverCli/    D&D→商品URLをクリップボードへコピーする入口(下記)
```

## BoothIdResolver（商品URLコピーツール）

ZIPファイルをドラッグ＆ドロップ（または引数指定）すると、`https://booth.pm/ja/items/{ID}` を表示してクリップボードへコピーします。BoothZipInspectorと同じくネットワーク通信は行わず、次の2つの手掛かりだけで判定します。

1. **Zone.Identifier**（実際のダウンロード元。最優先）
2. **ZIP内テキストのBOOTH商品URL**（Zone.Identifierで分からない場合のフォールバック。lilToon等の既知の依存ツールIDは本体と誤認しないよう除外）

複数の異なる商品IDが見つかった場合は自動選択せず、候補をすべて表示するだけでクリップボードにはコピーしません（誤った商品IDを断定しないため）。

`BoothIdResolver/` は判定の中身（ライブラリ）で、アプリもこれを使います。コマンドとして動かす入口は `experiments/BoothIdResolverCli/` にあります（アプリの配布物に使わない exe が入らないよう、入口を分けた。#46）。

```powershell
dotnet run --project experiments/BoothIdResolverCli -- "C:\Downloads\example.zip"
```

### ネイティブ単体実行ファイルとしてビルドする

.NETランタイムのインストールが不要な、`BoothIdResolverCli.exe` 1ファイルだけで動くネイティブAOTビルドを作れます（VC++ Build ToolsとWindows SDKが必要）。

```powershell
dotnet publish experiments/BoothIdResolverCli/BoothIdResolverCli.csproj -c Release -r win-x64 -p:PublishAot=true --self-contained true -o publish/BoothIdResolver
```

`publish/BoothIdResolver/BoothIdResolverCli.exe` だけをコピーして配布・実行できます。同フォルダに生成される `.pdb` はデバッグシンボルなので実行には不要です。

## 紐付け方式の調査資料

- [設計書: ZIP→BOOTH商品ID 特定](docs/research/id-resolution.md): 実ZIP 18本で検証した手法・パイプライン v2・Zone.Identifier と `items/{id}.json` の仕様
- [experiments/](experiments/README.md): `.unitypackage` 名前空間抽出、サムネイル dHash 照合、ローマ字→日本語ブリッジの検証ツール（本体とは別プロジェクト）
- [初回調査](docs/research/zip-linking.md): 18 ZIPの候補一覧とローカル証拠の分析
- [追補調査](docs/research/zip-linking-followup.md): 公開商品JSON、PDF、BOOTH Library Manager、AssetConnectを含む実装案
- `research/Compare-CatalogEvidence.ps1`: 公開配布名、任意のAssetConnect CSV、SHA-256を証拠として結合する読み取り専用プローブ
