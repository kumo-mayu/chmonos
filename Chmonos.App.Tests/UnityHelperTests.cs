using System.IO;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// Unity まわりの、Unity を動かさなくても決まる所：プロジェクトタブで探す語の決め方・開いているエディタの見分け・
/// 改変から引く「どの商品を使ったか」。Unity を実際に動かす確かめは、頼まれたときだけ（CLAUDE.md）。
/// </summary>
public sealed class UnityHelperTests : IDisposable
{
    private readonly string _project = Path.Combine(Path.GetTempPath(), "chmonos-app-test-unity-" + Guid.NewGuid().ToString("N"));

    public UnityHelperTests() => Directory.CreateDirectory(Path.Combine(_project, "Assets"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_project, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Folder(string relative) => Directory.CreateDirectory(Path.Combine(_project, relative));

    // ---- プロジェクトタブで探す語 ----

    [Fact]
    public void Assetsの下のフォルダは_場所で探し_実在すれば開く()
    {
        // 名前だけで探すと、同じ名前の別のフォルダ（Assets/Addon/SampleCostume）を開いた
        Folder(@"Assets\SampleCostume");
        Folder(@"Assets\Addon\SampleCostume");

        var (query, stop) = UnityProjectTab.Plan(_project, @"Assets\SampleCostume\");

        Assert.Equal("a:assets glob:\"Assets/SampleCostume\" t:Folder", query);
        Assert.Null(stop);
    }

    [Fact]
    public void プロジェクトに無いフォルダは_探す語は作るが_開かない理由を返す()
    {
        var (query, stop) = UnityProjectTab.Plan(_project, "Assets/NotImported");

        Assert.Equal("a:assets glob:\"Assets/NotImported\" t:Folder", query);
        Assert.Equal("プロジェクトの中にそのフォルダが見つからないので", stop);
    }

    [Fact]
    public void 記号の入る名前は_名前の語で探し_1つに決まれば開く()
    {
        // [作者名] は glob では文字の組に読まれるので、場所では探せない
        Folder(@"Assets\[SampleShop] Costume Set");
        Folder(@"Assets\Other");

        var (query, stop) = UnityProjectTab.Plan(_project, "Assets/[SampleShop] Costume Set");

        Assert.Equal("a:assets [SampleShop] Costume Set t:Folder", query);
        Assert.Null(stop);
    }

    [Fact]
    public void 記号の入る名前で_似た名前のフォルダが複数あれば_開かない理由に数を言う()
    {
        // プロジェクトタブの名前の検索は語の一部で当たる
        Folder(@"Assets\[SampleShop] Costume Set");
        Folder(@"Assets\Backup\[SampleShop] Costume Set v2");

        var (_, stop) = UnityProjectTab.Plan(_project, "Assets/[SampleShop] Costume Set");

        Assert.Equal("似た名前のフォルダが 2 個あるので", stop);
    }

    [Fact]
    public void Packagesの下は_検索に出ないので探さず_左の木で見つける名前を言う()
    {
        Folder(@"Packages\com.sample.tool");
        File.WriteAllText(
            Path.Combine(_project, "Packages", "com.sample.tool", "package.json"),
            """{ "name": "com.sample.tool", "displayName": "Sample Tool" }""");

        var (query, stop) = UnityProjectTab.Plan(_project, "Packages/com.sample.tool/Runtime");

        // プロジェクトタブの Packages の下には、フォルダの名前ではなく表示名で並ぶ
        Assert.Null(query);
        Assert.Equal("Packagesの中（プロジェクトタブの左の木ではPackagesの下の「Sample Tool」）はUnityの検索に表示されないので", stop);
    }

    [Fact]
    public void Packagesの表示名が読めなければ_フォルダの名前で言う()
    {
        var (query, stop) = UnityProjectTab.Plan(_project, "Packages/com.sample.unknown");

        Assert.Null(query);
        Assert.Contains("「com.sample.unknown」", stop);
    }

    [Fact]
    public void プロジェクトのフォルダを数えられなければ_理由を言って止める()
    {
        var missingProject = Path.Combine(_project, "no-such-project");

        var (query, stop) = UnityProjectTab.Plan(missingProject, "Assets/[SampleShop] Costume");

        Assert.Equal("a:assets [SampleShop] Costume t:Folder", query);
        Assert.Equal("プロジェクトのフォルダを数えられなかったので", stop);
    }

    // ---- 開いているエディタの見分け ----

    [Fact]
    public void 場所が分かっているエディタは_場所で見分ける()
    {
        var editors = new[]
        {
            new OpenUnityEditor(1, "SampleProject", @"D:\other\SampleProject"),
            new OpenUnityEditor(2, "SampleProject", @"D:\unity\SampleProject"),
        };

        // 名前が同じでも、場所が違えば別のプロジェクト
        var found = UnityEditors.FindByProject(editors, @"d:\unity\sampleproject\", "SampleProject");

        Assert.Equal(2, found!.ProcessId);
    }

    [Fact]
    public void 場所が分からないエディタは_名前で見分ける()
    {
        var editors = new[] { new OpenUnityEditor(1, "OtherProject"), new OpenUnityEditor(2, "sampleproject") };

        var found = UnityEditors.FindByProject(editors, @"D:\unity\SampleProject", "SampleProject");

        Assert.Equal(2, found!.ProcessId);
    }

    [Fact]
    public void 見分けられないエディタは_名前では選ばない()
    {
        // 同じ名前の別のプロジェクトかもしれない。違うプロジェクトへ送るより、送らない方がよい
        var editors = new[] { new OpenUnityEditor(1, "SampleProject", ProjectPath: null, IsAmbiguous: true) };

        Assert.Null(UnityEditors.FindByProject(editors, @"D:\unity\SampleProject", "SampleProject"));
    }

    [Fact]
    public void 開いているエディタが無ければ_見つからない()
        => Assert.Null(UnityEditors.FindByProject([], @"D:\unity\SampleProject", "SampleProject"));

    // ---- 改変から引く「どの商品を使ったか」----

    private static ModificationRecord Modification(string id, string avatar, string? project, params ModificationMember[] members) => new()
    {
        Id = id,
        AvatarItemId = avatar,
        Name = id,
        UnityProject = project,
        Members = members,
    };

    [Fact]
    public void 改変_アバター_プロジェクトごとに_使った商品をまとめる()
    {
        var usage = ModificationUsage.From(
        [
            Modification("m1", "9000001", @"D:\unity\SampleProject",
                new ModificationMember { ItemId = "1000001" }, new ModificationMember { ItemId = "1000002" }),
            Modification("m2", "9000001", @"d:\unity\sampleproject",
                new ModificationMember { ItemId = "1000002" }, new ModificationMember { ItemId = "1000003" }),
            Modification("m3", "9000002", project: null, new ModificationMember { ItemId = "1000004" }),
        ]);

        Assert.Equal(["1000001", "1000002"], usage.ItemIdsByModification["m1"].Order());
        Assert.Equal(["1000001", "1000002", "1000003"], usage.ItemIdsByAvatar["9000001"].Order());
        Assert.Equal(["1000004"], usage.ItemIdsByAvatar["9000002"]);

        // プロジェクトは場所の大文字小文字を無視して1つにまとめ、紐付けの無い改変は入れない
        var project = Assert.Single(usage.ItemIdsByProject);
        Assert.Equal(["1000001", "1000002", "1000003"], project.Value.Order());
        Assert.Equal(3, usage.Records.Count);
    }

    [Fact]
    public void 外した商品は_使った商品に数えない()
    {
        var usage = ModificationUsage.From(
        [
            Modification("m1", "9000001", project: null,
                new ModificationMember { ItemId = "1000001" },
                new ModificationMember { ItemId = "1000002", Detached = true }),
        ]);

        Assert.Equal(["1000001"], usage.ItemIdsByModification["m1"]);
    }

    [Fact]
    public void 同じ商品が2回入っていても_1つと数える()
    {
        var usage = ModificationUsage.From(
        [
            Modification("m1", "9000001", project: null,
                new ModificationMember { ItemId = "1000001", VariationId = 1 },
                new ModificationMember { ItemId = "1000001", VariationId = 2 }),
        ]);

        Assert.Equal(["1000001"], usage.ItemIdsByModification["m1"]);
    }
}
