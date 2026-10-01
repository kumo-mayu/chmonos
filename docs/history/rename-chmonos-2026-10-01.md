# コードとリポジトリの名前を Chmonos に揃えた（2026-10-01）

画面の名前・保存先・環境変数・BOOTH へ送るアプリ名は 2026-09-14 に Chmonos へ改名済みだった（`docs/research/booth-terms.md` §4）。
コードの名前空間・実行ファイル名・リポジトリ名・写しのフォルダ名は「公開の直前に変える」としていたが、
どのみち要る手間なので今まとめて変えた（ユーザ判断 2026-10-01「C にしよう。どちらにせよ今後必要になる手間です」）。

## 変えた物

| 前 | 後 |
|---|---|
| `BoothAssetManager.App`・`.Core`・`.Cli`・`.App.Tests`・`.Core.Tests`（フォルダ・csproj・名前空間） | `Chmonos.App`・`Chmonos.Core`・`Chmonos.Cli`・`Chmonos.App.Tests`・`Chmonos.Core.Tests` |
| `BoothAssetManager.sln` | `Chmonos.sln` |
| 実行ファイル `BoothAssetManager.App.exe` | `Chmonos.exe`（App の組み立ての名前を `Chmonos` にした。名前空間は `Chmonos.App` のまま） |
| 画面の資源の道 `/BoothAssetManager.App;component/…` | `/Chmonos;component/…` |
| 写しの置き場 `%LOCALAPPDATA%\BoothAssetManager-<名前>` | `%LOCALAPPDATA%\Chmonos-sandboxes\<名前>` |
| 設定の「バックアップ」の zip の名前 `BoothAssetManager-backup-日付.zip` | `Chmonos-backup-日付.zip`（画面から見える物の直し漏れだった） |
| リポジトリ `booth-asset-manager` | `chmonos` |

`BoothIdResolver`・`BoothZipInspector` は「BOOTH の ID を特定する」「BOOTH の zip を読む」という役目の名前なので残した。

## 変えなかった物・残した物

- `docs/history/` の中（書き直さない決まり）。書かれた時点の古い名前のまま
- `%LOCALAPPDATA%\BoothAssetManager-history-backup`（履歴を書き換える前の控え。触らない約束なので動かしていない）
- 保存されているデータ：保存先・JSON の形は前から Chmonos で、型の名前も JSON に入っていないので、作り直しも読み替えも要らない
- 確かめの道具は、改名前の本番と friendtest の置き場も「開かない場所」に入れたまま（残っていたら本番の中身かもしれない）

## 確かめたこと

- ビルドは警告0。試験は全部通過（本体 2,350・画面の側 377・zip 45・ID 8）。ソリューションの外の道具と実験も全部ビルドできた
- 窓を出さない台で全55場面を明暗で描き、改名前と比べた：109 枚のうち 103 枚は画素まで同じ。違った6枚は設定の画面の保存先のパスの所（台の作業フォルダの番号で毎回変わる）
- 確かめの道具で、新しい置き場の写しから `Chmonos.exe` を起動し、商品ページを開き、新しい写しを作れた。本番は変わっていない

## 一括の置き換えで崩れて、戻した物

「2026-09-14 に BoothAssetManager から改名」の形の経緯の文が「Chmonos から改名」になったので、3か所を元の名前に戻した
（`docs/spec/background-and-network.md`・`docs/spec/data-model.md`・`StoreLocation.cs` のコメント）。
