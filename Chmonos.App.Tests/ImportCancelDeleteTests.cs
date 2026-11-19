using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込みの中断で、この取り込みが新しく登録した商品を消すかも選べる（open 119・ユーザ判断 2026-11-19）。
/// 何を消して何を残すかは Core の ImportStateTests が見る。ここは窓の出し分けと、止まり切ってから消すこと
/// </summary>
public class ImportCancelDeleteTests
{
    private const string Created = "9900151";
    private const string Held = "9900152";

    /// <summary>ダウンロード元（Zone.Identifier）から商品が決まるファイルを置く。</summary>
    private static string PutDownloaded(TestApp app, string name, string itemId, byte size)
    {
        var path = app.NewFile(name, Enumerable.Repeat(size, size).ToArray());
        File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        return path;
    }

    [Fact]
    public Task 削除を選ぶと止まり切ってから新しく登録した商品を消し_ファイルは未確定に戻す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(Created, "作り物の衣装");
        app.Booth.HasItem(Held, "作り物の小物");
        var created = PutDownloaded(app, @"dl\a.zip", Created, 5);
        var held = PutDownloaded(app, @"dl\b.zip", Held, 6);
        app.Choose = _ => ChoiceDialogResult.Second;
        var main = await app.StartAsync();

        // 2つ目の商品の情報で止め、1つ目だけ作られた所で中断する
        app.Booth.Hold(url => url.Contains(Held, StringComparison.Ordinal));
        main.Import.Folders.Add(created);
        main.Import.Folders.Add(held);
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(() => app.Store.ImportState.Load().CreatedItems is [Created], "1つ目が作られる");

        main.Import.CancelCommand.Execute(null);
        app.Booth.Release();
        await UiThread.Until(() => main.Import.StackNotice?.Contains("削除", StringComparison.Ordinal) == true, "消した結果が出る");
        await app.SettleAsync();

        var question = Assert.Single(app.Choices);
        Assert.Equal("中断して、この取り込みで新しく登録した 1 件を削除する", question.Second);
        Assert.Equal("この取り込みで登録した 1 件を削除しました。ファイル 1 件を未確定に戻しました。", main.Import.StackNotice);
        Assert.Null(await app.Store.Items.LoadAsync(Created));
        Assert.True(File.Exists(created));
        Assert.True(File.Exists(held));

        // 消した商品のファイルは未確定に戻り、取り込み直せる（open 119・ユーザ判断 2026-11-20）
        Assert.Single(app.Store.Unresolved.Load(), file => file.Paths.Contains(created, StringComparer.OrdinalIgnoreCase));
    });

    [Fact]
    public Task 中断するを選ぶと何も消さない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(Created, "作り物の衣装");
        app.Booth.HasItem(Held, "作り物の小物");
        var created = PutDownloaded(app, @"dl\a.zip", Created, 5);
        var held = PutDownloaded(app, @"dl\b.zip", Held, 6);
        app.Choose = _ => ChoiceDialogResult.First;
        var main = await app.StartAsync();

        app.Booth.Hold(url => url.Contains(Held, StringComparison.Ordinal));
        main.Import.Folders.Add(created);
        main.Import.Folders.Add(held);
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(() => app.Store.ImportState.Load().CreatedItems is [Created], "1つ目が作られる");

        main.Import.CancelCommand.Execute(null);
        app.Booth.Release();
        await UiThread.Until(() => !main.Import.IsRunning, "止まる");
        await app.SettleAsync();

        Assert.Single(app.Choices);
        Assert.NotNull(await app.Store.Items.LoadAsync(Created));
    });

    /// <summary>
    /// 最後の段（画像・検出）まで通った商品も「人の手が入っていない」と見る。取り込みが書く欄を人の手と見ると、
    /// 消す相手が全部「残した」になり、窓の削除が何も消さない
    /// </summary>
    [Fact]
    public Task 最後まで取り込んだ商品は人の手が入っていないと見る() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(Created, "作り物の衣装", images: 2);
        var created = PutDownloaded(app, @"dl\a.zip", Created, 5);
        var main = await app.StartAsync();

        main.Import.Folders.Add(created);
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(() => !main.Import.IsRunning, "取り込みが終わる");
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(Created);
        Assert.NotNull(item);
        Assert.NotEmpty(item.Local.LocalFiles);
        Assert.False(Core.Services.ImportedItemRemoval.HasHumanInput(item.Local));
    });

    [Fact]
    public Task まだ何も登録していなければ聞かずに止める() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(Held, "作り物の小物");
        var held = PutDownloaded(app, @"dl\b.zip", Held, 6);
        var main = await app.StartAsync();

        app.Booth.Hold(url => url.Contains(Held, StringComparison.Ordinal));
        main.Import.Folders.Add(held);
        main.Import.StartCommand.Execute(null);
        await UiThread.Until(() => app.Booth.Requests.Any(url => url.Contains(Held, StringComparison.Ordinal)), "問い合わせで止まる");

        main.Import.CancelCommand.Execute(null);
        app.Booth.Release();
        await UiThread.Until(() => !main.Import.IsRunning, "止まる");
        await app.SettleAsync();

        Assert.Empty(app.Choices);
    });
}
