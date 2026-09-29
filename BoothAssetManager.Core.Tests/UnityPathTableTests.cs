using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// unitypackage の中のパスを覚える表（2026-09-24）。前は5000件まで覚えて丸ごと忘れていたので、大きな物が並ぶと何百MBでも握れた。
/// 今は合計の大きさで上限を決め、古く使った物から捨てる。
/// </summary>
public sealed class UnityPathTableTests
{
    private static readonly DateTime Written = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<UnityPackageAsset> Paths(int count)
        => [.. Enumerable.Range(0, count).Select(index => new UnityPackageAsset(index.ToString("x32"), $"Assets/Shop/Item/{index:0000}.prefab"))];

    private static (string, string) Key(string name) => ($@"D:\{name}.ZIP", "a.unitypackage");

    [Fact]
    public void DropsTheLeastRecentlyUsedWhenOverTheBudget()
    {
        // 1件あたり約 ((26 + 32) 文字 × 2 + 80) × 100 ≒ 20KB。上限 45KB なら2件まで
        var table = new UnityHandoff.PathTable(budgetBytes: 45_000);
        table.Put(Key("a"), 1, Written, Paths(100));
        table.Put(Key("b"), 1, Written, Paths(100));
        Assert.NotNull(table.TryGet(Key("a"), 1, Written)); // a を使ったので b の方が古い

        table.Put(Key("c"), 1, Written, Paths(100));

        Assert.NotNull(table.TryGet(Key("a"), 1, Written));
        Assert.Null(table.TryGet(Key("b"), 1, Written));
        Assert.NotNull(table.TryGet(Key("c"), 1, Written));
        Assert.True(table.Bytes <= 45_000);
    }

    [Fact]
    public void DoesNotKeepAnEntryLargerThanTheWholeBudget()
    {
        var table = new UnityHandoff.PathTable(budgetBytes: 45_000);
        table.Put(Key("small"), 1, Written, Paths(10));

        table.Put(Key("huge"), 1, Written, Paths(1000));

        Assert.Null(table.TryGet(Key("huge"), 1, Written));
        Assert.NotNull(table.TryGet(Key("small"), 1, Written));
    }

    /// <summary>zip の大きさか更新時刻が変わったら、覚えた物は使わない（読み直す）。</summary>
    [Fact]
    public void IgnoresEntriesWhoseZipChanged()
    {
        var table = new UnityHandoff.PathTable(budgetBytes: 1_000_000);
        table.Put(Key("a"), 1, Written, Paths(3));

        Assert.Null(table.TryGet(Key("a"), 2, Written));
        Assert.Null(table.TryGet(Key("a"), 1, Written.AddSeconds(1)));
        Assert.Equal(3, table.TryGet(Key("a"), 1, Written)!.Count);
    }

    [Fact]
    public void ReplacingAnEntryDoesNotCountItTwice()
    {
        var table = new UnityHandoff.PathTable(budgetBytes: 1_000_000);
        table.Put(Key("a"), 1, Written, Paths(10));
        var once = table.Bytes;

        table.Put(Key("a"), 2, Written, Paths(10));

        Assert.Equal(once, table.Bytes);
        Assert.Equal(1, table.Count);
    }
}
