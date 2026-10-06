using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// Unity Hub・VCC・ALCOM の入り方の組み合わせごとに、改変の画面と設定の画面に何が出るかを1つの表で確かめる（公開前の点検 2026-10-01）。
///
/// 作り手の PC には3つとも入っていて、入っていない PC の出方は見られない（docs/history/modifications.md §9-1）。
/// 個々の出し分けは <see cref="ModificationHubToolsTests"/>・<see cref="SettingsProjectManagerTests"/> にあるが、
/// 「Hub も無い」側の組み合わせが抜けていた。組み合わせを行に並べ、同じ組み合わせで2つの画面が食い違わないかを見る。
/// </summary>
public class UnityToolsCombinationTests
{
    private const string MissingHint = "VCCかALCOMを入れると、ここから開けます。";
    private const string VccHint = "VRChat Creator Companionを起動するか、手前に表示します。";

    private static UnityTools ToolsOf(string combination) => combination switch
    {
        "none" => new(HasHub: false, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false),
        "hub" => new(HasHub: true, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false),
        "vcc" => new(HasHub: false, HasVcc: true, HasAlcom: false, LinkOpensAlcom: false),
        // ALCOM だけの PC では、ALCOM が vcc:// を引き受けていることが多い。引き受けていなくても出方は同じ（片方だけならその方）
        "alcom" => new(HasHub: false, HasVcc: false, HasAlcom: true, LinkOpensAlcom: true),
        "both-link-vcc" => new(HasHub: false, HasVcc: true, HasAlcom: true, LinkOpensAlcom: false),
        "both-link-alcom" => new(HasHub: false, HasVcc: true, HasAlcom: true, LinkOpensAlcom: true),
        _ => throw new ArgumentException(combination),
    };

    // ---- 改変の画面（Unityプロジェクトの見方）：ボタン・吹き出し ----

    [Theory]
    [InlineData("none", true, false, false, MissingHint)]
    [InlineData("hub", true, false, false, MissingHint)]
    [InlineData("vcc", true, true, false, VccHint)]
    [InlineData("alcom", false, false, true, MissingHint)]
    [InlineData("both-link-vcc", true, true, false, VccHint)]
    [InlineData("both-link-alcom", false, true, true, VccHint)]
    public Task 組み合わせごとに_出すボタンと押せるかが決まる(
        string combination, bool showVcc, bool canOpenVcc, bool showAlcom, string vccHint) => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf(combination);
        var hub = await OpenHubAsync(app);

        Assert.Equal(showVcc, hub.ShowVccButton);
        Assert.Equal(canOpenVcc, hub.CanOpenVcc);
        Assert.Equal(showAlcom, hub.ShowAlcomButton);

