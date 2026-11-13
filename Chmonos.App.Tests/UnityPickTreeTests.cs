using Chmonos.App.ViewModels;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 「Unityで選択」の木（ユーザ判断 2026-11-13：候補が大量に平らに並んだのをフォルダの木にする）。
/// 木の組み立て・平らにする基準・開いた時の形・キーの動き・選んだ葉から元の候補が引けるか。フォルダ名と画像名は作り物
/// </summary>
public class UnityPickTreeTests
{
    private static UnityImageMatch Same(string path) => new(path, System.IO.Path.GetFileName(path), UnityImageMatchKind.SameImage);

    private static UnityImageMatch Named(string path) => new(path, System.IO.Path.GetFileName(path), UnityImageMatchKind.SameName);

    private static UnityPackageEntry Package(string name, string zip = @"C:\dl\sample.zip")
        => new(zip, $"{name}.unitypackage", 100);

    private static string Shape(IEnumerable<UnityPickRow> rows)
        => string.Join("\n", rows.Select(row => $"{new string(' ', row.Depth * 2)}{row.Name}{(row.IsLeaf ? $" [{row.Tag}]" : $" ({row.Count})")}"));

    [Theory]
    [InlineData(0, 3, true)] // unitypackage だけ：木にするフォルダが無い
    [InlineData(1, 20, true)] // 画像1つ：同じ
    [InlineData(5, 3, true)] // 8件：流さずに全部見える
    [InlineData(8, 1, false)] // 報告の形（同じ画像8・unitypackage 1）：9件で流れるので木
    [InlineData(9, 0, false)]
    public void IsFlat_IsTrueOnlyWhenEverythingFitsOrThereIsNoFolderToGroup(int images, int packages, bool flat)
        => Assert.Equal(flat, UnityPickTree.IsFlat(images, packages));

    [Fact]
    public void Build_PutsCommonHeadOnOneRow_AndFoldsSingleChildChains()
    {
        var roots = UnityPickTree.Build(
            [
                Same("Assets/ShopA/Avatar/Textures/Body/body.png"),
                Same("Assets/ShopA/Avatar/Textures/Body/body_normal.png"),
                Named("Assets/ShopA/Avatar/Textures/Hair/hair.png"),
                Same("Assets/ShopA/Avatar/Textures/Hair/Variants/Dark/hair_dark.png"),
                Same("Assets/ShopA/Avatar/Thumbnail.png"),
            ],
            []);

        // 共通の頭（Assets/ShopA/Avatar）は1行・フォルダが先で画像が後・1本道（Variants/Dark）は1行に畳む
        Assert.Equal(
            """
            Assets/ShopA/Avatar (5)
              Textures (4)
                Body (2)
                  body.png [同じ画像]
                  body_normal.png [同じ画像]
                Hair (2)
                  Variants/Dark (1)
                    hair_dark.png [同じ画像]
                  hair.png [名前が同じ]
              Thumbnail.png [同じ画像]
            """.ReplaceLineEndings("\n"),
            Shape(Flatten(roots)));
    }

    [Fact]
    public void Build_ComparesHeadsBySegment_NotByText()
    {
        // 「Foo」と「Foobar」は文字の頭は同じでも別のフォルダ
        var roots = UnityPickTree.Build([Same("Assets/Foo/a.png"), Same("Assets/Foobar/b.png")], []);

        Assert.Equal(
            """
            Assets (2)
              Foo (1)
                a.png [同じ画像]
              Foobar (1)
                b.png [同じ画像]
            """.ReplaceLineEndings("\n"),
            Shape(Flatten(roots)));
    }

    [Fact]
    public void Build_SplitsImagesAndPackagesIntoSections_OnlyWhenBothExist()
    {
        var both = UnityPickTree.Build([Same("Assets/X/a.png"), Same("Assets/Y/b.png")], [Package("Avatar_Body", @"C:\dl\body.zip")]);
        Assert.Equal(["画像", "unitypackage"], both.Select(row => row.Name));
        Assert.All(both, row => Assert.Equal(UnityPickRowKind.Section, row.Kind));
        Assert.Equal(2, both[0].Count);
        var package = Assert.Single(both[1].Children);
        Assert.Equal("Avatar_Body", package.Name);
        Assert.Equal("body.zip", package.Tag);

        var imagesOnly = UnityPickTree.Build([Same("Assets/X/a.png"), Same("Assets/Y/b.png")], []);
        Assert.Equal(UnityPickRowKind.Folder, Assert.Single(imagesOnly).Kind);
    }

