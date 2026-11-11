using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 何行かを選んでコピーした物の中の商品を、まとめて情報だけの商品として登録する（計画 26）。
/// BOOTH は作り物（<see cref="FakeBooth"/>）。HTML・番号・名前も作り物。
/// </summary>
public class PastedItemsRegisterTests
{
    private static string CfHtml(string fragment)
        => "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\nStartFragment:0000000141\r\nEndFragment:0000000900\r\n"
            + "SourceURL:https://sample-shop.booth.pm/items/1009999\r\n"
            + "<html><body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body></html>";

    /// <summary>ショップの一覧を3件分コピーした形。素の文には URL が無い。</summary>
    private static readonly string ListHtml = CfHtml(
        "<a href=\"https://sample-shop.booth.pm/items/1000001\">作り物の上着</a>"
        + "<a href=\"https://sample-shop.booth.pm/items/1000002\">作り物のスカート</a>"
        + "<a href=\"https://sample-shop.booth.pm/items/1000003\">作り物のリボン</a>");

    private const string ListText = "作り物の上着\n作り物のスカート\n作り物のリボン\n";

    // ---- 前提：読む順（計画 26 の「未確認」を確かめる）----

    /// <summary>
    /// 何行かを選んだ物には素の文と HTML の両方が入る。今の読む順では素の文が先に読まれ、
    /// リンク先の商品は見えない。だから HTML を別に読んで渡す（<see cref="MainWindow.ReadHtml"/>）。
    /// </summary>
    [Fact]
    public Task 素の文とHTMLの両方があると_素の文が先に読まれ_HTMLは別に読める() => UiThread.Run(() =>
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, ListText);
        data.SetData(DataFormats.Html, ListHtml);

