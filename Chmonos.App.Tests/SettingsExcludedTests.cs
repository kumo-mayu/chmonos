using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「管理対象から除外したファイル」。除外は溜まる一方で上限が無いので、
/// 閉じている間は件数だけを出し、開いたときだけ行を作る（5,000 件で開くたびに 7 秒余り止まっていた。2026-10-01）。
/// 開閉はアプリを閉じるまで覚える（画面をまたいで残る）ので、試験ごとに閉じた形から始めて閉じて終える。
/// </summary>
public class SettingsExcludedTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9));

    private static ExcludedEntry Entry(string hash, string path, int hoursAfterBase, string? reason = null)
        => new() { Hash = hash, Paths = [path], ExcludedAt = Base.AddHours(hoursAfterBase), Reason = reason };

    /// <summary>最後に開いた設定。開閉を閉じた形へ戻すのに使う（試験は並べて走らせない）。</summary>
    private static SettingsViewModel? s_last;

    private static MainViewModel? s_main;

    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app, params ExcludedEntry[] entries)
    {
        await app.Services.Store.Excluded.SaveAsync([.. entries]);
        s_main = await app.StartAsync();
        var settings = await ReopenAsync(app);
        settings.IsExcludedExpanded = false;
        return settings;
    }

    private static async Task<SettingsViewModel> ReopenAsync(TestApp app)
    {
        var settings = new SettingsViewModel(app.Services, s_main!);
        s_last = settings;
        await app.SettleAsync();
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        return settings;
    }

    private static async Task WithClosedAfterAsync(Func<Task> body)
    {
        try
        {
            await body();
        }
        finally
        {
            // 開閉は静的に覚えるので、ほかの試験へ持ち越さない
            if (s_last is not null)
            {
                s_last.IsExcludedExpanded = false;
            }

            s_last = null;
            s_main = null;
        }
    }

    [Fact]
    public Task 閉じている間は_行を作らず_件数だけを出す() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(
            app,
            Entry("AAAA", @"D:\作り物\a.zip", 0),
            Entry("BBBB", @"D:\作り物\b.zip", 1),
            Entry("CCCC", @"D:\作り物\c.zip", 2));

        Assert.False(settings.IsExcludedExpanded);
        Assert.True(settings.HasExcluded);
        Assert.Equal("3 件", settings.ExcludedText);
        Assert.Empty(settings.Excluded);
    }));

    [Fact]
    public Task 除外が無ければ_開く物が無い() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(app);

        Assert.False(settings.HasExcluded);
        Assert.Equal("0 件", settings.ExcludedText);
        Assert.Empty(settings.Excluded);
    }));

    [Fact]
    public Task 開くと_除外した日時の新しい順に並ぶ() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(
            app,
            Entry("OLD", @"D:\作り物\old.zip", 0, reason: "BOOTHの商品ではない"),
            Entry("NEW", @"D:\作り物\new.zip", 5),
            Entry("MID", @"D:\作り物\mid.zip", 2));

        settings.IsExcludedExpanded = true;

        Assert.Equal([@"D:\作り物\new.zip", @"D:\作り物\mid.zip", @"D:\作り物\old.zip"], settings.Excluded.Select(row => row.Label));
        Assert.Equal("BOOTHの商品ではない", settings.Excluded[2].SubText);
        Assert.All(settings.Excluded, row => Assert.NotNull(row.RestoreCommand));
    }));

    [Fact]
    public Task 閉じると_行を捨てる() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(app, Entry("AAAA", @"D:\作り物\a.zip", 0));

        settings.IsExcludedExpanded = true;
        Assert.Single(settings.Excluded);

        settings.IsExcludedExpanded = false;

        Assert.Empty(settings.Excluded);
        Assert.Equal("1 件", settings.ExcludedText);
    }));

    [Fact]
    public Task 開いたまま設定を開き直すと_開いた形で並ぶ() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(app, Entry("AAAA", @"D:\作り物\a.zip", 0), Entry("BBBB", @"D:\作り物\b.zip", 1));
        settings.IsExcludedExpanded = true;

        // 画面を移って戻ると作り直される。開いた形はアプリを閉じるまで覚える（ほかの畳む欄と同じ）
        var reopened = await ReopenAsync(app);

        Assert.True(reopened.IsExcludedExpanded);
        Assert.Equal(["BBBB", "AAAA"], reopened.Excluded.Select(row => row.Key));
    }));

    [Fact]
    public Task 除外を解除すると_その行だけが消え_記録からも消える() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var settings = await OpenSettingsAsync(
            app,
            Entry("AAAA", @"D:\作り物\a.zip", 0),
            Entry("BBBB", @"D:\作り物\b.zip", 1),
            Entry("CCCC", @"D:\作り物\c.zip", 2));
        settings.IsExcludedExpanded = true;
        var kept = settings.Excluded.Where(row => row.Key != "BBBB").ToList();

        settings.Excluded.Single(row => row.Key == "BBBB").RestoreCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => settings.Excluded.Count == 2, "解除した行が消える");

        Assert.Equal("除外を解除しました。元の場所にファイルが見つからないので、未確定には戻していません。", settings.ExcludedNote);
        Assert.False(settings.HasStatus);
        Assert.Equal("2 件", settings.ExcludedText);
        Assert.Equal(["CCCC", "AAAA"], settings.Excluded.Select(row => row.Key));

        // 残った行は作り直さない（一覧の流した位置と、見えている行の部品を保つ）
        Assert.Same(kept[0], settings.Excluded[0]);
        Assert.Same(kept[1], settings.Excluded[1]);
        Assert.Equal(["AAAA", "CCCC"], app.Services.Store.Excluded.Load().Select(entry => entry.Hash).Order(StringComparer.Ordinal));
    }));

    /// <summary>
    /// 解除したら、その場で未確定の一覧に戻り、ナビの件数も増える（ユーザ判断 2026-10-05・file-lifecycle.md「気になった所」10）。
    /// 前は記録を消すだけで、その取り込み元を取り込み直すまでどこにも出なかった。
    /// </summary>
    [Fact]
    public Task 除外を解除すると_ファイルが在ればその場で未確定に戻る() => TestApp.Run(app => WithClosedAfterAsync(async () =>
    {
        var path = app.NewFile(@"除外\作り物.psd");
        var hash = await Core.Scanning.FileHasher.ComputeSha256Async(path);
        var settings = await OpenSettingsAsync(app, Entry(hash, path, 0));
        await UiThread.Until(() => s_main!.UnresolvedCount == 0, "始めは未確定が無い");
        settings.IsExcludedExpanded = true;

        settings.Excluded.Single().RestoreCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => settings.Excluded.Count == 0, "解除した行が消える");

        Assert.Equal("除外を解除しました。未確定の一覧に戻しました。", settings.ExcludedNote);
        Assert.Equal(hash, Assert.Single(app.Services.Store.Unresolved.Load()).Hash);
        await UiThread.Until(() => s_main!.UnresolvedCount == 1, "ナビの未確定の数が増える");
    }));
}
