using BoothAssetManager.App.Services;
using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 設定の「プロジェクトの管理に使うアプリ」の説明の文と、選べる選択肢。
/// 手元に VCC・ALCOM のどちらがあるかで決まるので、調べる所を作り物に差し替えて確かめる。
/// </summary>
public class SettingsProjectManagerTests
{
    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app, UnityTools tools)
    {
        app.Tools = tools;
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);

        // 調べ終わるまでは空（調べるのはレジストリとファイルなので裏）。どの組み合わせでも、調べ終われば何か言う
        await UiThread.Until(() => settings.ProjectManagerNote.Length > 0, "VCC・ALCOM を調べ終わる");
        return settings;
    }

    [Fact]
    public Task 両方あって_vccリンクに合わせるなら_今どちらを開くかを言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: true, HasAlcom: true, LinkOpensAlcom: true));

        Assert.Equal(ProjectManagerChoice.VccLink, settings.ProjectManager);
        Assert.Equal("改変の画面から開くアプリです。vcc:// に合わせると、今はALCOMを開きます。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task 両方あって_リンクがVCCのままなら_VCCを開くと言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: true, HasAlcom: true, LinkOpensAlcom: false));

        Assert.Equal("改変の画面から開くアプリです。vcc:// に合わせると、今はVCCを開きます。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task 両方あって_どちらかを名指ししたら_説明だけにする() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(current => current with { ProjectManager = ProjectManagerChoice.Alcom });
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: true, HasAlcom: true, LinkOpensAlcom: false));

        Assert.Equal("改変の画面から開くアプリです。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task VCCだけなら_選んでもVCCを開くと言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: true, HasAlcom: false, LinkOpensAlcom: false));

        Assert.Equal("見つかったのはVCCだけなので、改変の画面からはVCCを開きます。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task ALCOMだけなら_ALCOMを開くと言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(false, HasVcc: false, HasAlcom: true, LinkOpensAlcom: true));

        Assert.Equal("見つかったのはALCOMだけなので、改変の画面からはALCOMを開きます。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task どちらも無ければ_入れると開けると言う() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false));

        Assert.Equal("VCCかALCOMを入れると、改変の画面から開けます。", settings.ProjectManagerNote);
    });

    [Fact]
    public Task 選べるのは_見つかった方だけ() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: false, HasAlcom: true, LinkOpensAlcom: true));

        Assert.Collection(
            settings.ProjectManagers,
            link => Assert.True(link.IsAvailable),      // 「vcc:// に合わせる」はいつでも選べる
            vcc => Assert.False(vcc.IsAvailable),
            alcom => Assert.True(alcom.IsAvailable));
    });

    [Fact]
    public Task 選び直すと_説明の文が替わり_設定に書かれる() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app, new UnityTools(true, HasVcc: true, HasAlcom: true, LinkOpensAlcom: true));
        var changed = new List<string?>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        settings.ProjectManager = ProjectManagerChoice.Vcc;

        Assert.Contains(nameof(SettingsViewModel.ProjectManagerNote), changed);
        Assert.Equal("改変の画面から開くアプリです。", settings.ProjectManagerNote);

        // 保存は投げっぱなしなので、書かれるのを待つ
        await UiThread.Until(
            () => app.Services.Settings.ProjectManager == ProjectManagerChoice.Vcc, "選んだアプリが設定に書かれる");
    });
}
