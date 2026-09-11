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
        Assert.Null(UnresolvedOrigin.FromReferrer("E:\\VRChat\\\uFFFDA\uFFFDo.zip"));

        var origin = UnresolvedOrigin.FromReferrer("E:\\VRChat\\\uFFFDA\uFFFDo\\usasaki_2.41.zip");
        Assert.Equal("usasaki_2.41.zip", origin?.ArchiveName);
    }

    [Fact]
    public void AcceptsSharedFolderPaths()
    {
        Assert.Equal("a.7z", UnresolvedOrigin.FromReferrer(@"\\nas\share\a.7z")?.ArchiveName);
    }

    /// <summary>手元のファイルから読み直せたら、そちらを使う。保存値は旧版の読み取りで化けていることがある。</summary>
    [Fact]
    public void PrefersWhatTheFileSaysNowOverTheStoredValue()
    {
        var file = File(@"D:\dl\Milfy\tex\a.png", storedReferrer: "E:\\\uFFFD\\\uFFFD.zip");

        var origin = UnresolvedOrigin.For(file, _ => @"E:\アバター\Milfy_v1.5.0.zip");

        Assert.Equal("Milfy_v1.5.0.zip", origin?.ArchiveName);
    }

    /// <summary>ファイルが動かされて読めなくても、保存しておいた値で束ねられる。</summary>
    [Fact]
    public void FallsBackToTheStoredValue()
    {
        var file = File(@"D:\dl\Milfy\tex\a.png", storedReferrer: "E:\\storage\\Milfy_v1.5.0.zip\0");

        var origin = UnresolvedOrigin.For(file, _ => null);

        Assert.Equal("Milfy_v1.5.0.zip", origin?.ArchiveName);
    }

    /// <summary>zipそのものが未確定なら、それ自身が束の単位。同じzipを展開した中身と同じ束に入る。</summary>
    [Fact]
    public void AnArchiveIsItsOwnOrigin()
    {
        var origin = UnresolvedOrigin.For(File(@"D:\dl\Tori_v1_1_1.zip"), _ => throw new InvalidOperationException("読まない"));

        Assert.Equal("Tori_v1_1_1.zip", origin?.ArchiveName);
    }

    [Fact]
    public void ReturnsNullWhenNothingIsKnown()
    {
        Assert.Null(UnresolvedOrigin.For(File(@"D:\dl\a.png"), _ => null));
    }
}
