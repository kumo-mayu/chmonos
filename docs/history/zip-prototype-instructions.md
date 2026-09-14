# Claude Code 実装指示書：BOOTH ZIP情報表示プロトタイプ

> **要約（2026-09-14 に足した。中身は書かれた時点のまま）**
> - **何の文書か**：最初期に作者が書いた、`BoothZipInspector`（zip を落とすと手掛かりを表示するコンソールアプリ）の実装指示書。
>   目的は、商品IDを当てることではなく、手元の zip からどの程度の手掛かりが取れるかを確かめること。
> - **後で変わった所**：
>   - .NET 8 ではなく net9.0 になった（.NET 8 の zip の名前の不具合・`docs/research/id-resolution.md` §10）。
>   - 本体はライブラリになり、コマンドの入口は `experiments/BoothZipInspectorCli` に分けた（配布物の exe を減らすため・`docs/research/antivirus.md`）。
>   - 手掛かりの使い方は `docs/spec/id-resolution.md`。
> - **節**：目的／技術条件／起動方法／表示する情報（A 基本情報・B Zone.Identifier・…）以降

## 目的

Windows上で、BOOTHからダウンロードした可能性のあるZIPファイルをターミナルへドラッグ＆ドロップし、そのファイルに残っている情報を確認できる最小限のC#コンソールアプリを作成してください。

この段階の目的は商品IDを必ず特定することではありません。後からBOOTH商品との照合機能を作るために、ローカルのZIPからどの程度の手掛かりを取得できるか確認することです。

## 技術条件

- 言語：C#
- 対象：Windows 10 / 11
- フレームワーク：.NET 8
- 形式：コンソールアプリ
- プロジェクト名：`BoothZipInspector`
- 外部NuGetパッケージは原則使用しない
- ZIPの読み取りには`System.IO.Compression`を使用する
- JSON出力やGUIはまだ作らない

## 起動方法

次の両方に対応してください。

### 1. ZIPパスをコマンドライン引数として渡す

```powershell
dotnet run -- "C:\Downloads\example.zip"
```

ビルド済みの場合：

```powershell
BoothZipInspector.exe "C:\Downloads\example.zip"
```

Windows Terminal、PowerShell、コマンドプロンプトへZIPをドラッグ＆ドロップするとパスが入力されるため、そのパスを引数として利用できること。

### 2. 引数なしで起動した場合

次の案内を表示し、ユーザーがZIPをターミナルへドラッグ＆ドロップしてEnterを押せるようにしてください。

```text
ZIPファイルをこのウィンドウへドラッグ＆ドロップして、Enterを押してください：
```

入力されたパスの前後に引用符がある場合は取り除いてください。ただし、パス内部の空白は維持してください。

## 表示する情報

### A. ファイル基本情報

- 絶対パス
- ファイル名
- 拡張子
- ファイルサイズ（バイトと読みやすい単位の両方）
- 作成日時
- 最終更新日時
- SHA-256

### B. Windowsダウンロード元情報

対象ファイルのNTFS代替データストリームを、次のパスとして読み取ってください。

```text
元ファイルパス:Zone.Identifier
```

取得できた場合は、最低限次を表示してください。

- `ZoneId`
- `ReferrerUrl`
- `HostUrl`
- `ReferrerUrl`または`HostUrl`から見つかったBOOTH商品ID

BOOTH商品URLは少なくとも次の形式を認識してください。

```text
https://booth.pm/ja/items/1234567
https://booth.pm/en/items/1234567
https://shop-name.booth.pm/items/1234567
```

商品ID抽出用の正規表現は、サブドメイン形式と言語パス形式の両方を扱ってください。

代替データストリームが存在しない、コピー等で失われている、またはNTFS以外で利用できない場合はエラー終了せず、次のように表示して処理を続行してください。

```text
Zone.Identifier: 見つかりませんでした
```

### C. ZIP情報

- ZIP内エントリ数
- 非圧縮時合計サイズ
- 圧縮後合計サイズ
- ディレクトリエントリを除くファイル一覧

ファイル一覧には次を表示してください。

- ZIP内相対パス
- 非圧縮サイズ
- 圧縮サイズ

一覧が長い場合も、プロトタイプでは省略せずすべて表示してください。

### D. ZIP内部のBOOTH手掛かり

以下の拡張子を持つエントリをテキスト候補として調べてください。

```text
.txt
.md
.json
.html
.htm
.url
.xml
.yaml
.yml
```

ただし、1エントリが2 MiBを超える場合は本文を読まずスキップしてください。ZIP Slip対策のため、検査目的でZIPをディスクへ展開しないでください。

各テキスト候補から以下を抽出してください。

- BOOTH商品URLと商品ID
- BOOTHショップURL
- `booth.pm`を含むその他のURL

