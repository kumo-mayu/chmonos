using System.Diagnostics;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 「BOOTHで開く」は、手で直せる JSON の URL や実行ファイルの場所を外のアプリへ渡さない（2026-10-06 外部の点検・L106）。
/// 外のアプリを起こす所（<see cref="Shell.StartOverride"/>）を差し替えて、何が渡されたかを見る。本物のブラウザは開かない。
/// </summary>
public class ExternalLinkGuardTests
{
    private const string Calc = @"C:\Windows\System32\calc.exe";

    private static List<ProcessStartInfo> CaptureStarts()
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        return started;
    }

    [Theory]
    [InlineData(Calc)]
    [InlineData("calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"\\server\share\run.exe")]
    [InlineData("ms-settings:")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("vrchat://launch")]
    [InlineData("")]
    [InlineData(null)]
    public void http_https以外は開かない(string? url)
    {
        var started = CaptureStarts();

        Assert.False(Shell.OpenUrl(url));
        Assert.Empty(started);
    }

    [Theory]
    [InlineData("https://booth.pm/ja/items/9900001")]
    [InlineData("http://example.com/page")]
    public void http_httpsは解釈し直した形で開く(string url)
    {
        var started = CaptureStarts();

        Assert.True(Shell.OpenUrl(url));
        Assert.Equal(new Uri(url).AbsoluteUri, Assert.Single(started).FileName);
    }

    [Fact]
    public Task 商品ページは記録のURLではなく商品IDから作る() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900001", "作り物の衣装");
        await app.AddItemAsync(item with { Booth = item.Booth with { Url = Calc } });
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();
        var started = CaptureStarts();

        main.Search.OpenBooth(card);

        Assert.Equal("https://booth.pm/ja/items/9900001", Assert.Single(started).FileName);
    });

    [Fact]
    public Task ショップのURLがBOOTHのホストでなければ開かない() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900002", "作り物の髪型", shop: "sample-shop");
        await app.AddItemAsync(item with
        {
            Booth = item.Booth with
            {
                Shop = new BoothShop { Name = "sample-shop", Subdomain = "sample-shop", Url = "https://sample-shop.booth.pm.example.com/" },
            },
        });
        var main = await app.StartAsync();
        main.ShowShopsCommand.Execute(null);
        var shops = Assert.IsType<ShopsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shops.IsListReady && !shops.IsLoading, "ショップ一覧の読み込みが済む");
        var card = shops.Rows.SelectMany(row => row.Cards).Single();
        var started = CaptureStarts();

        Assert.False(card.OpenBoothCommand!.CanExecute(null));
        card.OpenBoothCommand.Execute(null);

        Assert.Empty(started);
    });
}
