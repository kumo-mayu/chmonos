using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class UnresolvedOriginTests
{
    private static UnresolvedFile File(string path, string? storedReferrer = null) => new()
    {
        Hash = "h",
        Paths = [path],
        SizeBytes = 1,
        ModifiedAtUtc = DateTimeOffset.UnixEpoch,
        FirstSeenAt = DateTimeOffset.UnixEpoch,
        ZoneReferrerUrl = storedReferrer,
    };

    /// <summary>実測の形。旧版は末尾の NUL を付けたまま保存していた。</summary>
    [Fact]
    public void TakesTheZipNameFromAnExtractionRecord()
    {
        var origin = UnresolvedOrigin.FromReferrer("E:\\storage\\VRChat_model\\Milfy_v1.5.0.zip\0");

        Assert.NotNull(origin);
        Assert.Equal("Milfy_v1.5.0.zip", origin.ArchiveName);
        Assert.Equal(@"E:\storage\VRChat_model\Milfy_v1.5.0.zip", origin.ArchivePath);
    }

    /// <summary>ブラウザで落としたファイルの ReferrerUrl は商品ページ。zipを名乗っていても展開元ではない。</summary>
    [Theory]
    [InlineData("https://booth.pm/ja/items/1234567")]
    [InlineData("https://example.com/download/sample.zip")]
    [InlineData(@"D:\dl\readme.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresValuesThatAreNotAnArchivePath(string? referrer)
    {
        Assert.Null(UnresolvedOrigin.FromReferrer(referrer));
    }

    /// <summary>名前が化けていると使えない。途中のフォルダだけが化けているなら名前は使える。</summary>
    [Fact]
    public void RejectsOnlyAGarbledArchiveName()
    {
        Assert.Null(UnresolvedOrigin.FromReferrer("D:\\Assets\\\uFFFDA\uFFFDo.zip"));

        var origin = UnresolvedOrigin.FromReferrer("D:\\Assets\\\uFFFDA\uFFFDo\\nukumo_pack_1.20.zip");
        Assert.Equal("nukumo_pack_1.20.zip", origin?.ArchiveName);
    }

    [Fact]
    public void AcceptsSharedFolderPaths()
    {
        Assert.Equal("a.7z", UnresolvedOrigin.FromReferrer(@"\\nas\share\a.7z")?.ArchiveName);
    }

    /// <summary>
    /// 保存した値で束ねる。展開の仕方で別々のフォルダに割れた中身も、ファイルが今そこに無くても、同じzipの束に入る。
    /// </summary>
    [Fact]
    public void GroupsByTheStoredValue()
    {
        var texture = UnresolvedOrigin.For(File(@"D:\dl\Outfit\tex\a.png", storedReferrer: @"E:\storage\Outfit_v1.zip"));
        var prefab = UnresolvedOrigin.For(File(@"F:\moved\prefab\b.prefab", storedReferrer: @"E:\storage\Outfit_v1.zip"));

        Assert.Equal("Outfit_v1.zip", texture?.ArchiveName);
        Assert.Equal(texture, prefab);
    }

    /// <summary>
    /// 今あるファイルの Zone.Identifier は読まない（ユーザ判断 2026-09-30）。前は保存した値より先に読み直していて、
    /// 未確定の画面を開くたび・ナビの札が数え直すたびに、未確定の件数ぶんディスクを読んでいた。
    /// ファイルの印が保存した値と違っていても、保存した値の方で決まる。
    /// </summary>
    [Fact]
    public void DoesNotReadTheFileOnDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bam-origin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "a.png");
            System.IO.File.WriteAllText(path, "x");
            System.IO.File.WriteAllText(
                path + ":Zone.Identifier",
                "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=C:\\dl\\OnDisk.zip\r\n");

            Assert.Equal("Stored.zip", UnresolvedOrigin.For(File(path, storedReferrer: @"C:\dl\Stored.zip"))?.ArchiveName);
            Assert.Null(UnresolvedOrigin.For(File(path)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>zipそのものが未確定なら、それ自身が束の単位。同じzipを展開した中身と同じ束に入る。</summary>
    [Fact]
    public void AnArchiveIsItsOwnOrigin()
    {
        var origin = UnresolvedOrigin.For(File(@"D:\dl\Tori_v1_1_1.zip"));

        Assert.Equal("Tori_v1_1_1.zip", origin?.ArchiveName);
        Assert.Equal(origin, UnresolvedOrigin.For(File(@"D:\dl\unpacked\a.png", storedReferrer: @"D:\dl\Tori_v1_1_1.zip")));
    }

    [Fact]
    public void ReturnsNullWhenNothingIsKnown()
    {
        Assert.Null(UnresolvedOrigin.For(File(@"D:\dl\a.png")));
    }

    /// <summary>場所を1つも持たない記録（手で直した JSON）でも落ちず、保存した値で決まる。</summary>
    [Fact]
    public void ARecordWithoutPathsStillUsesTheStoredValue()
    {
        var file = new UnresolvedFile
        {
            Hash = "h",
            Paths = [],
            SizeBytes = 1,
            ModifiedAtUtc = DateTimeOffset.UnixEpoch,
            FirstSeenAt = DateTimeOffset.UnixEpoch,
            ZoneReferrerUrl = @"E:\storage\Outfit_v1.zip",
        };

        Assert.Equal("Outfit_v1.zip", UnresolvedOrigin.For(file)?.ArchiveName);
    }
}
