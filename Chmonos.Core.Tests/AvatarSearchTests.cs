using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// アバターを語で探す欄の照らし方（メモ48・メモ58）。名前だけでなく、登録簿の呼び方とBOOTHの正式名でも当たる。
/// 名前は作り物（実在のアバター名は使わない）。
/// </summary>
public class AvatarSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-avsearch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AvatarRegistryEntry Entry() => new()
    {
        ItemId = "9900001",
        BoothName = "【3Dモデル】ミズホ / Mizuho",
        Aliases = [new AvatarAlias { Text = "Mzh" }, new AvatarAlias { Text = "みずほ" }, new AvatarAlias { Text = "ミズホ" }],
    };

    [Fact]
    public void 呼び方で当たる_札には呼び方が付く()
    {
        Assert.True(AvatarSearch.Matches(Entry(), "ミズホ", "mz", out var hint));
        Assert.Equal("呼び方「Mzh」", hint!.Label);
    }

    [Fact]
    public void 大文字小文字は区別しない()
    {
        Assert.True(AvatarSearch.Matches(Entry(), "ミズホ", "MZH", out _));
    }

    [Fact]
    public void BOOTHの正式名の一部でも当たる_札は正式名()
    {
        Assert.True(AvatarSearch.Matches(Entry(), "ミズホ", "3Dモデル", out var hint));
        Assert.StartsWith("正式名", hint!.Label);
    }

    [Fact]
    public void 名前や商品IDで当たるときは札を付けない()
    {
        Assert.True(AvatarSearch.Matches(Entry(), "ミズホ", "ミズ", out var byName));
        Assert.Null(byName);

        Assert.True(AvatarSearch.Matches(Entry(), "ミズホ", "99000", out var byId));
        Assert.Null(byId);
    }

    [Fact]
    public void どれにも入っていない語は当たらない()
    {
        Assert.False(AvatarSearch.Matches(Entry(), "ミズホ", "ほかの名前", out var hint));
        Assert.Null(hint);
    }

    [Fact]
    public void 呼び方の一覧は名前と同じ字を省く()
    {
        var hints = AvatarSearch.Hints(Entry(), "ミズホ");

        Assert.DoesNotContain(hints, hint => hint.Text == "ミズホ");
        Assert.Contains(hints, hint => hint.Text == "みずほ");
    }

    // ----- 改変の絵（メモ58③） -----

    private static AppPaths MakePaths(string root)
    {
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        return paths;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
    }

    [Fact]
    public void 改変の絵は写真の1枚目が先()
    {
        var paths = MakePaths(_root);
        var photo = Path.Combine(paths.ModificationImagesDir("m1"), "a.webp");
        Touch(photo);
        Touch(Path.Combine(paths.AvatarImagesDir("9900001"), "av.webp"));
        var record = new ModificationRecord
        {
            Id = "m1",
            AvatarItemId = "9900001",
            Name = "普段着",
            Images = [new ModificationImage { FileName = "a.webp" }],
        };

        Assert.Equal(photo, ModificationIcon.PathOf(paths, record, null));
    }

    [Fact]
    public void 写真が無ければアバターの絵_それも無ければ空()
    {
        var paths = MakePaths(_root);
        var avatarPicture = Path.Combine(paths.AvatarImagesDir("9900001"), "av.webp");
        var record = new ModificationRecord { Id = "m1", AvatarItemId = "9900001", Name = "普段着" };

        Assert.Null(ModificationIcon.PathOf(paths, record, null));

        Touch(avatarPicture);
        Assert.Equal(avatarPicture, ModificationIcon.PathOf(paths, record, null));
    }

    [Fact]
    public void 持っているアバターの絵は_星で指名したサムネイル_無ければ1枚目()
    {
        var paths = MakePaths(_root);
        var first = Path.Combine(paths.ItemImagesDir("9900001"), "a.webp");
        var pinned = Path.Combine(paths.ItemImagesDir("9900001"), "b.webp");
        Touch(first);
        Touch(pinned);
        var record = new ModificationRecord { Id = "m1", AvatarItemId = "9900001", Name = "普段着" };
        ItemRecord Avatar(string? thumbnail) => new()
        {
            Id = "9900001",
            Booth = new BoothBlock { Name = "作り物のアバター", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { ThumbnailImage = thumbnail },
        };

        Assert.Equal(first, ModificationIcon.PathOf(paths, record, Avatar(null)));
        Assert.Equal(pinned, ModificationIcon.PathOf(paths, record, Avatar("b.webp")));
    }

    [Fact]
    public void 写真の記録があってもファイルが無ければアバターの絵に戻る()
    {
        var paths = MakePaths(_root);
        var avatarPicture = Path.Combine(paths.AvatarImagesDir("9900001"), "av.webp");
        Touch(avatarPicture);
        var record = new ModificationRecord
        {
            Id = "m1",
            AvatarItemId = "9900001",
            Name = "普段着",
            Images = [new ModificationImage { FileName = "gone.webp" }],
        };

        Assert.Equal(avatarPicture, ModificationIcon.PathOf(paths, record, null));
    }
}
