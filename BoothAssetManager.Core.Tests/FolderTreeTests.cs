using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class FolderTreeTests
{
    /// <summary>検索の「ファイルの場所」の候補：祖先を全部含み、通過点を畳まず、大文字小文字の違いで割れない。</summary>
    [Fact]
    public void AllFoldersListsEveryAncestorOnce()
    {
        var items = new[]
        {
            Item("1", @"D:\Assets\Avatar\a.zip"),
            Item("2", @"d:\assets\Cloth\b.zip"),
        };

        Assert.Equal(
            [@"D:\Assets", @"D:\Assets\Avatar", @"d:\assets\Cloth"],
            FolderTree.AllFolders(items));
    }

    private static ItemRecord Item(string id, params string[] paths) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock
        {
            LocalFiles = paths
                .Select((path, index) => new LocalFileRecord
                {
                    Hash = id + index,
                    Paths = [path],
                    SizeBytes = 1,
                })
                .ToList(),
        },
    };

    private static string[] Names(IEnumerable<FolderNode> nodes) => nodes.Select(node => node.Name).ToArray();

    /// <summary>
    /// 通過点は畳む。D:\ → storage → VRChat_model のように子が1つしかない階層が続くところは
    /// 1行にまとめる（VS Code の compact folders と同じ）。
    /// </summary>
    [Fact]
    public void CollapsesPassThroughFolders()
    {
        var items = new[]
        {
            Item("1", @"D:\storage\VRChat_model\a.zip"),
            Item("2", @"D:\storage\VRChat_model\b.zip"),
        };

        var root = Assert.Single(FolderTree.Children(items, null));

        Assert.Equal(@"D:\storage\VRChat_model", root.Path);
        Assert.Equal(2, root.ItemCount);
    }

    /// <summary>外付けのドライブ文字が変わった物は今の文字の下に入れ、記録の文字を添える（ユーザ指示 2026-09-14）。</summary>
    [Fact]
    public void 読み替えた物は今の文字の下に入り記録の文字を添える()
    {
        var items = new[]
        {
            Item("1", @"X:\storage\VRChat_model\a.zip"),
            Item("2", @"D:\storage\VRChat_model\b.zip"),
        };

        static string Map(string path) => path.StartsWith("X:", StringComparison.OrdinalIgnoreCase) ? "D:" + path[2..] : path;

        var root = Assert.Single(FolderTree.Children(items, null, Map));

        Assert.Equal(@"D:\storage\VRChat_model", root.Path);
        Assert.Equal(2, root.ItemCount);
        Assert.Equal(["X:"], root.RecordedLetters);
    }

    [Fact]
    public void 読み替えの無い木には記録の文字を添えない()
    {
        var items = new[] { Item("1", @"D:\a\b.zip"), Item("2", @"D:\a\c.zip") };

        Assert.Empty(Assert.Single(FolderTree.Children(items, null)).RecordedLetters);
    }

    [Fact]
    public void 読み替えた今の場所で絞り込める()
    {
        var item = Item("1", @"X:\storage\a.zip");

        Assert.True(FolderTree.IsUnder(item, @"D:\storage", path => "D:" + path[2..]));
        Assert.False(FolderTree.IsUnder(item, @"D:\storage"));
    }

    /// <summary>枝分かれするところで畳むのをやめる。</summary>
    [Fact]
    public void StopsCollapsingWhereItBranches()
    {
        var items = new[]
        {
            Item("1", @"D:\storage\VRChat_model\a.zip"),
            Item("2", @"D:\storage\VRChat_clothes\b.zip"),
        };

        var root = Assert.Single(FolderTree.Children(items, null));
        Assert.Equal(@"D:\storage", root.Path);

        Assert.Equal(["VRChat_clothes", "VRChat_model"], Names(FolderTree.Children(items, root.Path)));
    }

    /// <summary>根は複数ありうる。散らばって管理している人ほどそうなる。</summary>
    [Fact]
    public void KeepsEveryRoot()
    {
        var items = new[]
        {
            Item("1", @"D:\storage\VRChat_model\a.zip"),
            Item("2", @"D:\storage\VRChat_model\b.zip"),
            Item("3", @"E:\backup\c.zip"),
            Item("4", @"E:\backup\d.zip"),
        };

        Assert.Equal(2, FolderTree.Children(items, null).Count);
    }

    /// <summary>
    /// 商品が1件のフォルダで止める。それ以上降りても同じ1件しか出ない。
    /// 展開後の書庫の中身はすべてこれに当たるので、この1規則で自動的に止まる。
    /// </summary>
    [Fact]
    public void DoesNotDescendIntoFoldersWithOneItem()
    {
        var items = new[]
        {
            Item("1", @"D:\lib\alpha\Assets\deep\file.txt"),
            Item("2", @"D:\lib\beta\file.zip"),
        };

        var children = FolderTree.Children(items, @"D:\lib");

        Assert.Equal(["alpha", "beta"], Names(children));
        Assert.All(children, node => Assert.False(node.CanDescend));
    }

    /// <summary>1つの商品が同じフォルダに複数ファイルを持っていても1回だけ数える。</summary>
    [Fact]
    public void CountsAnItemOncePerFolder()
    {
        var items = new[] { Item("1", @"D:\lib\a.zip", @"D:\lib\b.zip", @"D:\lib\c.pdf") };

        Assert.Equal(1, Assert.Single(FolderTree.Children(items, null)).ItemCount);
    }

    /// <summary>1つの商品が複数のフォルダにまたがる場合、どちらにも属する。実データにも存在する。</summary>
    [Fact]
    public void CountsAnItemUnderEveryFolderItLivesIn()
    {
        var items = new[]
        {
            Item("1", @"D:\lib\x.zip", @"E:\other\x.zip"),
            Item("2", @"D:\lib\y.zip"),
            Item("3", @"E:\other\y.zip"),
        };

        var roots = FolderTree.Children(items, null);

        Assert.All(roots, node => Assert.Equal(2, node.ItemCount));
        Assert.True(FolderTree.IsUnder(items[0], @"D:\lib"));
        Assert.True(FolderTree.IsUnder(items[0], @"E:\other"));
    }

    /// <summary>選んだフォルダの子孫も含む。含まないと通過点を選んだとき0件になる。</summary>
    [Fact]
    public void IncludesDescendants()
    {
        var item = Item("1", @"D:\storage\VRChat_model\kuuta\body.fbx");

        Assert.True(FolderTree.IsUnder(item, @"D:\storage"));
        Assert.True(FolderTree.IsUnder(item, @"D:\storage\VRChat_model"));
        Assert.False(FolderTree.IsUnder(item, @"D:\other"));
    }

    /// <summary>Windowsでは大文字小文字を区別しない。同じ場所が別のノードに割れないようにする。</summary>
    [Fact]
    public void TreatsCasingAsTheSamePlace()
    {
        var items = new[]
        {
            Item("1", @"D:\Storage\VRChat_model\a.zip"),
            Item("2", @"D:\storage\vrchat_model\b.zip"),
        };

        Assert.Equal(2, Assert.Single(FolderTree.Children(items, null)).ItemCount);
    }

    /// <summary>
    /// 数字は数として並べる。辞書順だと 10_ が 2_ より前に来て、
    /// 番号接頭辞で処理順を表している人の設計が壊れる。
    /// </summary>
    [Fact]
    public void SortsNumbersAsNumbers()
    {
        var items = new[]
        {
            Item("1", @"D:\w\2_Package\a.zip"),
            Item("2", @"D:\w\10_Project\b.zip"),
            Item("3", @"D:\w\1_Base\c.zip"),
        };

        Assert.Equal(["1_Base", "2_Package", "10_Project"], Names(FolderTree.Children(items, @"D:\w")));
    }

    /// <summary>ファイルを持たない商品はどのフォルダにも現れない。</summary>
    [Fact]
    public void IgnoresItemsWithoutFiles()
    {
        var items = new[]
        {
            Item("1", @"D:\lib\a.zip"),
            new ItemRecord
            {
                Id = "2",
                Booth = new BoothBlock { Name = "情報だけ", FetchedAt = DateTimeOffset.Now },
                Local = new LocalBlock(),
            },
        };

        Assert.Equal(1, Assert.Single(FolderTree.Children(items, null)).ItemCount);
    }
}
