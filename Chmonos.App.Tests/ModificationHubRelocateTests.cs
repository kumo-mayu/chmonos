using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面の「場所を付け替える…」（ユーザ判断 2026-11-10）。Unity プロジェクトのフォルダを移した・名前を変えたとき、
/// 改変の紐付け・調べた控え・保存した検索・今の検索の条件を、古い場所から新しい場所へ書き換える。
/// フォルダを選ぶ窓は出さず、<see cref="TestApp.PickProjectFolder"/> が答える。
/// </summary>
public class ModificationHubRelocateTests
{
    /// <summary>Unity のプロジェクトの形のフォルダ（Assets と ProjectSettings）を作る。</summary>
    private static string NewProject(TestApp app, string name)
    {
        var path = Path.Combine(app.Root, "unity", name);
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
        return path;
    }

    /// <summary>在らないフォルダ（移した後の古い場所）。</summary>
    private static string MissingProject(TestApp app, string name) => Path.Combine(app.Root, "unity", name);

    private static UnityProjectScan Packages(params string[] ids)
        => new(ids.Select(id => new UnityProjectMatch(id, 1, 2)).ToList(), new Dictionary<string, IReadOnlyList<UnityImageMatch>>());

    private static async Task<string> CreateModificationAsync(TestApp app, string name, string? project)
    {
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", name)));
        if (project is not null)
        {
            await app.Services.Commands.ExecuteAsync(new UiCommand.SetModificationProject(created.Record.Id, project));
        }

        return created.Record.Id;
    }

    private static SearchHistoryEntry SavedSearch(string name, string kind, params string[] keys) => new()
    {
        Name = name,
        Modules = [new SearchModuleState { Kind = kind, Enabled = true, Items = keys }],
    };

    private static Task SaveSearchesAsync(TestApp app, params SearchHistoryEntry[] entries)
        => app.Services.Commands.ExecuteAsync(new UiCommand.ChangeSavedSearches(_ => new SavedSearchList { Entries = entries }));

