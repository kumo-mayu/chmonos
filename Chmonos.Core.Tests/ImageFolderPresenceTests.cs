using Chmonos.Core.Images;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 検索の読み直しで、画像のフォルダの日時と「絵が1枚でもあるか」を見る所。
/// 日時はファイルに直接書き込むか作り物を渡すので、時計には左右されない。
/// </summary>
public class ImageFolderPresenceTests : IDisposable
{
    private readonly string _root;

    public ImageFolderPresenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-imgpresence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

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

    private static readonly DateTime T1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>問い合わせを1回にしても、前の求め方（あるかを見てから日時）と同じ答えになる。</summary>
    [Fact]
    public void StampMatchesCheckingExistenceFirst()
    {
        var folder = Path.Combine(_root, "1");
        Directory.CreateDirectory(folder);
        Directory.SetLastWriteTimeUtc(folder, T1);

        Assert.Equal(T1, ImageFolderPresence.Stamp(folder));
        Assert.Equal(Directory.GetLastWriteTimeUtc(folder), ImageFolderPresence.Stamp(folder));
    }

    /// <summary>無いフォルダは最小値（作られたら変わったと分かる）。1601-01-01 のまま返さない。</summary>
    [Fact]
    public void StampOfAMissingFolderIsTheMinimum()
    {
        Assert.Equal(DateTime.MinValue, ImageFolderPresence.Stamp(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void HasAnyImageLooksForWebp()
    {
        var empty = Path.Combine(_root, "empty");
        var other = Path.Combine(_root, "other");
        var with = Path.Combine(_root, "with");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(other);
        Directory.CreateDirectory(with);
        File.WriteAllBytes(Path.Combine(other, "note.txt"), [1]);
        File.WriteAllBytes(Path.Combine(with, "0.webp"), [1]);

        Assert.False(ImageFolderPresence.HasAnyImage(empty));
        Assert.False(ImageFolderPresence.HasAnyImage(other));
        Assert.True(ImageFolderPresence.HasAnyImage(with));
        Assert.False(ImageFolderPresence.HasAnyImage(Path.Combine(_root, "missing")));
    }

    /// <summary>前に調べていない商品は全部調べる（最初の読み直し）。</summary>
    [Fact]
    public void ProbesEverythingTheFirstTime()
    {
        var probed = new List<string>();

        var (missing, memo) = ImageFolderPresence.FindWithoutImages(
            [("a", T1), ("b", T1)],
            new Dictionary<string, (DateTime, bool)>(),
            id =>
            {
                probed.Add(id);
                return id == "a";
            });

        Assert.Equal(["a", "b"], probed);
        Assert.Equal(["b"], missing);
        Assert.Equal((T1, true), memo["a"]);
        Assert.Equal((T1, false), memo["b"]);
    }

    /// <summary>日時が前と同じ商品は前の答えを使い、変わった商品だけ調べ直す。</summary>
    [Fact]
    public void ProbesOnlyFoldersWhoseStampChanged()
    {
        var previous = new Dictionary<string, (DateTime Stamp, bool HasImage)>(StringComparer.Ordinal)
        {
            ["same-without"] = (T1, false),
            ["same-with"] = (T1, true),
            ["changed"] = (T1, false),
        };
        var probed = new List<string>();

        var (missing, memo) = ImageFolderPresence.FindWithoutImages(
            [("same-without", T1), ("same-with", T1), ("changed", T2), ("new", T1)],
            previous,
            id =>
            {
                probed.Add(id);
                return true;
            });

        Assert.Equal(["changed", "new"], probed);
        Assert.Equal(["same-without"], missing);
        Assert.Equal((T2, true), memo["changed"]);
    }

    /// <summary>今回調べなかった商品（画像の無くなった商品・消えた商品）は覚えを持ち越さない。</summary>
    [Fact]
    public void ForgetsItemsNotAskedAbout()
    {
        var previous = new Dictionary<string, (DateTime Stamp, bool HasImage)>(StringComparer.Ordinal)
        {
            ["gone"] = (T1, false),
        };

        var (_, memo) = ImageFolderPresence.FindWithoutImages([("a", T1)], previous, _ => true);

        Assert.False(memo.ContainsKey("gone"));
    }

    /// <summary>実のフォルダで：絵を足すとフォルダの日時が変わり、前の答え（無い）を使わずに調べ直す。</summary>
    [Fact]
    public void NoticesAnImageAddedToTheFolder()
    {
        // 足した瞬間の日時と重ならないよう、ずっと前の日時にしておく
        var folder = Path.Combine(_root, "item");
        Directory.CreateDirectory(folder);
        Directory.SetLastWriteTimeUtc(folder, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var first = ImageFolderPresence.FindWithoutImages(
            [("item", ImageFolderPresence.Stamp(folder))],
            new Dictionary<string, (DateTime, bool)>(),
            _ => ImageFolderPresence.HasAnyImage(folder));
        Assert.Contains("item", first.Missing);

        File.WriteAllBytes(Path.Combine(folder, "0.webp"), [1]);

        var second = ImageFolderPresence.FindWithoutImages(
            [("item", ImageFolderPresence.Stamp(folder))],
            first.Memo,
            _ => ImageFolderPresence.HasAnyImage(folder));
        Assert.Empty(second.Missing);
    }
}
