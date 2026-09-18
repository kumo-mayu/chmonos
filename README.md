# Chmonos（クモノス）

BOOTH.pm で買ったアセット（VRChat 向けの衣装・アバターなど）を手元で管理する Windows アプリ（WPF / .NET 9）。**まだ公開していない。**
コードの名前空間・実行ファイル名は、改名前の `BoothAssetManager` のまま（公開の直前に変える）。

> **ライセンス**：コードは [Apache License 2.0](LICENSE)、設計文書は
> [CC BY 4.0](LICENSE-docs.md)。同梱している第三者のデータは [NOTICE](NOTICE) を参照。
>
> BOOTHへ問い合わせる処理を含みます。**必ず1本ずつ、1.5秒以上空けて**行う作りにしてあり、
> フォークする場合もその作法を残してほしい旨を `NOTICE` に書いています。

## 何ができるか

- ダウンロードした zip・展開したフォルダから商品を特定し、BOOTH の商品情報と合わせて手元に記録する
- 対応アバター・ユーザタグ・属性で探す。改変（着せ替え）に使ったアセットを記録し、Unity へ送る

機能の一覧は [docs/features.md](docs/features.md)、文書の入口は [docs/README.md](docs/README.md)。

## ビルドとテスト

```powershell
dotnet build
dotnet test BoothAssetManager.Core.Tests
```

実行ファイルは `BoothAssetManager.App\bin\Debug\net9.0-windows\BoothAssetManager.App.exe`。
データの保存先は `%LOCALAPPDATA%\Chmonos`。環境変数 `CHMONOS_HOME` で差し替えられる。