    private static async Task<(ModificationHubViewModel Hub, MainViewModel Main)> OpenHubAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Project);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        return (hub, main);
    }

    private static HubProjectGroup Group(ModificationHubViewModel hub, string path)
        => hub.Groups.OfType<HubProjectGroup>().Single(group => ModificationService.SamePath(group.Path, path));

    private static async Task<string?> ProjectOfAsync(TestApp app, string id)
        => (await app.Services.Modifications.LoadAsync(id))!.UnityProject;

    /// <summary>押して、書き終わって一覧と右の欄が出し直されるまで待つ。</summary>
    private static async Task RelocateAsync(TestApp app, ModificationHubViewModel hub, string from)
    {
        hub.RelocateProjectCommand.Execute(from);
        await app.SettleAsync();
    }

    // ---- 出し分け ----

    /// <summary>
    /// 同じ場所を綴りの違い（末尾の区切り）で指す改変があっても、プロジェクトの行は1つ（2026-11-10。前は生の文字列で除いて2行並んだ）
    /// </summary>
    [Fact]
    public Task 綴りだけ違う同じ場所は_プロジェクトの行が1つになる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var gone = MissingProject(app, "作り物の古いプロジェクト");
        await CreateModificationAsync(app, "作り物の改変の甲", gone);
        await CreateModificationAsync(app, "作り物の改変の乙", gone + Path.DirectorySeparatorChar);

        var (hub, _) = await OpenHubAsync(app);

        Assert.Single(hub.Groups.OfType<HubProjectGroup>(), group => ModificationService.SamePath(group.Path, gone));
    });

    [Fact]
    public Task 見つからないプロジェクトの行だけ_付け替えのボタンの印が立つ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var found = NewProject(app, "Found");
        var missing = MissingProject(app, "Moved");
        await CreateModificationAsync(app, "見つかる改変", found);
        await CreateModificationAsync(app, "見つからない改変", missing);
        await CreateModificationAsync(app, "紐付けていない改変", null);

        var (hub, _) = await OpenHubAsync(app);

        // 行のボタンは IsMissing で出す（XAML）。見つかる行は右クリックだけ、紐付けていない見出しは右クリックも開かない（IsProject）
        Assert.True(Group(hub, missing).IsMissing);
        Assert.False(Group(hub, found).IsMissing);
        Assert.True(Group(hub, found).IsProject);
        Assert.False(hub.Groups.OfType<HubProjectGroup>().Single(group => group.Candidate is null).IsProject);
        Assert.Equal("フォルダが見つかりません。「場所を付け替える…」で選び直せます。", Group(hub, missing).OpenHint);
    });

    // ---- 断る・何もしない ----

    [Fact]
    public Task プロジェクトでないフォルダを選ぶと_行の下で断り_何も書かない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var id = await CreateModificationAsync(app, "夏の改変", missing);
        var plain = Path.Combine(app.Root, "plain");
        Directory.CreateDirectory(Path.Combine(plain, "Assets"));
        var picks = 0;
        app.PickProjectFolder = _ =>
        {
            picks++;
            return plain;
        };
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, missing);

        // 窓を開き直す輪にはしない（1回だけ聞く）。押した行の下に警告で出す
        Assert.Equal(1, picks);
        Assert.Equal(ModificationHubViewModel.NotProjectText, Group(hub, missing).Notice.Text);
        Assert.True(Group(hub, missing).Notice.IsWarning);
        Assert.Equal(missing, await ProjectOfAsync(app, id));
    });

    [Fact]
    public Task 同じ場所を選ぶと_綴りが違っても何もしない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var project = NewProject(app, "Same");
        var id = await CreateModificationAsync(app, "夏の改変", project);
        app.PickProjectFolder = from => from.Replace('\\', '/') + "/";
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, project);

        Assert.Equal(project, await ProjectOfAsync(app, id));
        Assert.Empty(Group(hub, project).Notice.Text);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 選ぶ窓をやめると_何もしない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var id = await CreateModificationAsync(app, "夏の改変", missing);
        string? asked = null;
        app.PickProjectFolder = from =>
        {
            asked = from;
            return null;
        };
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, missing);

        Assert.Equal(missing, asked);
        Assert.Equal(missing, await ProjectOfAsync(app, id));
    });

    // ---- 書き換える ----

    [Fact]
    public Task 付け替えると_改変の場所と控えと保存した検索と今の検索の条件が新しい場所になる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var moved = NewProject(app, "MovedHere");
        var first = await CreateModificationAsync(app, "夏の改変", missing);
        var second = await CreateModificationAsync(app, "冬の改変", missing + @"\");
        var other = await CreateModificationAsync(app, "ほかの改変", NewProject(app, "Other"));
        app.Services.UnityScans.Save(UnityProjectPath.Normalize(missing)!, Packages("9900001"));
        await SaveSearchesAsync(
            app,
            SavedSearch("中身", nameof(SearchModuleKind.UnityProjectContents), UnityProjectPath.Normalize(missing)!),
            SavedSearch("改変の", nameof(SearchModuleKind.UnityProject), missing),
            SavedSearch("関係ない", nameof(SearchModuleKind.UnityProject), @"D:\unity\Elsewhere"));
        // 調べ直しは失敗させる：成功すると調べた結果が控えに書かれ、写したかが見分けられない
        app.Services.ScanUnityProject = (_, _, _, _) => throw new IOException("作り物の読めないプロジェクト");
        app.PickProjectFolder = _ => moved;
        var (hub, main) = await OpenHubAsync(app);
        var condition = (ListModule)SearchModules.Add(main.Search, SearchModuleKind.UnityProjectContents);
        condition.AddKey(UnityProjectPath.Normalize(missing)!);

        await RelocateAsync(app, hub, missing);

        // 改変：古い場所を指していた2つだけ（綴りの揺れも同じ場所として）
        Assert.Equal(moved, await ProjectOfAsync(app, first));
        Assert.Equal(moved, await ProjectOfAsync(app, second));
        Assert.NotEqual(moved, await ProjectOfAsync(app, other));

        // 控え：新しい場所の物として読める。古い方は残す（右クリックで付け替えたとき、古いフォルダがまだ在り得る）
        Assert.True(app.Services.UnityScans.Load(moved)!.FindsSameAs(Packages("9900001")));
        Assert.NotNull(app.Services.UnityScans.Load(UnityProjectPath.Normalize(missing)!));

        // 保存した検索：Unity プロジェクトの条件の古い場所だけ
        var saved = app.Services.Store.SavedSearches.Load().Entries;
        Assert.Equal([moved], saved.Single(entry => entry.Name == "中身").Modules.Single().Items);
        Assert.Equal([moved], saved.Single(entry => entry.Name == "改変の").Modules.Single().Items);
        Assert.Equal([@"D:\unity\Elsewhere"], saved.Single(entry => entry.Name == "関係ない").Modules.Single().Items);

        // 今の検索の条件
        Assert.Equal([moved], condition.Chips.Select(chip => chip.Key));

        // 結果は新しい場所の行と右の欄に出す（古い行は消える）
        Assert.DoesNotContain(hub.Groups.OfType<HubProjectGroup>(), group => ModificationService.SamePath(group.Path, missing));
        Assert.Equal("改変 2 件と保存した検索 2 件を、新しい場所に付け替えました。", Group(hub, moved).Notice.Text);
        Assert.False(Group(hub, moved).Notice.IsWarning);
        Assert.Equal(moved, Assert.IsType<HubProjectDetail>(hub.Detail).Path);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 新しい場所に控えがあれば_古い控えで上書きしない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var moved = NewProject(app, "MovedHere");
        await CreateModificationAsync(app, "夏の改変", missing);
        app.Services.UnityScans.Save(UnityProjectPath.Normalize(missing)!, Packages("9900001"));
        app.Services.UnityScans.Save(moved, Packages("9900002"));
        app.Services.ScanUnityProject = (_, _, _, _) => throw new IOException("作り物の読めないプロジェクト");
        app.PickProjectFolder = _ => moved;
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, missing);

        Assert.True(app.Services.UnityScans.Load(moved)!.FindsSameAs(Packages("9900002")));
    });

    [Fact]
    public Task 見つかるプロジェクトも_右クリックの道で付け替えられる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var found = NewProject(app, "Old");
        var moved = NewProject(app, "New");
        var id = await CreateModificationAsync(app, "夏の改変", found);
        app.Services.ScanUnityProject = (_, _, _, _) => Packages();
        app.PickProjectFolder = _ => moved;
        var (hub, _) = await OpenHubAsync(app);

        // 右クリックの項目は見出しを引数に同じ命令を呼ぶ（XAML の CommandParameter は Path）
        await RelocateAsync(app, hub, Group(hub, found).Path!);

        Assert.Equal(moved, await ProjectOfAsync(app, id));
        Assert.Equal("改変 1 件を、新しい場所に付け替えました。", Group(hub, moved).Notice.Text);
    });

    // ---- 統合の確認 ----

    [Fact]
    public Task 別の改変が使うプロジェクトへは_統合の確認が出て_キャンセルなら何もしない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var used = NewProject(app, "Used");
        var mine = await CreateModificationAsync(app, "夏の改変", missing);
        await CreateModificationAsync(app, "冬の改変", used);
        app.PickProjectFolder = _ => used;
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, missing);

        var asked = Assert.Single(app.Notices);
        Assert.Equal("場所を付け替える", asked.Caption);
        Assert.Equal(MessageBoxImage.Question, asked.Icon);
        Assert.Equal(MessageBoxResult.Cancel, asked.DefaultResult);
        Assert.Equal(ModificationHubViewModel.MergeQuestion("Used", 1), asked.Text);
        Assert.Equal(missing, await ProjectOfAsync(app, mine));
    });

    [Fact]
    public Task 統合の確認でOKなら_同じプロジェクトの下にまとまる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var used = NewProject(app, "Used");
        var mine = await CreateModificationAsync(app, "夏の改変", missing);
        await CreateModificationAsync(app, "冬の改変", used);
        app.Services.ScanUnityProject = (_, _, _, _) => Packages();
        app.PickProjectFolder = _ => used;
        app.Answer = _ => MessageBoxResult.OK;
        var (hub, _) = await OpenHubAsync(app);

        await RelocateAsync(app, hub, missing);

        Assert.Equal(used, await ProjectOfAsync(app, mine));
        Assert.Equal(2, Group(hub, used).Modifications.Count);
    });

    // ---- 途中で書けなかった ----

    [Fact]
    public Task 書けない改変があると_書けた分は付け替え_古い行に残りの数を警告で出す() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        var missing = MissingProject(app, "Moved");
        var moved = NewProject(app, "MovedHere");
        var writable = await CreateModificationAsync(app, "夏の改変", missing);
        var locked = await CreateModificationAsync(app, "冬の改変", missing);
        app.Services.ScanUnityProject = (_, _, _, _) => Packages();
        app.PickProjectFolder = _ => moved;
        var (hub, _) = await OpenHubAsync(app);
        var lockedFile = app.Services.Paths.ModificationFile(locked);
        File.SetAttributes(lockedFile, FileAttributes.ReadOnly);
        try
        {
            await RelocateAsync(app, hub, missing);
        }
        finally
        {
            File.SetAttributes(lockedFile, FileAttributes.Normal);
        }

        Assert.Equal(moved, await ProjectOfAsync(app, writable));
        Assert.Equal(missing, await ProjectOfAsync(app, locked));
        var notice = Group(hub, missing).Notice;
        Assert.Equal("改変 1 件を、新しい場所に付け替えました。改変 1 件は書き込めませんでした。もう一度押すと、残りを付け替えます。", notice.Text);
        Assert.True(notice.IsWarning);
    });

    // ---- 結果の文 ----

    [Theory]
    [InlineData(3, 0, 0, false, "改変 3 件を、新しい場所に付け替えました。")]
    [InlineData(0, 0, 2, false, "保存した検索 2 件を、新しい場所に付け替えました。")]
    [InlineData(0, 0, 0, false, "付け替える改変と保存した検索はありませんでした。")]
    [InlineData(0, 2, 0, false, "改変 2 件は書き込めませんでした。もう一度押すと、残りを付け替えます。")]
    [InlineData(1, 0, 0, true, "改変 1 件を、新しい場所に付け替えました。保存した検索は書き込めず、古い場所のままです。")]
    public void 結果の文は_書けた物を先に_書けなかった物を後に言う(int moved, int failed, int saved, bool savedFailed, string expected)
        => Assert.Equal(expected, ModificationHubViewModel.ResultText(new UnityProjectRelocation
        {
            Modifications = moved,
            FailedModifications = failed,
            SavedSearches = saved,
            SavedSearchesFailed = savedFailed,
            ScanCopied = false,
        }));
}
