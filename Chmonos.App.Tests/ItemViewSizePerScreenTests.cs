using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// カードの大きさ・行の高さを画面ごとに覚える（ユーザ指示 2026-11-18「デフォは独立」・判断 2026-11-19）。
/// 設定で切るとすべての画面で同じ値。画面ごとへ切り替えた時は、今の1つの値を全部の画面の最初の値にする。
/// </summary>
public class ItemViewSizePerScreenTests
{
    private static async Task<MainViewModel> StartAsync(TestApp app, Func<AppSettings, AppSettings>? change = null)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.ChangeSettingsAsync(settings => (change ?? (s => s))(settings with { CardWidth = 228, ListRowHeight = 52 }));
        return await app.StartAsync();
    }

    [Fact]
    public Task 画面ごとに大きさを覚え_戻ると前の大きさに戻る() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var size = ItemViewSize.Current;
        size.CardWidth = 300;
        size.ListRowHeight = 80;

        // 覚えていない画面は1つの値で開く
        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(228, CardMetrics.Width);
        Assert.Equal(52, size.ListRowHeight);
        size.CardWidth = 180;

        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(300, CardMetrics.Width);
        Assert.Equal(80, size.ListRowHeight);

        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(180, CardMetrics.Width);
    });

    [Fact]
    public Task スライダーの無い画面を挟んでも_大きさは変わらない() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        ItemViewSize.Current.CardWidth = 300;

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(300, CardMetrics.Width);

        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(300, CardMetrics.Width);
    });

    [Fact]
    public Task 動かしてすぐ別の画面へ移っても_書くのは動かした画面の鍵() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        ItemViewSize.Current.CardWidth = 300;

        // 書くまでの 0.4 秒を待たずに移り、閉じる前の書き出しで書く
        main.ShowSettingsCommand.Execute(null);
        await ItemViewSize.Current.FlushAsync();
        await app.SettleAsync();

        var saved = app.Services.Settings;
        Assert.Equal(300, saved.ItemViewSizes!["search"].CardWidth);
        Assert.False(saved.ItemViewSizes.ContainsKey("settings-hidden"));
        Assert.Equal(228, saved.CardWidth);
    });

    [Fact]
    public Task 起動し直すと画面ごとの大きさを読む() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app, settings => settings with
        {
            ItemViewSizes = new Dictionary<string, ItemViewSizeEntry>
            {
                ["search"] = new() { CardWidth = 300, ListRowHeight = 80 },
            },
        });

        Assert.Equal(300, CardMetrics.Width);
        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(228, CardMetrics.Width);
    });

    [Fact]
    public Task すべて同じにすると_どの画面で変えても全部が同じ大きさ() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app, settings => settings with { ItemViewSizePerScreen = false });
        ItemViewSize.Current.CardWidth = 300;

        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(300, CardMetrics.Width);

        await ItemViewSize.Current.FlushAsync();
        await app.SettleAsync();
        Assert.Equal(300, app.Services.Settings.CardWidth);
        Assert.Null(app.Services.Settings.ItemViewSizes);
    });

    [Fact]
    public Task 画面ごとへ切り替えると_今の値を全部の画面の最初の値にする() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app, settings => settings with
        {
            ItemViewSizePerScreen = false,
            CardWidth = 260,
            ItemViewSizes = new Dictionary<string, ItemViewSizeEntry>
            {
                ["settings-hidden"] = new() { CardWidth = 180, ListRowHeight = 52 },
            },
        });
        var size = ItemViewSize.Current;

        size.PerScreen = true;
        await app.SettleAsync();

        // 前に覚えていた画面ごとの値は捨て、今の値（260）で開く
        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(260, CardMetrics.Width);
        Assert.True(app.Services.Settings.ItemViewSizePerScreen);
        Assert.Null(app.Services.Settings.ItemViewSizes);
    });

    [Fact]
    public Task すべて同じへ切り替えると_前から持つ1つの値に戻り_画面ごとの値は消さない() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var size = ItemViewSize.Current;
        size.CardWidth = 300;

        size.PerScreen = false;
        await app.SettleAsync();

        Assert.Equal(228, CardMetrics.Width);
        Assert.False(app.Services.Settings.ItemViewSizePerScreen);
        Assert.Equal(300, app.Services.Settings.ItemViewSizes!["search"].CardWidth);
    });

    [Fact]
    public Task 設定を既定に戻すと_画面ごとの大きさも消す() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app, settings => settings with
        {
            ItemViewSizePerScreen = false,
            ItemViewSizes = new Dictionary<string, ItemViewSizeEntry>
            {
                ["search"] = new() { CardWidth = 300, ListRowHeight = 80 },
            },
        });

        // 1つの値だけ戻すと、覚えた画面が戻らず「戻したのに大きいまま」になる
        ItemViewSize.Current.ResetToDefaults(new AppSettings());
        await app.SettleAsync();

        Assert.True(ItemViewSize.Current.PerScreen);
        Assert.Null(app.Services.Settings.ItemViewSizes);
        main.ShowSettingsCommand.Execute(null);
        await app.SettleAsync();
        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(228, CardMetrics.Width);
    });
}
