using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>改変の画面のプロジェクトの右の欄に並べる VPM（plan 40）。名前・版は作り物。</summary>
public sealed class ModificationHubVpmTests
{
    [Fact]
    public void まだ調べていないプロジェクトと_1つも無いプロジェクトを言い分ける()
    {
        Assert.Equal("Unityで開くと表示されます。", HubProjectDetail.VpmText(null));
        Assert.Equal("ありません。", HubProjectDetail.VpmText([]));
    }

    [Fact]
    public void 一つ一行で表示名と版を出し_手で置いた物とフォルダの無い物には添える()
    {
        var text = HubProjectDetail.VpmText(
        [
            new VpmPackage("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true),
            new VpmPackage("com.example.core", null, "1.5.0", null, true),
            new VpmPackage("com.example.embedded", "手の物", null, "Embedded", false),
        ]);

        Assert.Equal("作り物シェーダー 2.0.1\ncom.example.core 1.5.0：フォルダが見つかりません\n手の物：手で置いたもの", text);
    }

    [Fact]
    public Task プロジェクトを選ぶと_調べた結果の控えからVPMを読んで右の欄に出す() => TestApp.Run(async app =>
    {
        var path = System.IO.Path.Combine(app.Root, "SampleProject");
        app.UnityProjects =
        [
            new UnityProjectCandidate
            {
                Path = path, Name = "SampleProject", Folder = app.Root, Version = "2022.3.22f1",
                Exists = true, IsOpen = false, Source = UnityProjectSource.Vcc,
            },
        ];
        app.Services.UnityScans.Save(path, UnityProjectScan.Empty with { Vpm = [new VpmPackage("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true)] });

        var main = await app.StartAsync();
        var hub = new ModificationHubViewModel(
            app.Services, main, main.Thumbnails, ModificationHubLevel.Project,
            new ModificationHubSelection(ModificationHubSelectionKind.Project, path));
        await UiThread.Until(() => hub.Detail is HubProjectDetail, "選んだプロジェクトが右の欄に出る");

        Assert.Equal("作り物シェーダー 2.0.1", ((HubProjectDetail)hub.Detail!).VpmValue);
    });
}
