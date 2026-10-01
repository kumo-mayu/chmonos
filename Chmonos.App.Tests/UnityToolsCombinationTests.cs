using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

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
    [InlineData("hub", "Unity Hubの一覧にプロジェクトがありません（VCCとALCOMは見つかりませんでした）。Hubでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("vcc", "VCCの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。VCCでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("alcom", "ALCOMの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。ALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("both-link-vcc", "VCCの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。VCCかALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData("both-link-alcom", "VCCの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。VCCかALCOMでプロジェクトを作るか開くと、ここに並びます。")]
    public Task 組み合わせごとに_空の一覧の文が決まる(string combination, string expected) => TestApp.Run(async app =>
    {
        app.Tools = ToolsOf(combination);
        var hub = await OpenHubAsync(app);

        Assert.True(hub.IsEmpty);
        Assert.Equal(expected, hub.EmptyText);
    });

    // ---- 設定の「プロジェクトの管理に使うアプリ」 ----

    [Theory]
    [InlineData("none", false, false, "VCCかALCOMを入れると、改変の画面から開けます。")]
    [InlineData("hub", false, false, "VCCかALCOMを入れると、改変の画面から開けます。")]
    [InlineData("vcc", true, false, "見つかったのはVCCだけなので、改変の画面からはVCCを開きます。")]
    [InlineData("alcom", false, true, "見つかったのはALCOMだけなので、改変の画面からはALCOMを開きます。")]
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
    public Task 選んだ方が見つからなければ_見つかった方を出す(
        ProjectManagerChoice choice, string combination, bool showVcc, bool showAlcom) => TestApp.Run(async app =>
    {
        // 設定は残っても、無い方のボタンを出すと押して失敗する。見つかった方に倒す（SettingsViewModel.ProjectManager の説明）
        app.Tools = ToolsOf(combination);
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = choice });
        var hub = await OpenHubAsync(app);

        Assert.Equal(showVcc, hub.ShowVccButton);
        Assert.Equal(showAlcom, hub.ShowAlcomButton);
    });

    // ---- 「Unityを開く」で、その版のエディタも Hub も無いとき ----

    [Fact]
    public void エディタもHubも無ければ_Hubに渡したと言わず_入れ方を言う()
        // 受け手の無い unityhub:// を開くと Windows がアプリを探す窓を出すだけなので、Hub には渡さない（§9-1）
        => Assert.Equal(
            "「SampleProject」のUnityが手元に入っていません。Unity Hubも見つかりませんでした。Unity Hubを入れると、このバージョンのUnityを入れて開けます。",
            UnityOpenText.For(UnityOpenResult.NoEditorNoHub, "SampleProject"));

    [Fact]
    public void バージョンが読めずHubを開いたときは_Hubの一覧から開くよう言う()
        => Assert.Equal(
            "「SampleProject」のUnityのバージョンが読めなかったので、Unity Hubを開きました。Hubのプロジェクトの一覧から開いてください。",
            UnityOpenText.For(UnityOpenResult.HandedToHubWithoutVersion, "SampleProject"));

    private static async Task<ModificationHubViewModel> OpenHubAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Project);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        return hub;
    }
}
