# ビルドとテスト・配る物の組み方

2026-10-07 まで直下の `README.md` にあった、開発する人向けの説明。直下の README は使う人向けに書き換えた。

## ビルドとテスト

```powershell
dotnet build
dotnet test Chmonos.Core.Tests
```

実行ファイルは `Chmonos.App\bin\Debug\net10.0-windows\Chmonos.exe`。
データの保存先は `%LOCALAPPDATA%\Chmonos`。環境変数 `CHMONOS_HOME` で差し替えられる。

## 渡す用のビルド

```powershell
dotnet publish Chmonos.App -p:PublishProfile=win-x64
```

`publish\Chmonos-win-x64\` に **exe 1つ ＋ `assets` フォルダ**（辞書など4件）の計5ファイルで出る。

- **自己完結**（.NET 10 のランタイムを同梱。exe は約 171 MB）。渡した相手にランタイムを入れてもらう必要はない。入れてもらう手順が1つ増えるほど、起動する前に詰まる
- **事前翻訳（ReadyToRun）している**。起動のたびの JIT が減り、起動が約 0.3 秒縮む（exe は約 30 MB 大きくなる。`docs/research/startup-dotnet10-2026-10-01.md`）。作るときに NuGet から事前翻訳の包みを取ってくる
- **1ファイルにまとめている**（ネイティブDLLも exe の中）。起動すると `%TEMP%\.net\` へネイティブDLL 5つ・7.8MB を展開し、以降は使い回す。
  これは `docs/research/antivirus.md` A の「参考」が懸念していた動きなので、**セキュリティソフトで詰まったらプロファイルの `PublishSingleFile` を false に戻す**
- **辞書（`assets\`）は exe の外に残す**。アプリは `AppContext.BaseDirectory\assets\` を見るので、exe に入れると見つけられなくなる（`Chmonos.Core.csproj` の `ExcludeFromSingleFile`）。**exe だけ取り出しても動かない。フォルダごと渡す**
- デバッグ情報は DLL に埋め込む（`Directory.Build.props` の `DebugType=embedded`）。pdb を並べずに、`logs\app.log` のスタックトレースの行番号を残すため
- 署名は無いので、初回は SmartScreen の「発行元不明」が出る（同 A の1つ目。まだ打っていない手）

設定は `Chmonos.App\Properties\PublishProfiles\win-x64.pubxml`。csproj ではなくプロファイルに置いてあるのは、
`RuntimeIdentifier` を csproj へ書くと `dotnet build` まで RID 付きになり、開発用の exe が
`bin\Debug\net10.0-windows\win-x64\` へ移って `ui-check` の起動が壊れるため。
