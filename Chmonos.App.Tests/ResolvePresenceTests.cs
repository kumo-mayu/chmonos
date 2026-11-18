using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の「このファイルについて分かっていること」に、フォルダの札と同じ「ほかに n か所」「見つかりません」を出す（open.md 97・ユーザ判断 2026-11-19 案A）。
/// </summary>
public class ResolvePresenceTests
{
    private static UnresolvedFile Unresolved(string hash, params string[] paths) => new()
    {
        Hash = hash,
        Paths = paths,
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public Task 場所が2つあれば_ほかに1か所と言い_在るので見つからないとは言わない() => TestApp.Run(async app =>
    {
        var first = app.NewFile(@"a\first.zip");
        var second = app.NewFile(@"b\first.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(Make.HashOf(first), first, second)]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);

        Assert.Equal("同じ中身がほかに 1 か所", resolve.SelectedOthersText);
        Assert.False(resolve.HasSelectedPresence);
    });

    [Fact]
    public Task どの場所にも無ければ_見つかりませんと言い_場所が1つならほかの場所は言わない() => TestApp.Run(async app =>
    {
        var gone = System.IO.Path.Combine(app.Root, "gone", "nothing.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(Make.HashOf(gone), gone)]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);

        Assert.Equal("見つかりません", resolve.SelectedPresenceText);
        Assert.False(resolve.HasSelectedOthers);
    });

    [Theory]
    [InlineData(FilePresence.Present, "")]
    [InlineData(FilePresence.Missing, "見つかりません")]
    [InlineData(FilePresence.OnDetachedDrive, "取り外しているドライブ")]
    [InlineData(FilePresence.Unverifiable, "")]
    public void 見た結果の言葉は_フォルダの札と同じ(FilePresence presence, string expected)
        => Assert.Equal(expected, ResolveViewModel.PresenceText(presence));
}
