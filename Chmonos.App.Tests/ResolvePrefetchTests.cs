using System.IO;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の自動検索の先回りと控え（ユーザ判断 2026-11-10・2026-11-11・plan 21）。作り物の BOOTH を相手にし、本物へは通信しない。
/// 控えがあれば待たずに出してボタンは「再検索」・押せば控えを置き換える・控えの無い対象で押した結果は控えない・
/// 片付けたら控えも消える・既定はオフで、入れると未確定の新しい順に引く。
/// </summary>
public class ResolvePrefetchTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static UnresolvedFile Unresolved(string path, int day = 0) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = Day,
        FirstSeenAt = Day.AddDays(day),
    };

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveAsync(TestApp app)
    {
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        return (main, Assert.IsType<ResolveViewModel>(main.CurrentViewModel));
    }

    private static ResolutionProposal Found(string id) => new([new ResolutionCandidate { ItemId = id, Name = "作り物の衣装", Score = 9 }], false);

    [Fact]
    public Task 控えがあれば待たずに候補を出し_ボタンは再検索になる() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\作り物の衣装.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path)]);
        await app.Services.CandidateSearches.AddIfMissingAsync(path, [Make.HashOf(path)], Found("100"), Day);

        var (_, resolve) = await OpenResolveAsync(app);

        Assert.Equal(0, app.Booth.SearchCount);
        Assert.True(resolve.HasSearched);
        Assert.Equal("100", Assert.Single(resolve.Candidates, row => row.ItemId == "100").ItemId);
        Assert.Equal("再検索", resolve.ProposeButtonText);
        Assert.Equal("11月1日に検索した候補を 1 件表示しています。", resolve.StatusText);
        Assert.Equal("BOOTHで検索し直し、候補を新しい結果に置き換えます。", resolve.ProposeHint);
    });

    [Fact]
    public Task 再検索を押すと引き直して控えを置き換える() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\作り物の衣装.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path)]);
        await app.Services.CandidateSearches.AddIfMissingAsync(path, [Make.HashOf(path)], Found("100"), Day);
        app.Booth.HasItem("500", "作り物の衣装");
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(path), "500");
        var (_, resolve) = await OpenResolveAsync(app);

        resolve.ProposeCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(app.Booth.SearchCount > 0);
        var entry = Assert.IsType<CandidateSearchEntry>(app.Services.CandidateSearches.Find(path));
        Assert.True(entry.SearchedAt > Day);
        Assert.Equal("500", Assert.Single(entry.Candidates).ItemId);
        Assert.DoesNotContain(resolve.Candidates, row => row.ItemId == "100");
        Assert.Contains(resolve.Candidates, row => row.ItemId == "500");
        Assert.Equal("再検索", resolve.ProposeButtonText);
    });

    [Fact]
    public Task 控えの無い対象で押した結果は控えず_ボタンは自動検索のまま() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\作り物の衣装.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path)]);
        app.Booth.HasItem("500", "作り物の衣装");
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(path), "500");
        var (_, resolve) = await OpenResolveAsync(app);
        Assert.Equal("自動検索", resolve.ProposeButtonText);

        resolve.ProposeCommand.Execute(null);
        await app.SettleAsync();

        Assert.Contains(resolve.Candidates, row => row.ItemId == "500");
        Assert.True(resolve.HasSearched);
        Assert.Null(app.Services.CandidateSearches.Find(path));
        Assert.Equal("自動検索", resolve.ProposeButtonText);
    });

    [Fact]
    public Task 除外して片付けたら_そのファイルの控えも消える() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\作り物の衣装.zip");
        var other = app.NewFile(@"dl2\作り物の靴.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path), Unresolved(other)]);
        await app.Services.CandidateSearches.AddIfMissingAsync(path, [Make.HashOf(path)], Found("100"), Day);
        await app.Services.CandidateSearches.AddIfMissingAsync(other, [Make.HashOf(other)], Found("200"), Day);
        var (_, resolve) = await OpenResolveAsync(app);

        resolve.Selected = resolve.Files.Single(row => row.FileName == "作り物の衣装.zip");
        app.Answer = _ => MessageBoxResult.OK;
        resolve.ExcludeCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => app.Services.CandidateSearches.Find(path) is null, "除外したファイルの控えが消える");

        Assert.NotNull(app.Services.CandidateSearches.Find(other));
    });

    [Fact]
    public Task 先回りは既定でオフで_入れると未確定の新しい順に引いて控え_切ると止まる() => TestApp.Run(async app =>
    {
        var older = app.NewFile(@"a\SampleHat.zip");
        var newer = app.NewFile(@"b\SampleShoes.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(older, day: 1), Unresolved(newer, day: 5)]);
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(older));
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(newer));

        await app.StartAsync();
        Assert.False(app.Services.Settings.PrefetchCandidateSearch);
        Assert.Null(app.Services.Startup.PrefetchRunning);
        Assert.Equal(0, app.Booth.SearchCount);

        await app.ChangeSettingsAsync(settings => settings with { PrefetchCandidateSearch = true });
        var running = Assert.IsAssignableFrom<Task>(app.Services.Startup.PrefetchRunning);
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(app.Services.CandidateSearches.Find(older));
        Assert.NotNull(app.Services.CandidateSearches.Find(newer));
        var searches = app.Booth.Requests.Where(url => url.StartsWith("https://booth.pm/ja/search/", StringComparison.Ordinal)).ToList();
        var shoes = searches.FindIndex(url => url.Contains("Shoes", StringComparison.OrdinalIgnoreCase));
        var hat = searches.FindIndex(url => url.Contains("Hat", StringComparison.OrdinalIgnoreCase));
        Assert.True(shoes >= 0 && hat >= 0 && shoes < hat, $"新しい方から引く（{string.Join(", ", searches)}）");

        // 切った後に増えた未確定は引かない
        await app.ChangeSettingsAsync(settings => settings with { PrefetchCandidateSearch = false });
        var asked = app.Booth.SearchCount;
        await app.Store.Unresolved.SaveAsync([.. app.Store.Unresolved.Load(), Unresolved(app.NewFile(@"c\SampleBag.zip"), day: 9)]);
        await app.ChangeSettingsAsync(settings => settings with { ShowAdult = !settings.ShowAdult });
        await UiThread.Settle();
        Assert.Equal(asked, app.Booth.SearchCount);
    });

    [Fact]
    public Task 起動したときは裏の取得の後に始める() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\SampleHat.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path)]);
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(path));
        await app.ChangeSettingsAsync(settings => settings with { ResumeFetchInBackground = true, PrefetchCandidateSearch = true });

        await app.StartAsync();
        await UiThread.Until(() => app.Services.Startup.PrefetchRunning is { IsCompleted: true }, "裏の取得の後に先回りが走り終える");

        Assert.NotNull(app.Services.CandidateSearches.Find(path));
    });

    [Fact]
    public Task 起動したとき裏で取得を始める設定がオフなら_起動したときは始めない() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"dl\SampleHat.zip");
        await app.Store.Unresolved.SaveAsync([Unresolved(path)]);
        app.Booth.SearchFinds(FileNameQuery.ToSearchQuery(path));
        await app.ChangeSettingsAsync(settings => settings with { PrefetchCandidateSearch = true });

        await app.StartAsync();

        Assert.Null(app.Services.Startup.PrefetchRunning);
        Assert.Equal(0, app.Booth.SearchCount);
    });

    /// <summary>止める・続けるの決まりは Core の試験（CandidatePrefetchTests）。ここは取り込みの係の知らせがつながっていること</summary>
    [Fact]
    public Task 取り込みの間は先回りを止め_終わったら続ける() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var folder = Path.GetDirectoryName(app.NewFile(@"in\作り物の衣装.psd"))!;
        bool? pausedWhileImporting = null;
        app.Services.Imports.OnEnded(_ =>
        {
            pausedWhileImporting = app.Services.Startup.Prefetch.IsPaused;
            return Task.CompletedTask;
        });

        Assert.False(app.Services.Startup.Prefetch.IsPaused);
        await app.Services.Imports.StartOrStackAsync(new ImportRequest(() => [folder], [folder]));
        await UiThread.Until(() => pausedWhileImporting is not null && app.Services.Imports.Running is null, "取り込みが終わる");
        await app.SettleAsync();

        Assert.True(pausedWhileImporting);
        Assert.False(app.Services.Startup.Prefetch.IsPaused);
    });

    [Fact]
    public void 先回りの対象と持ち主は画面と同じ決め方で_zipの中身は束ねて1件()
    {
        var zip = @"D:\dl\作り物の衣装.zip";
        UnresolvedFile Content(string path, int day) => new()
        {
            Hash = Make.HashOf(path),
            Paths = [path],
            SizeBytes = 3,
            ModifiedAtUtc = Day,
            FirstSeenAt = Day.AddDays(day),
            ZoneReferrerUrl = zip,
        };

        var rows = ResolveViewModel.BuildRows(
            [Content(@"D:\dl\作り物の衣装\body.psd", 1), Content(@"D:\dl\作り物の衣装\tex.png", 4), Unresolved(@"D:\dl\作り物の靴.unitypackage", 2)],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            []).Rows;

        var (targets, _) = StartupWork.PrefetchTargets(rows);

        Assert.Equal(2, targets.Count);
        var bundle = Assert.Single(targets, target => target.Target == zip);
        Assert.Equal(Day.AddDays(4), bundle.NewestSeenAt);
        foreach (var row in rows)
        {
            var target = Assert.Single(targets, entry => entry.Target == ResolveViewModel.SearchTargetFor(row));
            Assert.Equal(
                ResolveViewModel.SearchOwnersIn(row, rows).Order(StringComparer.OrdinalIgnoreCase),
                target.Files.Order(StringComparer.OrdinalIgnoreCase));
        }
    }
}
