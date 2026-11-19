using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品ページの「BOOTHでの変化の履歴」の元（ユーザ判断 2026-11-20）：今の知らせと保管庫の知らせを束ね、
/// 保管庫は目録で引いた位置の行だけを解く。
/// </summary>
public sealed class ItemChangeHistoryTests : IDisposable
{
    private const string ItemId = "9900701";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-change-history-" + Guid.NewGuid().ToString("N"));
    private readonly NotificationArchive _archive;

    public ItemChangeHistoryTests()
    {
        _archive = new NotificationArchive(Path.Combine(_root, NotificationArchive.DirName));
        Directory.CreateDirectory(_archive.Directory);
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

    private static DateTimeOffset At(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.FromHours(9));

    private static NotificationRecord Updated(DateTimeOffset createdAt, string itemId = ItemId, string detail = "価格") => new()
    {
        Id = $"item-updated:{itemId}",
        Kind = NotificationKind.ItemUpdated,
        ItemId = itemId,
        Title = "作り物の衣装",
        Detail = detail,
        Diffs = [new NotificationDiff { Field = "価格", Before = "¥ 500", After = "¥ 800" }],
        CreatedAt = createdAt,
        IsRead = true,
    };

    [Fact]
    public async Task 今の知らせと保管した知らせを新しい順に束ね_ほかの商品とほかの種類は入れない()
    {
        await _archive.AppendAsync(
        [
            Updated(At(9, 3), detail: "9月"),
            Updated(At(9, 4), itemId: "9900702"),
            Updated(At(10, 5), detail: "10月"),
            Updated(At(10, 6)) with { Id = $"variation-gone:{ItemId}", Kind = NotificationKind.OrphanVariationLink },
        ]);
        var current = new[] { Updated(At(11, 1), detail: "11月") with { IsRead = false } };

        var read = await ItemChangeHistory.ReadAsync(current, _archive, ItemId);

        Assert.Equal(["11月", "10月", "9月"], read.Records.Select(record => record.Detail));
        Assert.False(read.Records[0].IsRead);
        Assert.False(read.HasDamage);
    }

    /// <summary>保管庫に書けてから notifications.json から外す順なので、間で落ちると両方に残る。今の側（未読がありうる）を残す</summary>
    [Fact]
    public void 同じIDで同じ作った日時の知らせは1つにし_今の知らせを残す()
    {
        var current = Updated(At(10, 5), detail: "今の側");
        var archived = Updated(At(10, 5), detail: "保管庫の側");

        var merged = ItemChangeHistory.Merge([current], [archived], ItemId);

        Assert.Equal("今の側", Assert.Single(merged).Detail);
    }

    /// <summary>商品の更新の知らせは商品ごとに同じ ID。作った日時が違えば別の機会の更新なので両方並べる</summary>
    [Fact]
    public void 同じIDでも作った日時が違えば両方並べる_重ねた知らせは最後に変わった日時で並ぶ()
    {
        var stacked = Updated(At(9, 1), detail: "重ねた") with { UpdatedAt = At(10, 20) };
        var later = Updated(At(10, 10), detail: "後");

        var merged = ItemChangeHistory.Merge([], [later, stacked], ItemId);

        Assert.Equal(["重ねた", "後"], merged.Select(record => record.Detail));
    }

    /// <summary>商品ページと通知の画面が同時に目録を頼んでも、冷えた月を2本で流さない</summary>
    [Fact]
    public async Task 目録を同時に頼んでも_月は1回だけ流す()
    {
        await _archive.AppendAsync([Updated(At(9, 3))]);
        await _archive.AppendAsync([Updated(At(10, 3))]);

        var both = await Task.WhenAll(_archive.CatalogAsync(), _archive.CatalogAsync());

        Assert.Same(both[0], both[1]);
        Assert.Equal(2, _archive.IndexBuildsForTests);

        // 作り終えた後は控えを使う（変わっていない月は作り直さない）
        await _archive.CatalogAsync();
        Assert.Equal(2, _archive.IndexBuildsForTests);
    }
}
