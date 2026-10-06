using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// BOOTHで見つからなくなった商品の新しいIDを探す（ユーザ判断 2026-10-06）。
/// 商品ページの「同じ商品をBOOTHで探す」→「IDを変える」の窓の自動検索。BOOTH は作り物。
/// </summary>
public sealed class ChangeIdSearchTests
{
    private const string OldId = "9900101";
    private const string NewId = "9900102";
    private const string OtherShopId = "9900103";

    private static ItemRecord Delisted(string id = OldId, bool everFetched = true)
    {
        var item = Make.Item(id, "作り物のコート").WithFiles(Make.File(@"D:\files\Fake_Coat_v1.zip"));
        return item with
        {
            Booth = item.Booth with { FetchedAt = everFetched ? DateTimeOffset.Now.AddDays(-40) : null },
            Local = item.Local with { IsDelisted = true },
        };
    }

    private static async Task<ItemViewModel> OpenAsync(TestApp app, ItemRecord item)
    {
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync(item.Id))!);
        return Assert.IsType<ItemViewModel>(main.CurrentViewModel);
    }

    /// <summary>ボタンを押して窓を受け取り、自動検索が終わるまで待つ。窓は「キャンセル」で閉じた扱い（窓の VM は残る）。</summary>
    private static async Task<ChangeItemIdDialogViewModel> PressFindAsync(TestApp app, ItemViewModel page)
    {
        Assert.True(page.FindOnBoothCommand.CanExecute(null));
        page.FindOnBoothCommand.Execute(null);
        await UiThread.Until(() => app.ChangeIdDialogs.Count == 1, "「IDを変える」の窓");
        var dialog = app.ChangeIdDialogs[0];
        await UiThread.Until(() => dialog.HasSearched || dialog.HasStatus, "自動検索の終わり");
        await app.SettleAsync();
        return dialog;
    }

    [Fact]
    public Task 見つからない商品のボタンは_IDを変える窓を開いて自動検索を始め_同じショップの候補に札を付ける() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(NewId, "作り物のコート", images: 1);
        app.Booth.HasItem(OtherShopId, "作り物のコート 別の店", shop: "other-shop");
        app.Booth.SearchFinds("作り物のコート", OldId, OtherShopId, NewId);
        var page = await OpenAsync(app, Delisted());

        var dialog = await PressFindAsync(app, page);

        // 今のIDの商品は出さない
        Assert.DoesNotContain(dialog.Candidates, row => row.ItemId == OldId);
        Assert.True(dialog.Candidates.Single(row => row.ItemId == NewId).IsSameShop);
        Assert.False(dialog.Candidates.Single(row => row.ItemId == OtherShopId).IsSameShop);
        Assert.Equal("前の商品名で見つかりました", dialog.Candidates[0].FoundByText);

        // 名前で当たったので、ファイル名では引かない
        Assert.Equal(1, app.Booth.SearchCount);
    });

    /// <summary>外れると困る所：選んでも移さない。欄に入れて下見を出すだけ。</summary>
    [Fact]
    public Task 候補を選ぶと_IDの欄に入って下見が出るだけで_商品は移らない() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(NewId, "作り物のコート");
        app.Booth.SearchFinds("作り物のコート", NewId);
        var page = await OpenAsync(app, Delisted());
        var dialog = await PressFindAsync(app, page);

        dialog.UseCandidateCommand.Execute(dialog.Candidates.Single());
        await UiThread.Until(() => dialog.HasPlan, "下見");
        await app.SettleAsync();

        Assert.Equal(NewId, dialog.IdInput);
        Assert.Equal(NewId, dialog.Plan!.ToId);
        Assert.NotNull(await app.Store.Items.LoadAsync(OldId));
        Assert.Null(await app.Store.Items.LoadAsync(NewId));
    });

    [Fact]
    public Task 名前で見つからなければ_ファイル名で探す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(NewId, "Fake Coat 冬");
        app.Booth.SearchFinds("作り物のコート");
        app.Booth.SearchFinds("Fake Coat", NewId);
        var page = await OpenAsync(app, Delisted());

        var dialog = await PressFindAsync(app, page);

        var row = Assert.Single(dialog.Candidates);
        Assert.Equal(NewId, row.ItemId);
        Assert.Equal("「Fake_Coat_v1.zip」の名前で見つかりました", row.FoundByText);
        Assert.Equal(2, app.Booth.SearchCount);
    });

    [Fact]
    public Task 候補が0件なら_次にやることを言う() => TestApp.Run(async app =>
    {
        app.Booth.SearchFinds("作り物のコート");
        app.Booth.SearchFinds("Fake Coat");
        var page = await OpenAsync(app, Delisted());

        var dialog = await PressFindAsync(app, page);

        Assert.True(dialog.ShowsNoCandidates);
        Assert.Contains("IDかURLを上の欄に入れてください", dialog.NoCandidatesText);
    });

    [Fact]
    public Task 一度も取れていない商品は_そう言う() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Delisted(everFetched: false));

        Assert.True(page.ShowsNotFoundOnBooth);
        Assert.Contains("一度も情報を取得できていません", page.NotFoundOnBoothNotice);
    });

    [Fact]
    public Task 仮IDの商品と_BOOTHにある商品には_探すボタンを出さない() => TestApp.Run(async app =>
    {
        var local = Make.Item(LocalItemId.For(Make.HashOf("x")), "作り物の手元だけの商品") with { Booth = new BoothBlock() };
        await app.AddItemAsync(local with { Local = local.Local with { IsDelisted = true } });
        await app.AddItemAsync(Make.Item(NewId, "作り物の公開中の商品"));
        var main = await app.StartAsync();

        foreach (var id in new[] { local.Id, NewId })
        {
            main.ShowItem((await app.Store.Items.LoadAsync(id))!);
            var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
            Assert.False(page.ShowsNotFoundOnBooth);
            Assert.False(page.FindOnBoothCommand.CanExecute(null));
        }

        Assert.DoesNotContain(app.Booth.Requests, url => url.Contains("/search/", StringComparison.Ordinal));
    });
}