    [Fact]
    public void Leaf_KeepsTheOriginalCandidate_AndTellsHowItWasFound()
    {
        var image = Named("Assets/X/Sub/a.png");
        var entry = Package("Pack");
        var roots = UnityPickTree.Build([Same("Assets/X/b.png"), image], [entry]);
        var leaves = Flatten(roots).Where(row => row.IsLeaf).ToList();

        var imageLeaf = leaves.Single(row => row.Name == "a.png");
        Assert.Same(image, imageLeaf.Image);
        Assert.Null(imageLeaf.Package);
        Assert.Equal("名前が同じ", imageLeaf.Tag);
        Assert.Equal("a.png、名前が同じ", imageLeaf.AutomationName);

        // 吹き出しは平らな行と同じ文（見つかり方と Unity の中の場所）と、商品の側の名前
        Assert.Equal("名前が同じ：Assets/X/Sub/a.png\na.png", imageLeaf.ToolTip);

        var packageLeaf = leaves.Single(row => row.Package is not null);
        Assert.Same(entry, packageLeaf.Package);
        Assert.Null(packageLeaf.Image);
    }

    [Fact]
    public void Folder_ShowsCountAndFullPathInToolTip()
    {
        var roots = UnityPickTree.Build([Same("Assets/X/Tex/a.png"), Same("Assets/X/Tex/b.png"), Same("Assets/X/c.png")], []);
        var tex = Flatten(roots).Single(row => row.Name == "Tex");

        Assert.Equal("2", tex.CountText);
        Assert.Equal("Tex、2 件", tex.AutomationName);
        Assert.Equal("Assets/X/Tex", tex.ToolTip);
    }

    [Fact]
    public void ExpandInitially_OpensSmallTreesFully()
    {
        var roots = UnityPickTree.Build(
            [.. Enumerable.Range(0, 4).Select(i => Same($"Assets/X/A/a{i}.png")), .. Enumerable.Range(0, 5).Select(i => Same($"Assets/X/B/b{i}.png"))],
            []);
        UnityPickTree.ExpandInitially(roots);

        // 根1・フォルダ2・画像9 = 12行は 16 行に収まるので全部開く
        Assert.Equal(12, UnityPickTree.Visible(roots).Count);
    }

    [Fact]
    public void ExpandInitially_KeepsBigFoldersFolded_ButAlwaysOpensALoneRoot()
    {
        // 色違いの包みを入れたような形：同じ頭の下に 30 フォルダ × 10 枚
        var images = Enumerable.Range(0, 30)
            .SelectMany(folder => Enumerable.Range(0, 10).Select(i => Same($"Assets/Shop/Item/Color{folder:00}/tex{i}.png")))
            .ToList();
        var roots = UnityPickTree.Build(images, []);
        UnityPickTree.ExpandInitially(roots);

        var root = Assert.Single(roots);
        Assert.True(root.IsExpanded); // 根が1つなら、開いても行が収まらなくても開く（畳んでも見渡せる物が増えない）
        Assert.Equal(300, root.Count);
        Assert.All(root.Children, folder => Assert.False(folder.IsExpanded));
        Assert.Equal(31, UnityPickTree.Visible(roots).Count);
        Assert.Null(UnityPickTree.FirstVisibleLeaf(roots));
    }

    [Fact]
    public void ManyCandidates_BuildWithoutBlowingUp()
    {
        // 何十〜数百件でも固まらないこと（数の確かめ。時間は測らない）：深さのある 600 枚と包み 40 個
        var images = Enumerable.Range(0, 600)
            .Select(i => i % 3 == 0 ? Named($"Assets/Shop/Item/Set{i % 12}/Part{i % 5}/tex{i}.png") : Same($"Assets/Shop/Item/Set{i % 12}/Part{i % 5}/tex{i}.png"))
            .ToList();
        var packages = Enumerable.Range(0, 40).Select(i => Package($"Pack{i:00}")).ToList();

        var model = new UnityPickDialogViewModel("Unityで選択", images, packages);

        Assert.Equal(600, model.Roots[0].Count);
        Assert.Equal(40, model.Roots[1].Count);
        Assert.Equal(640, Flatten(model.Roots).Count(row => row.IsLeaf));
        Assert.True(model.Rows.Count <= UnityPickTree.InitialRows + 12); // 開くのは収まる分（と兄弟の無い段）だけ
    }

