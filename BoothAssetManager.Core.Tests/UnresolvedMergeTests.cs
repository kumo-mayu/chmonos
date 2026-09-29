using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>取り込みが未確定の一覧を書くとき、人の変更と外付けの物を消さない（技術的負債 1-2・1-3、2026-09-14）。</summary>
public sealed class UnresolvedMergeTests
{
    private static readonly RegisteredFolderSet NoOffline = new([]);

    /// <summary>この取り込みが走査した取り込み元。<see cref="File"/> の既定のパスはこの下。</summary>
    private static readonly RegisteredFolderSet Scanned = new([@"D:\BOOTH"]);

    private static UnresolvedFile File(string hash, string path = @"D:\BOOTH\a.zip") => new()
    {
        Hash = hash,
        Paths = [path],
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        FirstSeenAt = DateTimeOffset.UnixEpoch,
    };

    private static string[] Hashes(IEnumerable<UnresolvedFile> files) => files.Select(file => file.Hash).Order().ToArray();

    [Fact]
    public void 取り込みの最中に人が割り当てた物を未確定に戻さない()
    {
        var result = UnresolvedMerge.ForImport(current: [], lastWritten: [File("A")], found: [File("A")], Scanned, NoOffline);

        Assert.Empty(result);
    }

    [Fact]
    public void 取り込みの最中に人が足した物を消さない()
    {
        var result = UnresolvedMerge.ForImport(current: [File("B")], lastWritten: [], found: [File("A")], Scanned, NoOffline);

        Assert.Equal(["A", "B"], Hashes(result));
    }

    [Fact]
    public void 今回見つからなかった物は取り込みが判じ直したので落とす()
    {
        var result = UnresolvedMerge.ForImport(current: [File("A")], lastWritten: [File("A")], found: [], Scanned, NoOffline);

        Assert.Empty(result);
    }

    [Fact]
    public void 外付けを外している取り込み元の下の物は残す()
    {
        var onExternal = File("A", @"Q:\BOOTH\a.zip");
        var result = UnresolvedMerge.ForImport(
            current: [onExternal],
            lastWritten: [onExternal],
            found: [],
            new RegisteredFolderSet([@"Q:\BOOTH"]),
            new RegisteredFolderSet([@"Q:\BOOTH"]));

        Assert.Equal(["A"], Hashes(result));
    }

    [Fact]
    public void 今回走査していない取り込み元の物は残す()
    {
        // 対象は「今積んだ物」だけ。別のフォルダを取り込んだだけで、前に取り込んだフォルダの未確定を消さない（大容量の確かめ E）
        var elsewhere = File("A", @"E:\Other\a.zip");
        var result = UnresolvedMerge.ForImport(current: [elsewhere], lastWritten: [elsewhere], found: [], Scanned, NoOffline);

        Assert.Equal(["A"], Hashes(result));
    }

    [Fact]
    public void 走査した取り込み元の中でも_区切りの途中で一致するだけの物は走査していないと見る()
    {
        // "D:\BOOTH" を走査しても "D:\BOOTH2" は見ていない
        var sibling = File("A", @"D:\BOOTH2\a.zip");
        var result = UnresolvedMerge.ForImport(current: [sibling], lastWritten: [sibling], found: [], Scanned, NoOffline);

        Assert.Equal(["A"], Hashes(result));
    }

    [Fact]
    public void 同じファイルは今回見つけた方を使う()
    {
        var fresh = File("A", @"D:\BOOTH\moved.zip");
        var result = UnresolvedMerge.ForImport(current: [File("A")], lastWritten: [File("A")], found: [fresh], Scanned, NoOffline);

        Assert.Same(fresh, Assert.Single(result));
    }

    [Fact]
    public void つながっていないボリュームだけを外したと見る()
    {
        // フォルダが消えただけ（ボリュームはある）なら、片付いたと判じてよい
        Assert.False(UnresolvedMerge.IsOnMissingVolume(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.True(UnresolvedMerge.IsOnMissingVolume(MissingVolumeFolder()));
    }

    /// <summary>この PC に無いドライブ文字のフォルダ。</summary>
    internal static string MissingVolumeFolder()
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            if (!Directory.Exists($@"{letter}:\"))
            {
                return $@"{letter}:\BOOTH";
            }
        }

        throw new InvalidOperationException("空いているドライブ文字がありません。");
    }
}
