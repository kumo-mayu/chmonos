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

        var detail = (HubProjectDetail)hub.Detail!;
        Assert.Equal("作り物シェーダー 2.0.1", detail.VpmValue);

        // 1つ以上あれば行で並べ、初めて調べた日を「から」で添える（日付は控えた時の時計なので形だけ見る）
        Assert.False(detail.HasVpmValue);
        var row = Assert.Single(detail.VpmRows);
        Assert.Equal("作り物シェーダー 2.0.1", row.Text);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}から$", row.SinceText);
        Assert.False(row.HasPast);
        Assert.False(detail.HasVpmRemoved);
    });

    private static readonly DateTimeOffset Day1 = new(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9));

    private static VpmHistoryEntry At(int day, string? version, bool removed = false) => new(Day1.AddDays(day), version, removed);

    [Fact]
    public void 履歴があれば_今の版に見つけた日を添え_前の状態を新しい順に広げる()
    {
        var rows = HubVpmRows.Build(
            [
                new VpmPackage("com.example.shader", "作り物シェーダー", "2.3.3", "com.example.shader", true),
                new VpmPackage("com.example.tool", "作り物の道具", "0.1.0", "com.example.tool", true),
            ],
            [
                new VpmPackageHistory("com.example.shader", "作り物シェーダー", [At(0, "2.3.2"), At(5, null, removed: true), At(10, "2.3.3")]),
                new VpmPackageHistory("com.example.tool", "作り物の道具", [At(10, "0.1.0")]),
            ]);

        var shader = rows.Current[0];
        Assert.Equal("作り物シェーダー 2.3.3", shader.Text);
        Assert.Equal("2026-10-11から", shader.SinceText);
        Assert.Equal(["外していた　2026-10-06〜2026-10-11", "2.3.2　2026-10-01〜2026-10-06"], shader.PastLines);
        Assert.Equal("作り物シェーダーの前のバージョン", shader.ExpandName);

        // 前の状態の無い行は三角を持たないが、並びに三角があるので名前の頭を揃える
        var tool = rows.Current[1];
        Assert.False(tool.HasPast);
        Assert.True(tool.AlignWithToggle);
        Assert.Empty(rows.Removed);
    }

    [Fact]
    public void 外したものは_最後の版と外した日で別に並べる()
    {
        var rows = HubVpmRows.Build(
            [],
            [
                new VpmPackageHistory("com.example.old", "古い作り物", [At(0, "1.0.0"), At(3, null, removed: true)]),
                new VpmPackageHistory("com.example.gone", null, [At(0, null), At(3, null, removed: true)]),
            ]);

        Assert.Empty(rows.Current);
        Assert.Equal(["com.example.gone", "古い作り物 1.0.0"], rows.Removed.Select(row => row.Text));
        Assert.All(rows.Removed, row => Assert.True(row.IsRemoved));
        Assert.Equal("2026-10-04に外した", rows.Removed[1].SinceText);
        Assert.Equal(["1.0.0　2026-10-01〜2026-10-04"], rows.Removed[1].PastLines);
        Assert.Equal(["バージョンが読めません　2026-10-01〜2026-10-04"], rows.Removed[0].PastLines);
    }

    [Fact]
    public void 履歴の無い前の控えでは_日付も外したものも出さない()
    {
        var rows = HubVpmRows.Build([new VpmPackage("com.example.shader", "作り物シェーダー", "2.0.1", "com.example.shader", true)], null);

        var row = Assert.Single(rows.Current);
        Assert.Null(row.SinceText);
        Assert.False(row.AlignWithToggle);
        Assert.Empty(rows.Removed);
    }
}
