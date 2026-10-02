# 画面の側の試験（Chmonos.App.Tests）

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

- `UiThread`：STA のスレッド1本と `App`（資源だけ読む。起動の処理は走らない）。`await` の続きが同じスレッドへ戻る。試験は並べて走らせない（`AssemblyInfo.cs`）。
  アプリの部品の型（`Themes/Controls.xaml`・`App.xaml`）が効くので、部品を組んで描かれた物を見る試験が書ける（`NameUnderscoreTests`：名前の「_」が消えないこと）。
  **部品は `Grid` に載せて並べる**——単独で `Measure` しても、一覧・選ぶ欄・メニューは型が組まれず木が空になる
- `TestApp`：`AppServiceContainer` の引数付きの入口（保存先・通信の出口・待ちを渡す）で組む。
  一時フォルダは `%TEMP%\chmonos-app-test-<プロセス番号>\` の下。試験の終わり・一式の終わり・次の一式の始めの3段で消す
- 本体の側の口：`AppServiceContainer`（引数付きの入口・`DetectUnityTools`・`DiscoverUnityProjects`）、`Notice.Intercept`、`FireAndForget.Pending`。
  どれも既定はアプリの動きのままで、試験だけが差し替える

一式は約10秒（2026-09-30・287件）。保存先を作る試験は1件 20〜40ms。1秒かかる試験は、時計で進む物を待っている
（今は4件が1.0秒：ナビの件数を数え直す間隔 `MainViewModel.CountsInterval` の1秒を、`SettleAsync` が待っている）。

## 重くしない（本体の試験 `Chmonos.Core.Tests` も同じ）

`dotnet test` 全体で約16秒（本体 約2,300件が13秒・画面の側が10秒で、並んで走る）。前は95秒かかっていて、原因は2つだけだった（2026-09-30 に測った）。

- **BOOTH の待ちは差し替える。**`new BoothClient(http, settings, TestWait.None)`（画面の側は `TestApp` が渡している）。
  **`FetchIntervalMs = 0` では待ちは消えない**——間隔の床（1.5秒）は `BoothClient` 自身が踏むので、作り物の BOOTH に問い合わせるたびに実際に1.5秒待つ
  （取り込みの試験が1件 6〜18秒になり、一式の90秒はこれで決まっていた）。失敗の再試行（2秒・8秒）も同じ口で消える。
  差し替えても、順番・回数・優先度は変わらない（門は1本のまま）。待ちの長さを確かめる試験は、待ちを記録する関数を渡す（`BoothClientTests`）。
  **待ちを差し替えた `BoothClient` は、PC で1つの門（`BoothMachineGate`）に入らない**——試験が本物の門のファイルを書き換えず、その PC でアプリが動いていても結果が変わらない。
  差し替えてよいのは相手が作り物のときだけ（通信の出口が本物のまま待ちを差し替える `AppServiceContainer` は投げる）。門そのものの試験は、一時フォルダの門と手で進める時計を渡す（`BoothMachineGateTests`）
- **辞書は使い回す。**`SharedDictionaries.Bridge`・`.Japanese`・`.Readings`（一式で1回だけ組む。JMdict は組むのに3秒、並んで走ると6〜18秒）。
  `new JapaneseDictionary(同梱の辞書, 新しい控え)` を試験ごとに書かない。控えを書き換える・壊す試験だけが、自分の一時フォルダに自分の控えを作る
  （作り物の小さい辞書で足りるなら、そちらを使う。`DictionaryFailureTests`）
- **読むのに時間がかかる物を足すときも同じ**：組んだ後に書き換えない物なら、静的な遅延初期化で1回だけ作る。書き換える物は使い回さない
- 「n 秒後に起きる」を実際に待たない。待つ長さを渡せる口（`delay`）か、今やる道（`Debounced.RunNowAsync`）で確かめる

**探し方**：`dotnet test --no-build --logger trx --results-directory <一時フォルダ>` で試験ごとの時間が出る（`UnitTestResult` の `duration`）。
クラスごとに足して重い順に見る。**1件の時間が1.5秒の倍数なら BOOTH の待ち、数秒で辞書を引いているなら組み直し。**
並んで走ると1件が長く見えるので、疑った試験は `--filter` で単独でも走らせる。全体の時間は、いちばん長いクラス1つで決まる（クラスの中は順に走る）。
並行の設定（xUnit の既定：クラスごとに並行・スレッドは論理コアの数）は変えても縮まなかった（8・32・無制限で 13〜15秒。差は誤差の内）。