        Assert.Equal(ListText, MainWindow.ReadText(data));
        Assert.Equal(ListHtml, MainWindow.ReadHtml(data));
    });

    [Fact]
    public Task HTMLだけなら_見出しを落とした本体を読み_HTMLも渡せる() => UiThread.Run(() =>
    {
        var data = new DataObject();
        data.SetData(DataFormats.Html, ListHtml);

        var text = MainWindow.ReadText(data);
        Assert.NotNull(text);
        Assert.DoesNotContain("SourceURL", text, StringComparison.Ordinal);
        Assert.Equal(ListHtml, MainWindow.ReadHtml(data));
    });

    [Fact]
    public Task HTMLが無ければ_ReadHtmlはnull() => UiThread.Run(() =>
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "https://booth.pm/ja/items/1000001");

        Assert.Null(MainWindow.ReadHtml(data));
    });

    // ---- 窓 ----

    [Fact]
    public Task 二件以上を貼ると_出てきた順に並べ_手元にある物はチェックを外して押せなくする() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000002", "作り物のスカート"));
        var main = await app.StartAsync();

        await main.HandleDropAsync(null, ListText, html: ListHtml);

        var dialog = Assert.Single(app.PastedItemDialogs);
        Assert.Equal(["1000001", "1000002", "1000003"], dialog.Rows.Select(row => row.ItemId));
        Assert.Equal(["商品ID 1000001", "作り物のスカート", "商品ID 1000003"], dialog.Rows.Select(row => row.Name));
        Assert.Equal([true, false, true], dialog.Rows.Select(row => row.IsChecked));
        Assert.Equal([true, false, true], dialog.Rows.Select(row => row.CanCheck));
        Assert.Equal("ライブラリにあります・商品ID 1000002", dialog.Rows[1].NoteText);
        Assert.Equal("3 件の商品が見つかりました。登録する商品を選んでください。", dialog.HeadingText);
        Assert.Equal("2 件を登録", dialog.CommitText);

        // 手元にある物は、チェックしようとしても入らない
        dialog.Rows[1].IsChecked = true;
        Assert.False(dialog.Rows[1].IsChecked);

        dialog.ClearSelectionCommand.Execute(null);
        Assert.False(dialog.CanCommit);
        Assert.Equal("登録する商品を選んでください。", dialog.CommitHint);
        Assert.Equal("0 件を登録", dialog.CommitText);

        // キャンセル（既定の答え）なら BOOTH に問い合わせない
        await app.SettleAsync();
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task すべて手元にあれば_登録は押せず_そう言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の上着"));
        await app.AddItemAsync(Make.Item("1000002", "作り物のスカート"));
        var main = await app.StartAsync();

        await main.HandleDropAsync(null, "https://booth.pm/ja/items/1000001 https://booth.pm/ja/items/1000002");

        var dialog = Assert.Single(app.PastedItemDialogs);
        Assert.True(dialog.AllKnown);
        Assert.False(dialog.CanCommit);
        Assert.False(dialog.HasNote);
        Assert.False(dialog.HasBulkSelect);
        Assert.Equal("見つかった 2 件は、すべてライブラリにあります。", dialog.HeadingText);
        Assert.False(dialog.HasCommitHint);
    });

    /// <summary>1件だけのときは今まで通り：窓（一覧）は出さず、登録するかを尋ねる。</summary>
    [Fact]
    public Task 一件だけなら_一覧を出さずに今まで通り尋ねる() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        await main.HandleDropAsync(null, "https://booth.pm/ja/items/1000001",
            html: CfHtml("<a href=\"https://booth.pm/ja/items/1000001\">https://booth.pm/ja/items/1000001</a>"));

        Assert.Empty(app.PastedItemDialogs);
        Assert.Contains(app.Notices, notice => notice.Caption == "BOOTHのURLを受け取りました");
    });

    // ---- 登録 ----

    [Fact]
    public Task 登録を押すと_1件ずつ順に問い合わせ_帯にnとNを出し_済んだら件数で知らせる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の上着");
        app.Booth.HasItem("1000002", "作り物のスカート");
        app.Booth.HasItem("1000003", "作り物のリボン");
        app.AnswerPastedItems = _ => true;
        var main = await app.StartAsync();

        // 2件目の問い合わせで止め、その間に3件目へ進んでいない（並列にしない）ことを見る
        app.Booth.Hold(url => url.Contains("1000002", StringComparison.Ordinal));
        Task drop;
        try
        {
            drop = main.HandleDropAsync(null, ListText, html: ListHtml);
            await UiThread.Until(() => app.Booth.Requests.Any(url => url.Contains("1000002", StringComparison.Ordinal)), "2件目の問い合わせが届く");

            Assert.True(main.IsRegisteringDropped);
            Assert.True(main.CanStopBatchRegistering);
            Assert.Equal("商品を登録しています… 2/3", main.DroppedRegisteringText);
            Assert.DoesNotContain(app.Booth.Requests, url => url.Contains("1000003", StringComparison.Ordinal));
        }
        finally
        {
            app.Booth.Release();
        }

        await drop;
        await app.SettleAsync();

        Assert.False(main.IsRegisteringDropped);
        Assert.False(main.CanStopBatchRegistering);
        foreach (var id in new[] { "1000001", "1000002", "1000003" })
        {
            Assert.NotNull(await app.Store.Items.LoadAsync(id));
        }

        Assert.Equal("3 件の商品を登録しました。", main.RegisteredNoticeText);
        Assert.Equal("最初の商品を開く", main.RegisteredNoticeAction);
        Assert.DoesNotContain(app.Notices, notice => notice.Icon == MessageBoxImage.Warning);
    });

    [Fact]
    public Task 外したチェックの商品は登録しない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の上着");
        app.Booth.HasItem("1000003", "作り物のリボン");
        app.AnswerPastedItems = model =>
        {
            model.Rows[1].IsChecked = false;
            return true;
        };
        var main = await app.StartAsync();

        await main.HandleDropAsync(null, ListText, html: ListHtml);
        await app.SettleAsync();

        Assert.DoesNotContain(app.Booth.Requests, url => url.Contains("1000002", StringComparison.Ordinal));
        Assert.Null(await app.Store.Items.LoadAsync("1000002"));
        Assert.Equal("2 件の商品を登録しました。", main.RegisteredNoticeText);
    });

    [Fact]
    public Task 途中で失敗した物は_最後に1つの窓で_BOOTHに無い物と取れなかった物を分けて言う() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        app.Booth.HasItem("1000001", "作り物の上着");
        app.Booth.IsDown("1000003");
        app.AnswerPastedItems = _ => true;
        var main = await app.StartAsync();

        // 1000002 は教えていないので「無い」（非公開・削除）、1000003 は届かない
        await main.HandleDropAsync(null, ListText, html: ListHtml);
        await app.SettleAsync();

        var warning = Assert.Single(app.Notices, notice => notice.Icon == MessageBoxImage.Warning);
        Assert.Equal("登録できなかった商品があります", warning.Caption);
        Assert.Equal(
            "1 件を登録しました。2 件は登録できませんでした。\n\n"
            + "「BOOTHに見つからない商品」\n商品ID 1000002\nIDが違うか、非公開か削除されています。\n\n"
            + "「取得できなかった商品」\n商品ID 1000003\n通信を確かめて、少し待ってからもう一度貼り付けてください。",
            warning.Text);

        // 登録できた1件は、1件の登録と同じ知らせ
        Assert.Equal("「作り物の上着」を登録しました。", main.RegisteredNoticeText);
    });

    [Fact]
    public Task 中止を押すと_今の1件の後は問い合わせない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の上着");
        app.Booth.HasItem("1000002", "作り物のスカート");
        app.Booth.HasItem("1000003", "作り物のリボン");
        app.AnswerPastedItems = _ => true;
        var main = await app.StartAsync();

        app.Booth.Hold(url => url.Contains("1000001", StringComparison.Ordinal));
        Task drop;
        try
        {
            drop = main.HandleDropAsync(null, ListText, html: ListHtml);
            await UiThread.Until(() => app.Booth.Requests.Count > 0, "1件目の問い合わせが届く");

            main.StopBatchRegisteringCommand.Execute(null);
            Assert.Equal("登録を中止しています…", main.DroppedRegisteringText);
            Assert.False(main.CanStopBatchRegistering);
        }
        finally
        {
            app.Booth.Release();
        }

        await drop;
        await app.SettleAsync();

        Assert.NotNull(await app.Store.Items.LoadAsync("1000001"));
        Assert.DoesNotContain(app.Booth.Requests, url => url.Contains("1000002", StringComparison.Ordinal) || url.Contains("1000003", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Notices, notice => notice.Icon == MessageBoxImage.Warning);
        Assert.Equal("「作り物の上着」を登録しました。", main.RegisteredNoticeText);
    });

    [Fact]
    public void 中止と失敗が重なると_残りの件数も添える()
        => Assert.Equal(
            "選んだ 1 件を登録できませんでした。\n\n「取得できなかった商品」\n商品ID 1000003\n通信を確かめて、少し待ってからもう一度貼り付けてください。\n\n残りの 4 件は中止しました。",
            MainViewModel.BatchFailureText(0, [], ["1000003"], 4));
}
