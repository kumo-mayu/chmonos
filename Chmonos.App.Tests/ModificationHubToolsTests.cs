using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面の「VCCを開く」「ALCOMを開く」の出し分けと、Unityプロジェクトの見方が空のときの文。
///
/// どちらも「Unity Hub・VCC・ALCOM のどれが手元にあるか」で決まる。前は実マシンに入っている物でしか確かめられず、
/// ALCOM だけ・どれも無い、の組み合わせは見られなかった。調べる所を作り物に差し替えて、全部の組み合わせを確かめる。
/// </summary>
public class ModificationHubToolsTests
{
    private static UnityTools Tools(bool hub = false, bool vcc = false, bool alcom = false, bool linkOpensAlcom = false)
        => new(hub, vcc, alcom, linkOpensAlcom);

    // ---- 空のときの文 ----

    [Fact]
    public void どれも無ければ_どれかを入れるよう言う()
        => Assert.Equal(
            "Unity HubもVCCもALCOMも見つかりませんでした。どれかを入れてプロジェクトを作るか開くと、ここに並びます。",
            ModificationHubViewModel.ProjectEmptyText(Tools()));

    [Fact]
    public void Hubだけなら_Hubで作るよう言う()
        => Assert.Equal(
            "Unity Hubの一覧にプロジェクトがありません（VCCとALCOMは見つかりませんでした）。Hubでプロジェクトを作るか開くと、ここに並びます。",
            ModificationHubViewModel.ProjectEmptyText(Tools(hub: true)));

    [Fact]
    public void VCCだけなら_VCCの一覧と言う()
        => Assert.Equal(
            "VCCの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。VCCでプロジェクトを作るか開くと、ここに並びます。",
            ModificationHubViewModel.ProjectEmptyText(Tools(vcc: true)));

    [Fact]
    public void ALCOMだけなら_一覧をALCOMの名前で呼ぶ()
        // ALCOM は VCC と同じ一覧を書くので、ALCOM だけの PC でも一覧はあるものとして言う（ユーザ判断 2026-09-29）
        => Assert.Equal(
            "ALCOMの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。ALCOMでプロジェクトを作るか開くと、ここに並びます。",
            ModificationHubViewModel.ProjectEmptyText(Tools(alcom: true)));

    [Fact]
    public void VCCとALCOMの両方なら_作る先は両方を言う()
        => Assert.Equal(
            "VCCの一覧にプロジェクトがありません（Unity Hubは見つかりませんでした）。VCCかALCOMでプロジェクトを作るか開くと、ここに並びます。",
            ModificationHubViewModel.ProjectEmptyText(Tools(vcc: true, alcom: true)));