    [Fact]
    public void Dialog_SelectsFirstLeaf_AndOnlyLeavesCanBePicked()
    {
        var model = new UnityPickDialogViewModel("Unityで選択", [Same("Assets/X/A/a.png"), Same("Assets/X/b.png")], []);

        Assert.Equal("a.png", model.Selected?.Name);
        Assert.True(model.CanPick);

        model.Selected = model.Rows[0];
        Assert.False(model.CanPick);
        Assert.Null(model.Picked);
    }

    [Fact]
    public void Keys_RightOpensThenEnters_LeftFoldsThenGoesUp()
    {
        var images = Enumerable.Range(0, 20).Select(i => Same($"Assets/X/A/a{i:00}.png"))
            .Append(Same("Assets/X/B/b.png"))
            .ToList();
        var model = new UnityPickDialogViewModel("Unityで選択", images, []);
        var a = model.Roots[0].Children.Single(row => row.Name == "A");
        Assert.False(a.IsExpanded); // 20枚は 16 行に収まらないので畳む

        model.Selected = a;
        model.Right();
        Assert.True(a.IsExpanded);
        Assert.Same(a, model.Selected);
        Assert.Contains(a.Children[0], model.Rows);

        model.Right();
        Assert.Equal("a00.png", model.Selected?.Name);

        model.Right(); // 葉では何もしない
        Assert.Equal("a00.png", model.Selected?.Name);

        model.Left(); // 葉から親へ
        Assert.Same(a, model.Selected);

        model.Left(); // 開いたフォルダは畳む
        Assert.False(a.IsExpanded);
        Assert.DoesNotContain(a.Children[0], model.Rows);

        model.Left(); // 畳んだフォルダから親へ
        Assert.Same(model.Roots[0], model.Selected);
    }

    [Fact]
    public void Enter_OnFolderToggles_OnLeafDecides()
    {
        var model = new UnityPickDialogViewModel("Unityで選択", [Same("Assets/X/A/a.png"), Same("Assets/X/b.png")], []);
        var folder = model.Roots[0].Children.Single(row => row.Name == "A");
        Assert.True(folder.IsExpanded);

        model.Selected = folder;
        Assert.False(model.Activate());
        Assert.False(folder.IsExpanded);

        model.Selected = model.Rows.Single(row => row.Name == "b.png");
        Assert.True(model.Activate());
    }

    [Fact]
    public void Folding_MovesSelectionOutOfHiddenRows()
    {
        var model = new UnityPickDialogViewModel("Unityで選択", [Same("Assets/X/A/a.png"), Same("Assets/X/b.png")], []);
        var folder = model.Roots[0].Children.Single(row => row.Name == "A");
        Assert.Equal("a.png", model.Selected?.Name);

        model.Toggle(folder);

        Assert.Same(folder, model.Selected);
        Assert.False(model.CanPick);
    }

    [Fact]
    public void Ask_ReturnsThePickedCandidate()
    {
        var target = Named("Assets/X/Sub/target.png");
        var entry = Package("Pack");
        try
        {
            UnityPickDialogViewModel.Intercept = model => Flatten(model.Roots).Single(row => row.Image == target);
            Assert.Same(target, UnityPickDialogViewModel.Ask("Unityで選択", [Same("Assets/X/a.png"), target], [entry])?.Image);

            UnityPickDialogViewModel.Intercept = model => Flatten(model.Roots).Single(row => row.Package == entry);
            Assert.Same(entry, UnityPickDialogViewModel.Ask("Unityで選択", [Same("Assets/X/a.png"), target], [entry])?.Package);

            UnityPickDialogViewModel.Intercept = _ => null;
            Assert.Null(UnityPickDialogViewModel.Ask("Unityで選択", [Same("Assets/X/a.png"), target], [entry]));
        }
        finally
        {
            UnityPickDialogViewModel.Intercept = null;
        }
    }

    private static List<UnityPickRow> Flatten(IEnumerable<UnityPickRow> rows)
        => [.. rows.SelectMany(row => (IEnumerable<UnityPickRow>)[row, .. Flatten(row.Children)])];
}
