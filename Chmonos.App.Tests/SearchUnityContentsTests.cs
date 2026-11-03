using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「Unityプロジェクト」（プロジェクトの中に入っている商品）と、検索欄の右のスイッチ「開いているUnity」。
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

    /// <summary>Unity を開いた後に窓へ戻ったことにして、スイッチが押せるようになるまで待つ。</summary>
    private static async Task OpenAsync(TestApp app, SearchViewModel search, params OpenUnityEditor[] editors)
    {
        app.OpenEditors = editors;
        search.NoteOpenProjectsMaybeChanged();
        await UiThread.Until(() => search.CanUseOpenUnity, "スイッチが押せる");
    }

    [Fact]
    public Task スイッチをオンにすると_開いているプロジェクトに入っている商品だけが出て_条件は足さない() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900002");
        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", project));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => Shown(search) is ["9900002"], "待つ");
        Assert.DoesNotContain(search.Modules, entry => entry.Kind == SearchModuleKind.UnityProjectContents);
        Assert.True(search.IsOpenUnityFiltering);
        Assert.Equal(1, search.ActiveFilterCount);
        Assert.Equal("開いているUnity", search.FilterSummary);
        Assert.False(search.HasOpenUnityStatus);

        // オフにすると全部に戻る
        search.OpenUnityOnly = false;
        await UiThread.Until(() => Shown(search) is ["9900001", "9900002"], "戻る");
        Assert.Equal(0, search.ActiveFilterCount);
    });

    [Fact]
    public Task スイッチのオンは次の起動に残り_最初の一覧から絞る() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(UnityProjectPath.Normalize(project)!, Packages("9900002"));
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with { SearchOpenUnityOnly = true });
        using var gate = new ManualResetEventSlim();
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            gate.Wait();
            return Packages("9900002");
        };
        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];

        var search = (await app.StartWhileWorkRunsAsync()).Search;

        // 全商品を一度見せてから絞らない（前回の結果から出す）
        Assert.True(search.OpenUnityOnly);
        Assert.Equal(["9900002"], Shown(search));
        Assert.Contains("「projA」の中を調べています", search.OpenUnityStatus);
        gate.Set();
        await UiThread.Until(() => !search.HasOpenUnityStatus, "調べ終わる");
    });

    [Fact]
    public Task スイッチを押すと_オンかオフかを覚える() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", NewProject(app, "projA")));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => app.Services.SettingsStore.UiState.SearchOpenUnityOnly, "書く");
    });

    [Fact]
    public Task 調べている間は_帯に進み具合と0件の理由を出し_読み直すで取り直す() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        using var gate = new ManualResetEventSlim();
        var scans = 0;
        var found = "9900001";
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            Interlocked.Increment(ref scans);
            gate.Wait();
            return Packages(found);
        };
        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", project));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => search.OpenUnityStatus.Contains("「projA」の中を調べています"), "待つ");
        Assert.Empty(Shown(search));
        Assert.Equal("開いているUnityの中を調べています。", search.EmptyHint);
        gate.Set();
        await UiThread.Until(() => Shown(search) is ["9900001"], "待つ");
        Assert.False(search.HasOpenUnityStatus);

        found = "9900002";
        search.ReloadOpenUnityCommand.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900002"], "取り直す");
        Assert.Equal(2, scans);
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
    public Task 先に始めた確認が後から終わっても_新しい結果を古い結果で上書きしない() => TestApp.Run(async app =>
    {
        var open = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900002");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        await UiThread.Until(() => module.StatusText == "Unityが開いていません。", "はじめの確認が終わる");

        // 1回目は遅れて「Unity は開いていない」と答え、2回目はすぐ「開いている」と答える
        using var late = new ManualResetEventSlim();
        var calls = 0;
        app.Services.OpenUnityEditors = () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                late.Wait();
                return [];
            }

            return [new OpenUnityEditor(1, "projB", open)];
        };

        search.NoteOpenProjectsMaybeChanged();
        await UiThread.Until(() => Volatile.Read(ref calls) == 1, "1回目が始まる");
        search.NoteOpenProjectsMaybeChanged();
        await UiThread.Until(() => Shown(search) is ["9900002"], "2回目の結果で絞る");

        late.Set();
        await app.SettleAsync();

        Assert.Equal(["9900002"], Shown(search));
        Assert.True(module.IsActive);
    });

    [Fact]
    public Task 思わぬ失敗でも_調べていますのまま残らない() => TestApp.Run(async app =>
    {
        // わざと失敗させ、ログに残ることも確かめる
        app.AllowLoggedFailures = true;
        var project = NewProject(app, "projA");
        var search = await StartWithItemsAsync(app, "9900001");
        app.Services.ScanUnityProject = (_, _, _, _) => throw new InvalidOperationException("作り物の失敗");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        module.AddKey(UnityProjectPath.Normalize(project)!);

        await UiThread.Until(() => module.StatusText.Contains("「projA」の中を調べられませんでした"), "理由を言う");
        Assert.DoesNotContain("調べています", module.StatusText);
        Assert.Contains(app.LoggedFailures(), line => line.Contains("作り物の失敗", StringComparison.Ordinal));
    });

    [Fact]
    public Task 名前の中のURLは_リンクにしない() => TestApp.Run(_ =>
    {
        static int LinksOf(System.Windows.Controls.RichTextBox box)
            => box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .OfType<System.Windows.Documents.Hyperlink>()
                .Count();

        const string text = "作り物の名前 https://example.invalid/";
        var body = new System.Windows.Controls.RichTextBox();
        Chmonos.App.Controls.SelectableText.SetText(body, text);
        var name = new System.Windows.Controls.RichTextBox();
        Chmonos.App.Controls.SelectableText.SetPlainText(name, true);
        Chmonos.App.Controls.SelectableText.SetText(name, text);

        Assert.Equal(1, LinksOf(body));
        Assert.Equal(0, LinksOf(name));
        Assert.Contains(text, new System.Windows.Documents.TextRange(name.Document.ContentStart, name.Document.ContentEnd).Text);
        return Task.CompletedTask;
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
    public Task スイッチは手で足した条件と両方に入っている物を出し_0件ならそう言う() => TestApp.Run(async app =>
    {
        var other = NewProject(app, "projA");
        var open = NewProject(app, "projB");
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        app.Services.ScanUnityProject = (project, _, _, _) =>
            Packages(project == UnityProjectPath.Normalize(open) ? "9900002" : "9900001");
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);
        module.IncludeOn = false;
        module.AddKey(UnityProjectPath.Normalize(other)!);
        await UiThread.Until(() => Shown(search) is ["9900001"], "条件だけ");
        await OpenAsync(app, search, new OpenUnityEditor(1, "projB", open));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => Shown(search).Count == 0 && !search.HasOpenUnityStatus, "待つ");
        Assert.Equal("「開いているUnity」と条件「Unityプロジェクト」の両方に入っている商品はありません。", search.EmptyHint);
        Assert.Equal(2, search.ActiveFilterCount);
    });

    [Fact]
    public Task 開いているプロジェクトに入っている物が無ければ_そう言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        ScanFinds(app);
        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", NewProject(app, "projA")));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => !search.HasOpenUnityStatus && Shown(search).Count == 0, "待つ");
        Assert.Equal("開いているUnityのプロジェクトに入っている商品はありません。", search.EmptyHint);
    });

    [Fact]
    public Task ほかの条件で0件のときは_条件をクリアの案内に_スイッチが残ると添える() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        ScanFinds(app, "9900001");
        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", NewProject(app, "projA")));
        search.OpenUnityOnly = true;
        await UiThread.Until(() => Shown(search) is ["9900001"], "絞る");

        search.QueryText = "当たらない語";

        await UiThread.Until(() => Shown(search).Count == 0, "0件");
        Assert.Equal("「条件をクリア」で、開いているUnityに入っている商品すべてに戻ります", search.EmptyHint);
    });

    [Fact]
    public Task Unityが開いていないとスイッチは押せず_オンを覚えたまま絞らず_開くと絞る() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with { SearchOpenUnityOnly = true });
        var search = await StartWithItemsAsync(app, "9900001", "9900002");
        ScanFinds(app, "9900002");

        Assert.False(search.CanUseOpenUnity);
        Assert.Equal("Unityが開いていません。", search.OpenUnityDisabledReason);
        Assert.True(search.OpenUnityOnly);
        Assert.False(search.IsOpenUnityFiltering);
        Assert.Equal(["9900001", "9900002"], Shown(search));
        Assert.Equal(0, search.ActiveFilterCount);

        await OpenAsync(app, search, new OpenUnityEditor(1, "projA", project));

        await UiThread.Until(() => Shown(search) is ["9900002"], "開くと絞る");
    });

    [Fact]
    public Task スイッチは_複数開いていれば開いている全部を出す() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001", "9900002", "9900003");
        app.Services.ScanUnityProject = (project, _, _, _) => Packages(project.EndsWith("projA", StringComparison.OrdinalIgnoreCase) ? "9900001" : "9900002");
        await OpenAsync(
            app, search,
            new OpenUnityEditor(1, "projA", NewProject(app, "projA")), new OpenUnityEditor(2, "projB", NewProject(app, "projB")));

        search.OpenUnityOnly = true;

        await UiThread.Until(() => Shown(search) is ["9900001", "9900002"], "待つ");
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 場所の分からないプロジェクトだけが開いていれば_スイッチは押せず理由を言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", null, IsAmbiguous: true)];

        search.NoteOpenProjectsMaybeChanged();

        await UiThread.Until(() => search.OpenUnityDisabledReason == "開いているUnityのプロジェクトの場所が分かりません。", "待つ");
        Assert.False(search.CanUseOpenUnity);
    });

    [Fact]
    public Task 場所の分からないプロジェクトは_条件では調べられないと言う() => TestApp.Run(async app =>
    {
        var search = await StartWithItemsAsync(app, "9900001");
        app.OpenEditors = [new OpenUnityEditor(1, "projA", null, IsAmbiguous: true)];

        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProjectContents);

        await UiThread.Until(() => module.StatusText.Contains("「projA」は場所が分からない"), "待つ");
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

    /// <summary>札で1つのプロジェクトを指した条件を、前回の状態として置く。</summary>
    private static Task RestoreChipAsync(TestApp app, string project)
        => app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchModules = [new SearchModuleState { Kind = nameof(SearchModuleKind.UnityProjectContents), Items = [project] }],
        });

    private static ListModule ContentsModule(SearchViewModel search)
        => (ListModule)search.Modules.Single(entry => entry.Kind == SearchModuleKind.UnityProjectContents);

    [Fact]
    public Task 起動のとき_調べ直している間は前回の結果を見せ_終わったら差し替える() => TestApp.Run(async app =>
    {
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);
        using var gate = new ManualResetEventSlim();
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            gate.Wait();
            return Packages("9900002");
        };

        var search = (await app.StartWhileWorkRunsAsync()).Search;

        Assert.Equal(["9900001"], Shown(search));
        Assert.Contains("「projA」の中を調べています", ContentsModule(search).StatusText);

        gate.Set();
        await UiThread.Until(() => Shown(search) is ["9900002"], "差し替える");
        Assert.DoesNotContain("調べています", ContentsModule(search).StatusText);

        // 次の起動のために、新しい結果が控えられている
        Assert.True(app.Services.UnityScans.Load(project)!.FindsSameAs(Packages("9900002")));
    });

    [Fact]
    public Task 起動のとき_今開いているプロジェクトの条件は_全商品を挟まず前回の結果から出す() => TestApp.Run(async app =>
    {
        var project = NewProject(app, "projA");
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(UnityProjectPath.Normalize(project)!, Packages("9900001"));
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with
        {
            SearchModules = [new SearchModuleState { Kind = nameof(SearchModuleKind.UnityProjectContents), IncludeOpenProject = true }],
        });
        using var gate = new ManualResetEventSlim();
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            gate.Wait();
            return Packages("9900001");
        };
        app.OpenEditors = [new OpenUnityEditor(1, "projA", project)];

        var main = await app.StartWhileWorkRunsAsync();

        Assert.Equal(["9900001"], Shown(main.Search));
        gate.Set();
        await UiThread.Until(() => !ContentsModule(main.Search).HasStatus, "調べ終わる");
    });

    [Fact]
    public Task 調べ直して同じ結果なら_一覧を組み直さない() => TestApp.Run(async app =>
    {
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);
        using var gate = new ManualResetEventSlim();
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            gate.Wait();
            return Packages("9900001");
        };
        var search = (await app.StartWhileWorkRunsAsync()).Search;
        var built = 0;
        search.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SearchViewModel.ResultSummary))
            {
                built++;
            }
        };

        gate.Set();
        await UiThread.Until(() => !ContentsModule(search).StatusText.Contains("調べています"), "調べ終わる");

        Assert.Equal(0, built);
        Assert.Equal(["9900001"], Shown(search));
    });

    [Fact]
    public Task 読み直すと_控えを見せず_調べた結果を待つ() => TestApp.Run(async app =>
    {
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);
        ScanFinds(app, "9900001");
        var search = (await app.StartAsync()).Search;
        await UiThread.Until(() => !ContentsModule(search).StatusText.Contains("調べています"), "調べ終わる");

        using var gate = new ManualResetEventSlim();
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            gate.Wait();
            return UnityProjectScan.Empty;
        };
        ContentsModule(search).ReloadCommand!.Execute(null);
        await UiThread.Until(() => ContentsModule(search).StatusText.Contains("調べています"), "調べ始める");

        // 入れ直した後に押すので、消えたはずの物を控えからまた見せない
        Assert.Empty(Shown(search));
        gate.Set();
        await UiThread.Until(() => !ContentsModule(search).StatusText.Contains("調べています"), "調べ終わる");
        Assert.Empty(Shown(search));
    });

    [Fact]
    public Task 調べ直せなかったら_前回の結果を残して理由を言う() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);
        app.Services.ScanUnityProject = (_, _, _, _) => throw new IOException("作り物の失敗");

        var search = (await app.StartAsync()).Search;

        await UiThread.Until(() => ContentsModule(search).StatusText.Contains("調べられませんでした"), "理由を言う");
        Assert.Equal(["9900001"], Shown(search));
    });

    [Fact]
    public Task フォルダを確かめられないときは_無いと言わず前回の結果を残す() => TestApp.Run(async app =>
    {
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);
        app.Services.ProjectFolderState = _ => DiskAnswer.Unknown;

        var search = (await app.StartAsync()).Search;

        await UiThread.Until(() => ContentsModule(search).StatusText.Contains("調べられませんでした"), "理由を言う");
        Assert.DoesNotContain("見つかりません", ContentsModule(search).StatusText);
        Assert.Equal(["9900001"], Shown(search));

        // 届くようになれば、読み直すで取り直せる
        app.Services.ProjectFolderState = _ => DiskAnswer.Present;
        ScanFinds(app, "9900002");
        ContentsModule(search).ReloadCommand!.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900002"], "取り直す");
        Assert.DoesNotContain("調べられませんでした", ContentsModule(search).StatusText);
    });

    [Fact]
    public Task 読み直しの後に終わった古い調べは_新しい結果の控えを上書きしない() => TestApp.Run(async app =>
    {
        var project = UnityProjectPath.Normalize(NewProject(app, "projA"))!;
        await app.AddItemAsync(Owned("9900001"));
        await app.AddItemAsync(Owned("9900002"));
        await RestoreChipAsync(app, project);
        using var late = new ManualResetEventSlim();
        var scans = 0;
        app.Services.ScanUnityProject = (_, _, _, _) =>
        {
            if (Interlocked.Increment(ref scans) == 1)
            {
                late.Wait();
                return Packages("9900001");
            }

            return Packages("9900002");
        };
        var search = (await app.StartWhileWorkRunsAsync()).Search;
        await UiThread.Until(() => Volatile.Read(ref scans) == 1, "古い調べが始まる");

        ContentsModule(search).ReloadCommand!.Execute(null);
        await UiThread.Until(() => Shown(search) is ["9900002"], "新しい結果で絞る");
        late.Set();
        await app.SettleAsync();

        Assert.True(app.Services.UnityScans.Load(project)!.FindsSameAs(Packages("9900002")));
        Assert.Equal(["9900002"], Shown(search));
    });

    [Fact]
    public Task フォルダが無くなったプロジェクトは_前回の結果を見せない() => TestApp.Run(async app =>
    {
        var project = Path.Combine(app.Root, "gone");
        await app.AddItemAsync(Owned("9900001"));
        app.Services.UnityScans.Save(project, Packages("9900001"));
        await RestoreChipAsync(app, project);

        var search = (await app.StartAsync()).Search;

        await UiThread.Until(() => ContentsModule(search).StatusText.Contains("フォルダが見つかりません"), "理由を言う");
        await UiThread.Until(() => Shown(search).Count == 0, "前回の結果を消す");
        Assert.NotNull(app.Services.UnityScans.Load(project));
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
