# 画面の側の試験（BoothAssetManager.App.Tests）

ViewModel と App の Services の試験。**窓を出さずに、アプリと同じ組み立てを一時フォルダの保存先で作って確かめる。**
App のコードを直す前に読む。決めた経緯は `docs/history/app-tests-2026-09-30.md`。

## 何を試験で確かめ、何を画面で見るか

| 試験で確かめる（起動しない） | 画面で見る（`ui-check`） |
|---|---|
| 文言の出し分け（結果の文・空の文・吹き出し・確認の窓の文） | 配置・余白・折り返し・切れ |
| ボタン・札を出すか、押せるか | 色・コントラスト・暗い表 |
| 一覧に何が並ぶか、件数、絞り込み | 一覧を流したときの速さ・固まり |
| 押したら保存先に何が書かれるか、ほかの画面の数が合うか | ドロップ・キー・マウスの手触り |
| 画面の履歴（戻る・進む）と戻るの文言 | XAML の部品が ViewModel の値を正しく出しているか |

**「計算で決まる物」は試験に書く。**撮って読むと1回に数分かかり、組み合わせ（VCC だけ・ALCOM だけ・どちらも無い）は実マシンでは作れない。

## 書き方

```csharp
[Fact]
public Task 壊れたzipだけを出すと_壊れたzipを持つ商品だけが並ぶ() => TestApp.Run(async app =>
{
    // 1. 保存先へ置く（主画面を作る前に）
    await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(@"D:\files\a.zip", archiveBroken: true)));

    // 2. 主画面を作る。検索の読み込みまで済んでから戻る
    var main = await app.StartAsync();

    // 3. 押す・開く
    main.Search.ShowOnlyBrokenZip();

    // 4. 確かめる
    Assert.Equal("1 件", main.Search.ResultSummary);
});
```

- **`TestApp.Run`**：中身は画面のスレッドで走る。終わりに、投げっぱなしの作業が済むのを待ち、**ログに「失敗」が残っていたら試験を落とす**
  （`Forget()` で投げた読み込みの失敗は、画面にもログにしか出ない）。失敗する道を確かめる試験は `app.AllowLoggedFailures = true`
- **`app.SettleAsync()`**：`Forget()` で投げた読み込み・保存が済むまで待つ。コマンドを押した後・画面を開いた後に呼ぶ。
  待ってから書く物（0.5秒後に書く検索の条件など）は数に入らないので、`UiThread.Until(() => 条件, "何を待つか")` で名指しして待つ
- **`Make.Item` は持っている商品**（zip を1つ持つ）。統計・ショップの所持の数・フォルダの表示は持っている商品しか数えないので、
  持たせないと関係の無い試験で空の表示になる。持っていない商品は `.WithFiles()`
- **検索の条件は空から始まる。**既定の条件を確かめる試験だけが、`SearchModules = null` に戻してから始める（`SearchDefaultsTests`）
- **`app.Booth`**（作り物の BOOTH）：何も教えなければ 404。`HasItem(id, name)` で商品を答えさせる。`Requests` で「BOOTH へ行かない」を確かめられる
- **`app.Tools`・`app.UnityProjects`**：Unity Hub・VCC・ALCOM とプロジェクトの一覧（作り物）。実マシンに入っている物は見ない
- **確認の窓**（`Notice`）は出ない。出すはずだった文は `app.Notices` に入り、答えは `app.Answer`（既定は「やめる」側）
- **画面は主画面から開く**（`main.ShowStatsCommand.Execute(null)`・`main.ShowItem(item)`）。組み込んで使う画面は直に作ってよい
  （`new ResolveViewModel(app.Services, main)`）
- ViewModel を作らずに済む関数は `internal static` にして、値を渡して確かめる（`ImportViewModel.UnreadableFilesText` など）。速く、組み合わせを全部書ける
- WPF の部品を作るだけの試験は `UiThread.Run(() => { … })`（STA。見本は `UiThreadTests`）

試験の名前は日本語で、何が成り立つかを書く。作り物のデータの名前・ID・ファイル名は作り物にする（友人のデータの物を書かない）。

## 書かない物

- **窓を出す試験**（`ShowDialog`・ファイルを選ぶ窓・`ListChoice`・`ChoiceDialog`）。答える人がいないので止まる。
  窓を出す道を通る操作は、窓の手前までを関数に切り出して確かめる
- **外へ出る操作**：エクスプローラを開く・ブラウザを開く・クリップボード・Unity へ送る・ごみ箱へ送る
- **時計に左右される試験**。「n 秒後に戻ります」「0.8秒後に保存」は、今やる道（`Debounced.RunNowAsync`・設定で切る）で確かめる
- **実マシンに左右される試験**（Unity が動いているか・ドライブの構成・入っているアプリ）。差し替えの口が無ければ、値を渡す関数に切り出す
- **XAML・見た目**。素の `Application` には色の表も型も入っていない（窓なしで画面を画像にする台は別のプロジェクト）

## 仕組み（`Support/`）

- `UiThread`：STA のスレッド1本と素の `Application`。`await` の続きが同じスレッドへ戻る。試験は並べて走らせない（`AssemblyInfo.cs`）
- `TestApp`：`AppServiceContainer` の引数付きの入口（保存先・通信の出口・待ちを渡す）で組む。
  一時フォルダは `%TEMP%\chmonos-app-test-<プロセス番号>\` の下で、一式の終わりにまとめて消す
- 本体の側の口：`AppServiceContainer`（引数付きの入口・`DetectUnityTools`・`DiscoverUnityProjects`）、`Notice.Intercept`、`FireAndForget.Pending`。
  どれも既定はアプリの動きのままで、試験だけが差し替える

一式は約9秒（2026-09-30・285件）。**1分を超えそうなら、遅い試験を `--logger "console;verbosity=normal"` で探す**
（保存先を作る試験は1件 20〜40ms。1秒かかる試験は、時計で進む物を待っている）。