    [Theory]
    [InlineData(true, false, "Unity HubとVCCの一覧にプロジェクトがありません。どちらかでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData(false, true, "Unity HubとALCOMの一覧にプロジェクトがありません。どちらかでプロジェクトを作るか開くと、ここに並びます。")]
    [InlineData(true, true, "Unity HubとVCCの一覧にプロジェクトがありません。どちらかでプロジェクトを作るか開くと、ここに並びます。")]
    public void Hubと管理のアプリがあれば_両方の一覧を言う(bool vcc, bool alcom, string expected)
        => Assert.Equal(expected, ModificationHubViewModel.ProjectEmptyText(Tools(hub: true, vcc: vcc, alcom: alcom)));

    [Fact]
    public void 調べる前は_見つからないと言い出さない()
    {
        // 調べ終わる前に「見つかりません」と出すと、入っている人の画面で一瞬うそを言う
        var text = ModificationHubViewModel.ProjectEmptyText(UnityTools.Unknown);

        Assert.DoesNotContain("見つかりません", text);
    }

    // ---- 画面を通して：空の文 ----

    [Fact]
    public Task プロジェクトの見方が空なら_手元にある物に合わせた文を出す() => TestApp.Run(async app =>
    {
        app.Tools = Tools(alcom: true);
        var hub = await OpenHubAsync(app, ModificationHubLevel.Project);

        Assert.True(hub.IsEmpty);
        Assert.Equal(ModificationHubViewModel.ProjectEmptyText(app.Tools), hub.EmptyText);
        Assert.StartsWith("ALCOMの一覧に", hub.EmptyText);
    });

    [Fact]
    public Task アバターの見方と改変の見方が空なら_それぞれの次にやることを出す() => TestApp.Run(async app =>
    {
        var avatars = await OpenHubAsync(app, ModificationHubLevel.Avatar);
        Assert.Equal(
            "持っているアバターがまだありません。アバターの管理で検出するか、アバターの商品を取り込むと出ます。",
            avatars.EmptyText);

        var modifications = await OpenHubAsync(app, ModificationHubLevel.Modification);
        Assert.Equal(
            "改変はまだありません。「アバター」の見方で、アバターの行の「改変を作る」から作れます。",
            modifications.EmptyText);
    });

    [Fact]
    public Task 探して当たらなければ_打った語を入れて言う() => TestApp.Run(async app =>
    {
        var hub = await OpenHubAsync(app, ModificationHubLevel.Project);

        hub.Query = "  どこにも無い  ";

        Assert.Equal("「どこにも無い」に当てはまるものはありません。", hub.EmptyText);
    });

    [Fact]
    public Task 一覧にプロジェクトがあれば_空の文の代わりに行が並ぶ() => TestApp.Run(async app =>
    {
        app.Tools = Tools(hub: true);
        app.UnityProjects =
        [
            new Core.Services.UnityProjectCandidate
            {
                Path = @"D:\unity\SampleProject",
                Name = "SampleProject",
                Folder = @"D:\unity",
                Version = "2022.3.22f1",
                Exists = true,
                IsOpen = false,
                Source = Core.Services.UnityProjectSource.Hub,
            },
        ];

        var hub = await OpenHubAsync(app, ModificationHubLevel.Project);

        Assert.False(hub.IsEmpty);
        Assert.Single(hub.Groups);
    });

    // ---- 画面を通して：ボタンの出し分け ----

    [Fact]
    public Task VCCだけなら_VCCを開くを押せる形で出す() => TestApp.Run(async app =>
    {
        app.Tools = Tools(vcc: true);
        var hub = await OpenHubAsync(app);

        Assert.True(hub.ShowVccButton);
        Assert.True(hub.CanOpenVcc);
        Assert.False(hub.ShowAlcomButton);
        Assert.Equal("VRChat Creator Companionを起動するか、手前に表示します。", hub.VccHint);
    });

    [Fact]
    public Task ALCOMだけなら_ALCOMを開くだけを出す() => TestApp.Run(async app =>
    {
        app.Tools = Tools(alcom: true);
        var hub = await OpenHubAsync(app);

        Assert.False(hub.ShowVccButton);
        Assert.True(hub.ShowAlcomButton);
    });

    [Fact]
    public Task どちらも無ければ_VCCを開くを押せない形で出し_吹き出しで入れ方を言う() => TestApp.Run(async app =>
    {
        // ボタンごと消すと、ここから開けることに気付けない
        app.Tools = Tools(hub: true);
        var hub = await OpenHubAsync(app);

        Assert.True(hub.ShowVccButton);
        Assert.False(hub.CanOpenVcc);
        Assert.False(hub.ShowAlcomButton);
        Assert.Equal("VCCかALCOMを入れると、ここから開けます。", hub.VccHint);
    });

    [Theory]
    [InlineData(ProjectManagerChoice.VccLink, false, true)]
    [InlineData(ProjectManagerChoice.VccLink, true, false)]
    [InlineData(ProjectManagerChoice.Vcc, true, true)]
    [InlineData(ProjectManagerChoice.Alcom, false, false)]
    public Task 両方あれば_設定の方を1つだけ出す(ProjectManagerChoice choice, bool linkOpensAlcom, bool expectVcc)
        => TestApp.Run(async app =>
        {
            app.Tools = Tools(vcc: true, alcom: true, linkOpensAlcom: linkOpensAlcom);
            await app.ChangeSettingsAsync(settings => settings with { ProjectManager = choice });
            var hub = await OpenHubAsync(app);

            // 2つ並べると、どちらで開くかを毎回選ばせることになる
            Assert.Equal(expectVcc, hub.ShowVccButton);
            Assert.Equal(!expectVcc, hub.ShowAlcomButton);
            Assert.True(hub.CanOpenVcc);
        });

    [Fact]
    public Task 設定を変えて戻ると_出すボタンが替わる() => TestApp.Run(async app =>
    {
        app.Tools = Tools(vcc: true, alcom: true);
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = ProjectManagerChoice.Vcc });
        var hub = await OpenHubAsync(app);
        Assert.True(hub.ShowVccButton);

        // ボタンは設定を毎回読む（写しを持たない）。設定の画面で変えて戻ったら、その場で合う
        await app.ChangeSettingsAsync(settings => settings with { ProjectManager = ProjectManagerChoice.Alcom });

        Assert.False(hub.ShowVccButton);
        Assert.True(hub.ShowAlcomButton);
    });

    // ---- 開いた結果の文 ----

    [Theory]
    [InlineData(AppOpenResult.Launched, "VCC", "VCCを起動しました。")]
    [InlineData(AppOpenResult.BroughtToFront, "ALCOM", "ALCOMは開いていたので、手前に出しました。")]
    [InlineData(
        AppOpenResult.AlreadyOpenNotFront, "VCC",
        "VCCは開いています。手前に出せなかったので、タスクバーのVCCを押して切り替えてください。")]
    [InlineData(AppOpenResult.NotInstalled, "ALCOM", "ALCOMが見つかりませんでした。ALCOMを入れ直すと、ここから開けます。")]
    public void 開いた結果は_VCCとALCOMで同じ文にアプリの名前を入れる(AppOpenResult result, string app, string expected)
        => Assert.Equal(expected, ModificationHubViewModel.OpenResultText(result, app));

    [Theory]
    [InlineData(@"D:\unity\SampleProject", "SampleProject")]
    [InlineData(@"D:\unity\SampleProject\", "SampleProject")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void プロジェクトの名前はフォルダ名(string? path, string expected)
        => Assert.Equal(expected, ModificationHubViewModel.ProjectNameOf(path));

    private static async Task<ModificationHubViewModel> OpenHubAsync(
        TestApp app, ModificationHubLevel level = ModificationHubLevel.Project)
    {
        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, level);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        return hub;
    }
}
