using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品ページの「見つかりません」の見分け（点検 2026-09-30 の B：移したファイルが普通の行で出ていた）。
/// ディスクとドライブは関数で差し替え、実機のドライブに結果が左右されないようにする。
/// </summary>
public class LocalFilePresenceTests
{
    private static readonly Func<string, bool> NoDriveMissing = _ => false;

    [Fact]
    public void IsPresentWhenAnyRecordedPlaceHasTheFile()
    {
        var presence = LocalFilePresence.Of(
            [@"C:\old\a.zip", @"D:\new\a.zip"],
            fileExists: path => path.StartsWith(@"D:\", StringComparison.Ordinal),
            onMissingVolume: NoDriveMissing);

        Assert.Equal(FilePresence.Present, presence);
    }

    [Fact]
    public void IsMissingWhenTheFileWasMovedAway()
    {
        var presence = LocalFilePresence.Of([@"C:\old\a.zip"], fileExists: _ => false, onMissingVolume: NoDriveMissing);

        Assert.Equal(FilePresence.Missing, presence);
    }

    [Fact]
    public void IsMissingWhenNoPlaceIsRecorded()
    {
        Assert.Equal(FilePresence.Missing, LocalFilePresence.Of([], fileExists: _ => true, onMissingVolume: _ => true));
    }

    /// <summary>取り込みがパスを残すのと同じ考えで、外付けを外しているだけのファイルを「見つかりません」と言わない。</summary>
    [Fact]
    public void TellsApartAFileOnADetachedDrive()
    {
        var presence = LocalFilePresence.Of(
            [@"C:\old\a.zip", @"X:\external\a.zip"],
            fileExists: _ => false,
            onMissingVolume: path => path.StartsWith(@"X:\", StringComparison.Ordinal));

        Assert.Equal(FilePresence.OnDetachedDrive, presence);
    }

    [Fact]
    public void PrefersPresentOverADetachedDrive()
    {
        var presence = LocalFilePresence.Of(
            [@"X:\external\a.zip", @"C:\here\a.zip"],
            fileExists: path => path.StartsWith(@"C:\", StringComparison.Ordinal),
            onMissingVolume: path => path.StartsWith(@"X:\", StringComparison.Ordinal));

        Assert.Equal(FilePresence.Present, presence);
    }
}
