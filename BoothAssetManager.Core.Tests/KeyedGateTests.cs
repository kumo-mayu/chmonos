using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 鍵ごとの錠（商品・ファイル）を、使い終わったら捨てる（2026-09-24）。前は触った鍵の数だけ錠が溜まり続けた。
/// **捨てても、同じ鍵を同時に持てる人は1人のまま**であることを確かめる。
/// </summary>
public sealed class KeyedGateTests
{
    [Fact]
    public async Task OnlyOneHolderPerKeyAndTheTableEmptiesAfterwards()
    {
        var gate = new KeyedGate<string>(StringComparer.Ordinal);
        var inside = 0;
        var most = 0;
        var totals = new int[2];

        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
        {
            for (var round = 0; round < 50; round++)
            {
                var handle = gate.For(index % 2 == 0 ? "偶数" : "奇数");
                await handle.WaitAsync();
                try
                {
                    var now = Interlocked.Increment(ref inside);
                    InterlockedMax(ref most, now);
                    var seen = totals[index % 2];
                    await Task.Yield();
                    totals[index % 2] = seen + 1; // 錠の中でなければ取りこぼす書き方
                    Interlocked.Decrement(ref inside);
                }
                finally
                {
                    handle.Release();
                }
            }
        })));

        Assert.Equal([32 * 50, 32 * 50], totals);
        Assert.True(most <= 2); // 鍵が2つなので、同時に中にいるのは多くて2人
        Assert.Equal(0, gate.Count);
    }

    /// <summary>待っている間に取り消した人の分も数から外れる（錠は持っていないので放さない）。</summary>
    [Fact]
    public async Task ACancelledWaiterDoesNotLeaveTheKeyBehind()
    {
        var gate = new KeyedGate<string>(StringComparer.Ordinal);
        var holder = gate.For("a");
        await holder.WaitAsync();

        using var cancel = new CancellationTokenSource();
        var waiter = gate.For("a");
        var waiting = waiter.WaitAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        Assert.Equal(1, gate.Count);
        holder.Release();
        Assert.Equal(0, gate.Count);

        // 捨てた後に同じ鍵をまた取れる
        var again = gate.For("a");
        await again.WaitAsync();
        again.Release();
        Assert.Equal(0, gate.Count);
    }

    /// <summary>見出しの正規化は計算で出せるので JSON に書かない。前に書かれたファイルに残っていても読める。</summary>
    [Fact]
    public void NormalizedHeadingIsNotWrittenAndOldFilesStillRead()
    {
        var section = new H2Section { Heading = "★ 更新履歴 ★", Text = "v1" };

        var json = JsonSerializer.Serialize(section, JsonStore.Options);
        var old = JsonSerializer.Deserialize<H2Section>(
            """{ "heading": "★ 更新履歴 ★", "normalizedHeading": "手で書いた別の値", "text": "v1" }""",
            JsonStore.Options)!;

        Assert.DoesNotContain("normalizedHeading", json, StringComparison.Ordinal);
        Assert.Equal("更新履歴", section.NormalizedHeading);
        Assert.Equal("更新履歴", old.NormalizedHeading);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
