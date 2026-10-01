using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>起動時に対応アバターを検出し直すか（設定「対応アバターを検出し直す間隔」）。</summary>
public sealed class AvatarRedetectTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void 一度も検出していなければ検出する()
        => Assert.True(AvatarService.IsRedetectDue(null, 90, Now));

    [Fact]
    public void 間隔を過ぎていれば検出する()
        => Assert.True(AvatarService.IsRedetectDue(Now.AddDays(-90), 90, Now));

    [Fact]
    public void 間隔の内なら検出しない()
        => Assert.False(AvatarService.IsRedetectDue(Now.AddDays(-89), 90, Now));

    [Fact]
    public void 間隔が0以下でも1日は空ける()
        => Assert.False(AvatarService.IsRedetectDue(Now.AddHours(-1), 0, Now));
}