        // 出すのはいつも1つ（2つ並べると、どちらで開くかを毎回選ばせる）。どれも無くても消さない（ここから開けることに気付けない）
        Assert.True(hub.ShowVccButton ^ hub.ShowAlcomButton);
        Assert.Equal(vccHint, hub.VccHint);
    });

    // ---- 改変の画面：一覧が空のときの文 ----

    [Theory]
    [InlineData("none", "Unity HubもVCCもALCOMも見つかりませんでした。どれかを入れてプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("hub", "Unity Hubの一覧にプロジェクトがありません。Hubでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("vcc", "VCCの一覧にプロジェクトがありません。VCCでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("alcom", "ALCOMの一覧にプロジェクトがありません。ALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("both-link-vcc", "VCCの一覧にプロジェクトがありません。VCCかALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    // 上のボタンが「ALCOMを開く」なら、一覧も ALCOM の名前で呼ぶ（前は「VCCの一覧に」と言い、ボタンと食い違っていた）
    [InlineData("both-link-alcom", "ALCOMの一覧にプロジェクトがありません。VCCかALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    public Task 組み合わせごとに_空の一覧の文が決まる(string combination, string expected) => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf(combination);
        var hub = await OpenHubAsync(app);

        Assert.True(hub.IsEmpty);
        Assert.Equal(expected, hub.EmptyText);
        Assert.True(expected.Length <= 80, "本文は80字まで（ui-writing.md）");
        Assert.DoesNotContain("（", expected);
    });

    [Theory]
    [InlineData(ProjectManagerChoice.Vcc, "VCCの一覧に")]
    [InlineData(ProjectManagerChoice.Alcom, "ALCOMの一覧に")]
    public Task 両方あれば_一覧の名前は設定で選んだボタンの方に合わせる(ProjectManagerChoice choice, string start) => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf("both-link-alcom");
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = choice });
        var hub = await OpenHubAsync(app);

        Assert.StartsWith(start, hub.EmptyText);
        Assert.Equal(choice == ProjectManagerChoice.Alcom, hub.ShowAlcomButton);
    });

    // ---- 一覧の名前：手元にある方で呼ぶ（UnityToolsText に1か所でまとめた決まり） ----

    [Theory]
    [InlineData("none", "VCC")]
    [InlineData("hub", "VCC")]
    [InlineData("vcc", "VCC")]
    [InlineData("alcom", "ALCOM")]
    [InlineData("both-link-vcc", "VCC")]
    [InlineData("both-link-alcom", "ALCOM")]
    public void 一覧の名前は_出しているボタンと同じ(string combination, string expected)
    {
        var tools = ToolsOf(combination);

        Assert.Equal(expected, UnityToolsText.ManagerName(tools, ProjectManagerChoice.VccLink));
        Assert.Equal(expected == "ALCOM", tools.Buttons(ProjectManagerChoice.VccLink).ShowAlcom);
    }

    [Theory]
    [InlineData("none", UnityProjectSource.Vcc, "VCC")]
    [InlineData("alcom", UnityProjectSource.Vcc, "ALCOM")]
    [InlineData("alcom", UnityProjectSource.Hub | UnityProjectSource.Vcc, "Unity Hub・ALCOM")]
    [InlineData("both-link-alcom", UnityProjectSource.Vcc, "ALCOM")]
    [InlineData("both-link-vcc", UnityProjectSource.Vcc, "VCC")]
    [InlineData("alcom", UnityProjectSource.Hub, "Unity Hub")]
    [InlineData("alcom", UnityProjectSource.None, "改変から紐付けたもの")]
    public void 情報元は_手元にある方の名前で言う(string combination, UnityProjectSource source, string expected)
        => Assert.Equal(expected, UnityToolsText.SourceValue(source, ToolsOf(combination), ProjectManagerChoice.VccLink));

    [Fact]
    public Task ALCOMだけのPCで_VCCの一覧に載ったプロジェクトの情報元はALCOMと出す() => TestApp.Run(async app =>
    {
        var path = System.IO.Path.Combine(app.Root, "SampleProject");
        app.Tools = ToolsOf("alcom");
        app.UnityProjects =
        [
            new UnityProjectCandidate
            {
                Path = path, Name = "SampleProject", Folder = app.Root, Version = "2022.3.22f1",
                Exists = true, IsOpen = false, Source = UnityProjectSource.Vcc,
            },
        ];
        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(
            app.Services, main, main.Thumbnails, ModificationHubLevel.Project,
            new ModificationHubSelection(ModificationHubSelectionKind.Project, path));
        await UiThread.Until(() => hub.Detail is HubProjectDetail, "選んだプロジェクトが右の欄に出る");

        Assert.Equal("ALCOM", ((HubProjectDetail)hub.Detail!).SourceValue);
    });

    [Theory]
    [InlineData("none", "Unityプロジェクトの一覧を読み直します。")]
    [InlineData("hub", "Unity Hubの一覧を読み直します。")]
    [InlineData("vcc", "VCCの一覧を読み直します。")]
    [InlineData("alcom", "ALCOMの一覧を読み直します。")]
    [InlineData("both-link-alcom", "ALCOMの一覧を読み直します。")]
    public void 読み直すの吹き出しは_入っている物の一覧を言う(string combination, string expected)
    {
        Assert.Equal(expected, UnityToolsText.RefreshHint(ToolsOf(combination), ProjectManagerChoice.VccLink));
        Assert.True(expected.Length <= 40, "吹き出しは40字まで（ui-writing.md）");
    }

    [Fact]
    public void Hubと管理のアプリがあれば_読み直すの吹き出しは両方を言う()
        => Assert.Equal(
            "Unity HubとALCOMの一覧を読み直します。",
            UnityToolsText.RefreshHint(ToolsOf("alcom") with { HasHub = true }, ProjectManagerChoice.VccLink));

    [Theory]
    [InlineData("none", "Unity HubかVCCかALCOMを入れて、プロジェクトを追加してください。")]
    [InlineData("hub", "先にUnity Hubにプロジェクトを追加してください。")]
    [InlineData("vcc", "先にVCCにプロジェクトを追加してください。")]
    [InlineData("alcom", "先にALCOMにプロジェクトを追加してください。")]
    public void 紐付け直す先の足し方は_入っている物で言う(string combination, string expected)
        => Assert.Equal(expected, UnityToolsText.AddProjectFirst(ToolsOf(combination), ProjectManagerChoice.VccLink));

    [Fact]
    public void Hubと管理のアプリがあれば_どちらかに足すよう言う()
        => Assert.Equal(
            "先にUnity HubかVCCにプロジェクトを追加してください。",
            UnityToolsText.AddProjectFirst(ToolsOf("vcc") with { HasHub = true }, ProjectManagerChoice.VccLink));

    [Theory]
    [InlineData("none", "Unity HubもVCCもALCOMも見つかりませんでした。")]
    [InlineData("hub", "Unity Hubの一覧に無いプロジェクトです。")]
    [InlineData("alcom", "ALCOMの一覧に無いプロジェクトです。")]
    public void Unityで選択で引けない理由は_入っている物の一覧で言う(string combination, string expected)
        => Assert.Equal(expected, UnityToolsText.NotListed(ToolsOf(combination), ProjectManagerChoice.VccLink));

    [Fact]
    public void Hubと管理のアプリがあれば_両方に無いと言う()
        => Assert.Equal(
            "Unity HubにもVCCにも無いプロジェクトです。",
            UnityToolsText.NotListed(ToolsOf("vcc") with { HasHub = true }, ProjectManagerChoice.VccLink));

    // ---- 改変の右側の「紐付ける先」 ----

    [Theory]
    [InlineData("none", "Unity HubもVCCもALCOMも見つかりませんでした。どれかを入れてプロジェクトを作るか開くと、ここに並びます。", "Unityプロジェクトの一覧を読み直します。")]
    [InlineData("alcom", "ALCOMの一覧にプロジェクトがありません。ALCOMでプロジェクトを作るか開くと、ここに並びます。", "ALCOMの一覧を読み直します。")]
    [InlineData("hub", "Unity Hubの一覧にプロジェクトがありません。Hubでプロジェクトを作るか開くと、ここに並びます。", "Unity Hubの一覧を読み直します。")]
    public Task 紐付ける先の空の文と吹き出しは_入っている物で言う(
        string combination, string empty, string hint) => TestApp.Run(async app =>
    {
        // 前は入っているかを見ずに「Unity Hubと、VCCかALCOMの一覧を見ましたが…」と言っていた
        app.Tools = ToolsOf(combination);
        var main = await app.StartAsync();
        var record = new ModificationRecord { Id = "m1", AvatarItemId = "1000001", Name = "夏の改変" };
        var modification = new ModificationViewModel(record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => modification.RefreshProjectsHint == hint, "Hub・VCC・ALCOM を調べ終わる");

        Assert.False(modification.HasProjectCandidates);
        Assert.Equal(empty, modification.ProjectCandidatesEmptyText);
    });

    // ---- 設定の「プロジェクトの管理に使うアプリ」 ----

    [Theory]
    [InlineData("none", false, false, "VCCかALCOMを入れると、改変の画面から開けます。")]
    [InlineData("hub", false, false, "VCCかALCOMを入れると、改変の画面から開けます。")]
    [InlineData("vcc", true, false, "VCCだけ見つかったので、改変の画面ではVCCを開きます。")]
    [InlineData("alcom", false, true, "ALCOMだけ見つかったので、改変の画面ではALCOMを開きます。")]
    [InlineData("both-link-vcc", true, true, "改変の画面から開くアプリです。vcc:// に合わせると、今はVCCを開きます。")]
    [InlineData("both-link-alcom", true, true, "改変の画面から開くアプリです。vcc:// に合わせると、今はALCOMを開きます。")]
    public Task 組み合わせごとに_設定で選べる物と説明が決まる(
        string combination, bool vccAvailable, bool alcomAvailable, string note) => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf(combination);
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => settings.ProjectManagerNote.Length > 0, "VCC・ALCOM を調べ終わる");

        Assert.Equal(note, settings.ProjectManagerNote);
        Assert.Collection(
            settings.ProjectManagers,
            link => Assert.True(link.IsAvailable),
            vcc => Assert.Equal(vccAvailable, vcc.IsAvailable),
            alcom => Assert.Equal(alcomAvailable, alcom.IsAvailable));
    });

    // ---- 設定で選んだ方が後で消えたとき ----

    [Theory]
    [InlineData(ProjectManagerChoice.Vcc, "alcom", false, true)]
    [InlineData(ProjectManagerChoice.Alcom, "vcc", true, false)]
    [InlineData(ProjectManagerChoice.Alcom, "none", true, false)]
    public Task 選んだ方が見つからなければ_見つかった方のボタンを出す(
        ProjectManagerChoice choice, string combination, bool showVcc, bool showAlcom) => TestApp.Run(async app =>
    {
        // 設定は残っても、無い方のボタンを出すと押して失敗する。見つかった方に倒す（SettingsViewModel.ProjectManager の説明）
        app.Tools = ToolsOf(combination);
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = choice });
        var hub = await OpenHubAsync(app);

        Assert.Equal(showVcc, hub.ShowVccButton);
        Assert.Equal(showAlcom, hub.ShowAlcomButton);
    });

    [Theory]
    [InlineData(ProjectManagerChoice.Vcc, "alcom", "ALCOMだけ見つかったので、改変の画面ではALCOMを開きます。")]
    [InlineData(ProjectManagerChoice.Alcom, "vcc", "VCCだけ見つかったので、改変の画面ではVCCを開きます。")]
    [InlineData(ProjectManagerChoice.Vcc, "none", "VCCかALCOMを入れると、改変の画面から開けます。")]
    public Task 設定で選んだ方が見つからなければ_欄はvccに合わせるを見せ_設定は書き換えない(
        ProjectManagerChoice choice, string combination, string note) => TestApp.Run(async app =>
    {
        // 前は欄が押せない「VCC」を選んだ形のまま、下の説明は「ALCOMを開きます」と食い違っていた
        app.Tools = ToolsOf(combination);
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = choice });
        var settings = await OpenSettingsAsync(app);

        Assert.Equal(ProjectManagerChoice.VccLink, settings.ProjectManagerShown);
        Assert.Equal(choice, settings.ProjectManager);
        Assert.Equal(note, settings.ProjectManagerNote);

        // 欄が見せている値を返してきても、選び直しではないので保存しない
        settings.ProjectManagerShown = ProjectManagerChoice.VccLink;
        Assert.Equal(choice, settings.ProjectManager);
        Assert.Equal(choice, app.Services.Settings.ProjectManager);
    });

    [Fact]
    public Task 選んでいた方を入れ直すと_欄は選んでいた方に戻る() => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf("alcom");
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = ProjectManagerChoice.Vcc });
        var main = await app.StartAsync();
        var before = await OpenSettingsAsync(app, main);
        Assert.Equal(ProjectManagerChoice.VccLink, before.ProjectManagerShown);

        app.Tools = ToolsOf("both-link-alcom");
        var after = await OpenSettingsAsync(app, main);

        Assert.Equal(ProjectManagerChoice.Vcc, after.ProjectManagerShown);
    });

    [Fact]
    public Task 見つからない方を選んでいても_別の方を選べば保存する() => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf("both-link-vcc") with { HasVcc = false };
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = ProjectManagerChoice.Vcc });
        var settings = await OpenSettingsAsync(app);

        settings.ProjectManagerShown = ProjectManagerChoice.Alcom;

        Assert.Equal(ProjectManagerChoice.Alcom, settings.ProjectManager);
        await UiThread.Until(
            () => app.Services.Settings.ProjectManager == ProjectManagerChoice.Alcom, "選んだアプリが設定に書かれる");
    });

    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app, MainViewModel? main = null)
    {
        main ??= await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => settings.ProjectManagerNote.Length > 0, "VCC・ALCOM を調べ終わる");
        return settings;
    }

    // ---- 「Unityを開く」で、その版のエディタも Hub も無いとき ----

    [Fact]
    public void エディタもHubも無ければ_Hubに渡したと言わず_入れ方を言う()
        // 受け手の無い unityhub:// を開くと Windows がアプリを探す窓を出すだけなので、Hub には渡さない（§9-1）。
        // 載せるのは「入っていない」と「どうすれば開けるか」の2つ（前は「Hubも見つかりませんでした」を足した3文だった）
        => Assert.Equal(
            "「SampleProject」のUnityが入っていません。Unity Hubを入れると、このバージョンを入れて開けます。",
            UnityOpenText.For(UnityOpenResult.NoEditorNoHub, "SampleProject", hasHub: false));

    [Fact]
    public void バージョンが読めずHubを開いたときは_Hubの一覧から開くよう言う()
        => Assert.Equal(
            "「SampleProject」のUnityのバージョンが読めなかったので、Unity Hubを開きました。Hubのプロジェクトの一覧から開いてください。",
            UnityOpenText.For(UnityOpenResult.HandedToHubWithoutVersion, "SampleProject", hasHub: true));

    [Theory]
    [InlineData("hub", "Unityを開けませんでした。Unity Hubから開いてみてください。")]
    [InlineData("none", "Unityを開けませんでした。Unityを起動して、このプロジェクトのフォルダを開いてみてください。")]
    [InlineData("vcc", "Unityを開けませんでした。Unityを起動して、このプロジェクトのフォルダを開いてみてください。")]
    public Task 開けなかったとき_Hubが無い人にHubを勧めない(string combination, string expected) => TestApp.Run(async app =>
    {
        // 前はレジストリを直に読んでいて、Hub の無い側を試験で確かめられなかった。今は差し替えられる口で、その場で調べる
        app.Tools = ToolsOf(combination);

        Assert.Equal(expected, await UnityOpenText.ForAsync(app.Services, UnityOpenResult.Failed, "SampleProject"));
    });

    private static async Task<ModificationHubViewModel> OpenHubAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Project);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        return hub;
    }
}
