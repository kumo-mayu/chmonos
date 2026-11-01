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

    private static UnityProjectScan Packages(params string[] ids)
        => new(ids.Select(id => new UnityProjectMatch(id, 1, 2)).ToList(), new Dictionary<string, IReadOnlyList<UnityImageMatch>>());

    private static void ScanFinds(TestApp app, params string[] ids)
        => app.Services.ScanUnityProject = (_, _, _, _) => Packages(ids);

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
        Assert.True(module.IncludeOn);
        Assert.Empty(module.Chips);
        Assert.False(module.HasStatus);
    });

    [Fact]
    public Task 札は名前だけで_場所は吹き出し() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001");
        ScanFinds(app, "9900001");

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.AddKey(UnityProjectPath.Normalize(project)!);

        var chip = module.Chips.Single();
        Assert.Equal("projA", chip.DisplayText);
        Assert.Equal(chip.Key, chip.ToolTipText);
        Assert.True(module.HasReload);
        await UiThread.Until(() => Shown(search) is ["9900001"], "待つ");
    });

    [Fact]
    public Task Unityが開いていない間は_今開いているプロジェクトでは絞らず_そう言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        await UiThread.Until(() => module.StatusText == "Unityが開いていません。", "待つ");
        Assert.True(module.IncludeOn);
        Assert.False(module.IsActive);
        Assert.Equal(["9900001"], Shown(search));
    });

    [Fact]
    public Task Unityを開いて窓に戻ると_今開いているプロジェクトで絞り直す() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900002");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        await UiThread.Until(() => module.StatusText.Length > 0, "開いていないと言う");

        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];
        search.NoteOpenProjectsMaybeChanged();

        await UiThread.Until(() => Shown(search) is ["9900002"], "絞り直す");
        Assert.True(module.IsActive);
        Assert.False(module.HasStatus);
    });

    [Fact]
    public Task 名前が同じ画像は_切り替えを入れたときだけ出る() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        app.Services.ScanUnityProject = (_, _, _, _) => new UnityProjectScan([], new Dictionary<string, IReadOnlyList<UnityImageMatch>>
        {
            ["9900001"] = [new UnityImageMatch("Assets/a.png", "a.png", UnityImageMatchKind.SameImage)],
            ["9900002"] = [new UnityImageMatch("Assets/b.png", "b.png", UnityImageMatchKind.SameName)],
        });
        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        await UiThread.Until(() => Shown(search) is ["9900001"], "同じ画像だけ");
        Assert.True(module.HasFlag);
        module.Flag = true;

        Assert.Equal(["9900001", "9900002"], Shown(search));
    });

    [Fact]
    public Task 開いているプロジェクトが並び_押すと札に入って一覧から消える() => TestApp.Run(async app =>
    {
        var first = NewProject(app, "projA");
        var second = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        app.Services.ScanUnityProject = (project, _, _, _) => Packages(project == UnityProjectPath.Normalize(first) ? "9900001" : "9900002");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", first), new OpenUnityEditor(2, "projB", second)];
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        await UiThread.Until(() => module.OpenProjectRows.Count == 2, "2つ並ぶ");
        await UiThread.Until(() => Shown(search) is ["9900001", "9900002"], "開いている全部が対象");
        Assert.Equal(["projA", "projB"], module.OpenProjectRows.Select(row => row.Name));

        module.OpenProjectRows[0].AddCommand.Execute(null);

        // 押した物だけに絞られる（入れたままだと開いている全部が対象で、押した効き目が見えない）
        await UiThread.Until(() => Shown(search) is ["9900001"], "押した物だけ");
        Assert.False(module.IncludeOn);
        Assert.Equal("projA", module.Chips.Single().DisplayText);
        Assert.Equal(["projB"], module.OpenProjectRows.Select(row => row.Name));

        module.OpenProjectRows[0].AddCommand.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900001", "9900002"], "2つ目も足せる");
        Assert.Empty(module.OpenProjectRows);
        Assert.False(module.HasOpenProjectRows);
    });

    [Fact]
    public Task Unityが開いていなければ_一覧は無く_切っていても開いていないと言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.IncludeOn = false;

        await UiThread.Until(() => module.StatusText == "Unityが開いていません。", "待つ");
        Assert.False(module.HasOpenProjectRows);
        Assert.True(module.HasReload);

        // 札もチェックも無くても、切り替えは隠さない（出たり消えたりすると下の欄が縦に揺れる）
        Assert.Empty(module.Chips);
        Assert.True(module.HasFlag);
    });

    [Fact]
    public void 画像の行は_見つかり方を頭に付ける()
    {
        Assert.Equal("同じ画像：Assets/a.png", ItemUnityActions.ImageRowText(new UnityImageMatch("Assets/a.png", "a.png", UnityImageMatchKind.SameImage)));
        Assert.Equal("名前が同じ：Assets/a.png", ItemUnityActions.ImageRowText(new UnityImageMatch("Assets/a.png", "a.png", UnityImageMatchKind.SameName)));
    }

    [Theory]
    [InlineData("Assets/手で置いた/a b.png", "a:assets glob:\"Assets/手で置いた/a b.png\"")]
    [InlineData("Assets/[作者]/x{1}.png", "a:assets glob:\"Assets/?作者?/x?1?.png\"")]
    public void 画像の検索の語は_パスで1件に絞り_記号は1字に当てる(string path, string expected)
        => Assert.Equal(expected, UnityProjectTab.ImageQuery(path));

    [Fact]
    public void 画像の持ち方で_選択できるかが決まる()
    {
        var plain = Make.Item("9900001", "画像なし").WithFiles(Make.File(@"D:\files\a.zip") with { Contents = ["説明.txt"] });
        var inZip = Make.Item("9900002", "zip の中").WithFiles(Make.File(@"D:\files\b.zip") with { Contents = ["tex/a.psd"] });
        var single = Make.Item("9900003", "単体").WithFiles(Make.File(@"D:\files\c.png"));

        Assert.False(CardMenuState.HasSelectableImage(plain));
        Assert.True(CardMenuState.HasSelectableImage(inZip));
        Assert.True(CardMenuState.HasSelectableImage(single));
    }

    [Fact]
    public Task 押すと_前のプロジェクトの札を置き換え_切った条件も効かせる() => TestApp.Run(async app =>
    {
        var before = NewProject(app, "projA");
        var open = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        app.Services.ScanUnityProject = (project, _, _, _) =>
            Packages(project == UnityProjectPath.Normalize(open) ? "9900002" : "9900001");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.IncludeOn = false;
        module.AddKey(UnityProjectPath.Normalize(before)!);
        module.IsExcluded = true;
        module.IsEnabled = false;
        module.IsCollapsed = true;
        app.OpenEditors = [new OpenUnityEditor(1, "projB", open)];

        search.ShowOpenUnityContentsCommand.Execute(null);

        await UiThread.Until(() => Shown(search) is ["9900002"], "待つ");
        Assert.Same(module, search.Modules.Single(entry => entry.Kind == SearchModuleKind.UnityProjectContents));
        Assert.Empty(module.Chips);
        Assert.True(module.IncludeOn);
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
        using var gate = new ManualResetEventSlim();
        var scans = 0;
        var found = "9900001";
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            scans++;
            gate.Wait();
            return Packages(found);
        };

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.IncludeOn = false;
        module.AddKey(UnityProjectPath.Normalize(project)!);

        await UiThread.Until(() => module.StatusText.Contains("「projA」の中を調べています"), "待つ");
        gate.Set();
        await UiThread.Until(() => Shown(search) is ["9900001"], "待つ");
        Assert.DoesNotContain("調べています", module.StatusText);

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

        module.IncludeOn = false;
        module.AddKey(Path.Combine(app.Root, "gone"));

        await UiThread.Until(() => module.StatusText.Contains("「gone」のフォルダが見つかりません。"), "待つ");
        Assert.Empty(Shown(search));
    });

    [Fact]
    public Task 改変プロジェクトは_今までの意味のまま別の条件で残る() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        Assert.Equal("改変プロジェクト", SearchModuleCatalog.Of(SearchModuleKind.UnityProject).Label);
        Assert.Equal("Unityプロジェクト", SearchModuleCatalog.Of(SearchModuleKind.UnityProjectContents).Label);
        var old = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProject);
        Assert.False(old.HasReload);
    });
}
