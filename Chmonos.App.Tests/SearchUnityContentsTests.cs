using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「Unityプロジェクト」（プロジェクトの中に入っている商品）と「開いているUnityの中身を表示」。
/// 調べる所は差し替える（実際の照らし方は <c>UnityProjectMatcherTests</c>）。作り物の商品だけを使う。
/// </summary>
public class SearchUnityContentsTests
{
    private static ItemRecord Owned(string id) => Make.Item(id, $"商品{id}");

    private static IReadOnlyList<string> Shown(SearchViewModel search)
        => search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal).ToList();

    private static string NewProject(TestApp app, string name)
    {
        var path = Path.Combine(app.Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<SearchViewModel> StartWithItemsAsync(TestApp app, params string[] ids)
    {
        foreach (var id in ids)
        {
            await app.AddItemAsync(Owned(id));
        }

        return (await app.StartAsync()).Search;
    }

    private static void ScanFinds(TestApp app, params string[] ids)
        => app.Services.ScanUnityProject = (_, _, _, _) => ids.Select(id => new UnityProjectMatch(id, 1, 2)).ToList();

    [Fact]
    public Task 押すと_開いているプロジェクトに入っている商品だけが出る() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900002");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];

        search.ShowOpenUnityContentsCommand.Execute(null);

        await UiThread.Until(() => Shown(search) is ["9900002"], "待つ");
        var module = Assert.IsType<ListModule>(search.Modules.Single(entry => entry.Kind == SearchModuleKind.UnityProjectContents));
        var chip = module.Chips.Single();
        Assert.Equal(UnityProjectPath.Normalize(project), chip.Key);

        // 札と要約は名前だけで、場所は吹き出し（場所まで出すと長く、間を省くと名前が読めない）
        Assert.Equal("projA", chip.DisplayText);
        Assert.Equal(chip.Key, chip.ToolTipText);
        Assert.True(module.HasReload);
        Assert.False(module.HasStatus);
    });

    [Fact]
    public Task 押すと_前のプロジェクトの札を置き換え_切った条件も効かせる() => TestApp.Run(async app =>
    {
        var before = NewProject(app, "projA");
        var open = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        app.Services.ScanUnityProject = (project, _, _, _) =>
            [new UnityProjectMatch(project == UnityProjectPath.Normalize(open) ? "9900002" : "9900001", 1, 1)];
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.AddKey(UnityProjectPath.Normalize(before)!);
        module.IsExcluded = true;
        module.IsEnabled = false;
        module.IsCollapsed = true;
        app.OpenEditors = [new OpenUnityEditor(1, "projB", open)];

        search.ShowOpenUnityContentsCommand.Execute(null);

        await UiThread.Until(() => Shown(search) is ["9900002"], "待つ");
        Assert.Same(module, search.Modules.Single(entry => entry.Kind == SearchModuleKind.UnityProjectContents));
        Assert.Equal(UnityProjectPath.Normalize(open), module.Chips.Single().Key);
        Assert.True(module.IsEnabled);
        Assert.False(module.IsExcluded);
        Assert.False(module.IsCollapsed);
    });

    [Fact]
    public Task Unityが開いていなければ_言うだけで条件を足さない() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        var count = search.Modules.Count;

        search.ShowOpenUnityContentsCommand.Execute(null);

        await UiThread.Until(() => app.Notices.Count > 0, "知らせが出る");
        var notice = app.Notices.Last();
        Assert.Contains("Unityが開いていません", notice.Text);
        Assert.Equal(count, search.Modules.Count);
    });

    [Fact]
    public Task 複数開いていれば選ばせ_選ばなければ何もしない() => TestApp.Run(async app =>
    {
        var chosen = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900001");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", NewProject(app, "projA")), new OpenUnityEditor(2, "projB", chosen)];
        var asked = 0;
        app.Services.ChooseUnityEditor = (_, editors) =>
        {
            asked++;
            return asked == 1 ? null : editors[1];
        };

        search.ShowOpenUnityContentsCommand.Execute(null);
        await UiThread.Until(() => asked == 1, "待つ");
        await app.SettleAsync();
        Assert.DoesNotContain(search.Modules, module => module.Kind == SearchModuleKind.UnityProjectContents);

        search.ShowOpenUnityContentsCommand.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900001"], "待つ");
        var chip = ((ListModule)search.Modules.Single(module => module.Kind == SearchModuleKind.UnityProjectContents)).Chips.Single();
        Assert.Equal(UnityProjectPath.Normalize(chosen), chip.Key);
    });

    [Fact]
    public Task 場所の分からないプロジェクトは_調べられないと言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", null, IsAmbiguous: true)];

        search.ShowOpenUnityContentsCommand.Execute(null);

        await UiThread.Until(() => app.Notices.Count > 0, "知らせが出る");
        var notice = app.Notices.Last();
        Assert.Contains("「projA」の中を調べられません", notice.Text);
    });

    [Fact]
    public Task メニューから足すと_開いているUnityが1つならそれが入り_複数なら入らない() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001");
        ScanFinds(app);
        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];

        var one = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        await UiThread.Until(() => one.Chips.Count == 1, "待つ");
        Assert.Equal(UnityProjectPath.Normalize(project), one.Chips.Single().Key);

        app.OpenEditors = [new OpenUnityEditor(1, "projA", project), new OpenUnityEditor(2, "projB", NewProject(app, "projB"))];
        var two = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        await app.SettleAsync();
        Assert.Empty(two.Chips);
    });

    [Fact]
    public Task 候補は実在するプロジェクトだけで_開いている物が先に目印付きで並ぶ() => TestApp.Run(async app =>
    {
        app.UnityProjects =
        [
            new UnityProjectCandidate { Path = @"D:\p\closed", Name = "closed", Folder = @"D:\p", Exists = true, IsOpen = false, Source = UnityProjectSource.Hub },
            new UnityProjectCandidate { Path = @"D:\p\gone", Name = "gone", Folder = @"D:\p", Exists = false, IsOpen = false, Source = UnityProjectSource.Hub },
            new UnityProjectCandidate { Path = @"D:\p\open", Name = "open", Folder = @"D:\p", Exists = true, IsOpen = true, Source = UnityProjectSource.Hub },
        ];
        var search = await StartWithItemsAsync(app, "9900001");

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        await UiThread.Until(() => module.Suggestions.Count > 0, "待つ");
        Assert.Equal([@"open（開いています・D:\p\open）", @"closed（D:\p\closed）"], module.Suggestions);
    });

    [Fact]
    public Task 調べている間は進み具合を出し_結果は覚え_読み直すで取り直す() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        var gate = new TaskCompletionSource();
        var scans = 0;
        var found = "9900001";
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            scans++;
            gate.Task.Wait();
            return [new UnityProjectMatch(found, 1, 1)];
        };

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.AddKey(UnityProjectPath.Normalize(project)!);

        await UiThread.Until(() => module.StatusText.Contains("「projA」の中を調べています"), "待つ");
        gate.SetResult();
        await UiThread.Until(() => Shown(search) is ["9900001"], "待つ");
        Assert.False(module.HasStatus);

        // 関係の無い絞り直しでは調べ直さない
        module.MatchAll = true;
        await app.SettleAsync();
        Assert.Equal(1, scans);

        found = "9900002";
        module.ReloadCommand!.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900002"], "待つ");
        Assert.Equal(2, scans);
    });

    [Fact]
    public Task フォルダが無いプロジェクトは_理由を出す() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        module.AddKey(Path.Combine(app.Root, "gone"));

        await UiThread.Until(() => module.StatusText == "「gone」のフォルダが見つかりません。", "待つ");
        Assert.Empty(Shown(search));
    });

    [Fact]
    public Task 改変のUnityプロジェクトは_今までの意味のまま別の条件で残る() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        Assert.Equal("改変のUnityプロジェクト", SearchModuleCatalog.Of(SearchModuleKind.UnityProject).Label);
        Assert.Equal("Unityプロジェクト", SearchModuleCatalog.Of(SearchModuleKind.UnityProjectContents).Label);
        var old = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProject);
        Assert.False(old.HasReload);
    });
}