読み取り文字コードは次の順で試してください。

1. BOM付き文字コードはBOMに従う
2. UTF-8
3. UTF-8として不正ならShift-JIS（コードページ932）

Shift-JISを扱うため、起動時に以下を登録してください。

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

同じURLや商品IDが複数回見つかった場合は重複を除き、発見元のZIP内パスも表示してください。

## 出力イメージ

厳密に同じ装飾でなくて構いませんが、情報の区切りが分かるようにしてください。

```text
=== ファイル ===
パス: C:\Downloads\example.zip
ファイル名: example.zip
サイズ: 12.34 MiB (12,939,264 bytes)
SHA-256: ABCDEF...

=== ダウンロード元情報 ===
ZoneId: 3
ReferrerUrl: https://booth.pm/ja/items/1234567
HostUrl: https://...
BOOTH商品ID: 1234567

=== ZIP ===
エントリ数: 42
非圧縮時合計: 30.12 MiB
圧縮後合計: 12.30 MiB

[ファイル一覧]
README.txt | 1.2 KiB -> 630 bytes
Assets/example.prefab | 24.8 KiB -> 4.1 KiB

=== BOOTH手掛かり ===
商品ID: 1234567
URL: https://booth.pm/ja/items/1234567
発見元: README.txt
```

情報が見つからなかったセクションも非表示にせず、「見つかりませんでした」と表示してください。

## 入力検証とエラー処理

以下を明確な日本語メッセージで処理してください。

- パスが空
- ファイルが存在しない
- ディレクトリが指定された
- ZIP以外の拡張子
- 壊れたZIPまたはZIPとして開けない
- アクセス権不足
- 使用中などの理由でファイルを開けない

ユーザー入力の誤りではスタックトレースをそのまま表示しないでください。想定外の例外については、例外型とメッセージを表示して非ゼロの終了コードを返してください。

`Ctrl+C`で終了できるようにし、キャンセルを通常の致命的エラーとして扱わないでください。

## 実装方針

処理を`Program.cs`だけへ詰め込まず、最低限次の責務に分けてください。名前は変更して構いません。

```text
Program.cs
FileInspector.cs
ZoneIdentifierReader.cs
ZipInspector.cs
BoothUrlExtractor.cs
Models/
```

過度な抽象化、DIコンテナ、GUI、データベースは不要です。将来の機能追加よりも、今回のプロトタイプが読みやすく確実に動くことを優先してください。

## テスト

xUnitのテストプロジェクト`BoothZipInspector.Tests`を作成してください。最低限、次をテストしてください。

- 引用符付きパスの正規化
- BOOTH標準ドメインの商品URLからの商品ID抽出
- ショップサブドメインの商品URLからの商品ID抽出
- BOOTH以外のURLを商品として扱わない
- URLと商品IDの重複除去
- UTF-8のテキストを含むZIPからURLを発見できる
- Shift-JISのテキストを含むZIPからURLを発見できる
- テキストを含まないZIPでも正常終了する
- 壊れたZIPを適切に報告する

`Zone.Identifier`は環境依存なので、読み取り処理を差し替え可能にするか、パーサー部分と実ファイルアクセス部分を分離し、パーサーを単体テストしてください。

## README

`README.md`に以下を記載してください。

- このツールの目的
- 必要環境
- ビルド方法
- ターミナルへのドラッグ＆ドロップを含む使い方
- `Zone.Identifier`は存在しない場合があること
- BOOTHの商品IDを必ず特定できるものではないこと
- ZIPの内容を外部へ送信せず、ローカルで解析すること

## 今回は実装しないもの

- BOOTHへの通信
- BOOTHへのログイン
- BOOTHライブラリの取得
- 商品JSONの取得
- ブラウザ履歴の解析
- 商品名検索や候補スコアリング
- ZIPの展開・インストール
- `.unitypackage`の解析
- GUI
- 設定保存
- 自動更新

これらは勝手に追加しないでください。

## 完了条件

以下をすべて満たしたら完了です。

1. WindowsでZIPパスを引数として渡して実行できる
2. 引数なしの場合はD&D後のパス入力を受け付ける
3. ファイル基本情報、`Zone.Identifier`、ZIP一覧、BOOTH URL候補を表示できる
4. 情報が欠けていてもクラッシュせず理由を表示する
5. ZIPをディスクへ展開しない
6. `dotnet build`が成功する
7. `dotnet test`が成功する
8. READMEに実行方法が記載されている

## 実装後の報告形式

実装完了後、次を簡潔に報告してください。

- 作成したプロジェクト構成
- 実装した機能
- 実行例
- `dotnet build`の結果
- `dotnet test`の結果とテスト件数
- 懸念点または未対応事項
