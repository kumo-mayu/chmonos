using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保管した通知を読む画面の読む口（ユーザ案 2026-11-10）：月を丸ごと読まずに、目録を作って新しい順に何件かずつ中身を読む。
/// 50MB の月を丸ごと読むと作業セットが 359MB になったため。
/// </summary>
public sealed class NotificationArchivePagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-archive-pages-" + Guid.NewGuid().ToString("N"));
    private readonly NotificationArchive _archive;

    public NotificationArchivePagesTests()
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

    private static DateTimeOffset At(int day, int minute = 0) => new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.FromHours(9)).AddMinutes(minute);

    private static NotificationRecord Record(string id, DateTimeOffset createdAt, string title = "作り物の衣装") => new()
    {
        Id = id,
        Kind = NotificationKind.ItemUpdated,
        ItemId = "9900501",
        Title = title,
        Detail = "価格：500 → 800",
        CreatedAt = createdAt,
        IsRead = true,
    };

    private async Task<IReadOnlyList<string>> PageAsync(NotificationArchiveIndex index, IReadOnlyList<NotificationArchiveEntry> entries, int skip, int take)
        => (await _archive.ReadEntriesAsync(index, entries.Skip(skip).Take(take).ToList())).Select(record => record.Id).ToList();

    [Fact]
    public async Task 新しい順に並び_何件かずつ続きを読める()
    {
        // 足した順と日時の順をずらす（並びがファイルの位置ではなく日時で決まることを見る）
        await _archive.AppendAsync(Enumerable.Range(0, 120).Select(i => Record($"a{i:D3}", At(1, i))).ToList());
        await _archive.AppendAsync(Enumerable.Range(0, 130).Select(i => Record($"b{i:D3}", At(1, i).AddSeconds(30))).ToList());

        var index = await _archive.IndexAsync("2026-10");

        Assert.Equal(250, index.Newest.Count);
        var all = new List<string>();
        for (var skip = 0; skip < 250; skip += 100)
        {
            var page = await PageAsync(index, index.Newest, skip, 100);
            Assert.Equal(Math.Min(100, 250 - skip), page.Count);
            all.AddRange(page);
        }

        var expected = Enumerable.Range(0, 130).Select(i => (Id: $"b{i:D3}", At: At(1, i).AddSeconds(30)))
            .Concat(Enumerable.Range(0, 120).Select(i => (Id: $"a{i:D3}", At: At(1, i))))
            .OrderByDescending(pair => pair.At)
            .Select(pair => pair.Id);
        Assert.Equal(expected, all);
        Assert.Equal(["b120", "b119", "a119", "b118", "a118"], all.Skip(9).Take(5));
        Assert.Empty(await PageAsync(index, index.Newest, 250, 100));
    }

    [Fact]
    public async Task 並びは最後に変わった日時で_同じ日時は後から保管した物が先()
    {
        await _archive.AppendAsync(
        [
            Record("old-but-updated", At(1)) with { UpdatedAt = At(20) },
            Record("same-1", At(10)),
            Record("same-2", At(10)),
            Record("newer", At(15)),
        ]);

        var index = await _archive.IndexAsync("2026-10");

        Assert.Equal(["old-but-updated", "newer", "same-2", "same-1"], await PageAsync(index, index.Newest, 0, 100));
    }

    /// <summary>同じ知らせが2回足されても1件（足す途中で落ちると、次にもう一度足される）。区切りをまたいでも2回出ない。</summary>
    [Fact]
    public async Task 同じ知らせが2回あっても1件で_区切りをまたいでも2回出ない()
    {
        await _archive.AppendAsync(Enumerable.Range(0, 5).Select(i => Record($"r{i}", At(1, i))).ToList());
        await _archive.AppendAsync(Enumerable.Range(0, 5).Select(i => Record($"r{i}", At(1, i))).ToList());
        await _archive.AppendAsync([Record("r5", At(1, 5))]);

        var index = await _archive.IndexAsync("2026-10");

        Assert.Equal(6, index.Newest.Count);
        var pages = new List<string>();
        for (var skip = 0; skip < 6; skip += 4)
        {
            pages.AddRange(await PageAsync(index, index.Newest, skip, 4));
        }

        Assert.Equal(["r5", "r4", "r3", "r2", "r1", "r0"], pages);

        // 前の物を残す（ReadAsync と同じ）：後ろの写しの位置は目録に入らない
        Assert.All(index.Newest.Where(entry => entry.Id != "r5"), entry => Assert.InRange(entry.Ordinal, 0, 4));
    }

    /// <summary>
    /// 同じ商品の別の機会の更新は、同じ ID（item-updated:{ID}）・別の作った日時で控えに入る。両方を目録に並べて中身も読める。
    /// 片方が2回書かれていても（足す途中で落ちた）3件にしない（2026-11-13 の点検 件7）。
    /// </summary>
    [Fact]
    public async Task 同じIDで作った日時の違う知らせは_両方を目録に並べて中身も読める()
    {
        var first = Record("item-updated:9900501", At(1)) with { Detail = "1回目の更新" };
        var second = Record("item-updated:9900501", At(3)) with { Detail = "2回目の更新" };
        await _archive.AppendAsync([first]);
        await _archive.AppendAsync([first, second]);

        var index = await _archive.IndexAsync("2026-10");
        var records = await _archive.ReadEntriesAsync(index, index.Newest);

        Assert.Equal(["2回目の更新", "1回目の更新"], records.Select(record => record.Detail));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("header")]
    [InlineData("truncate")]
    public async Task 壊れたかたまりは飛ばし_後ろのかたまりは読める(string damage)
    {
        var path = _archive.FileFor("2026-10");
        var starts = new List<long>();
        var random = new Random(20261111);
        for (var i = 0; i < 9; i++)
        {
            starts.Add(File.Exists(path) ? new FileInfo(path).Length : 0);
            var detail = new byte[16];
            random.NextBytes(detail);
            await _archive.AppendAsync([Record($"r{i}", At(1, i)) with { Detail = Convert.ToHexString(detail) }]);
        }

        starts.Add(new FileInfo(path).Length);
        NotificationArchiveTests.Damage(path, starts[4], starts[5], damage);

        var index = await _archive.IndexAsync("2026-10");

        Assert.Equal(1, index.UnreadableBlocks);
        Assert.Equal(["r8", "r7", "r6", "r5", "r3", "r2", "r1", "r0"], await PageAsync(index, index.Newest, 0, 100));

        // 正確な読みでも位置がずれず、続きの区切りでも同じ物が出る
        Assert.Equal(["r3", "r2"], await PageAsync(index, index.Newest, 4, 2));
    }

    [Fact]
    public async Task 読めない行は飛ばして数える()
    {
        var good = JsonSerializer.Serialize(Record("good", At(1)), JsonStore.Options).Replace("\r", string.Empty).Replace("\n", string.Empty);
        var text = new StringBuilder()
            .Append($"{{\"formatVersion\":{StoreFormat.Current}}}\n")
            .Append(good).Append('\n')
            .Append(good.Replace("itemUpdated", "somethingNew").Replace("\"good\"", "\"unknown-kind\"")).Append('\n')
            .Append("{壊れた行\n")
            .Append($"{{\"formatVersion\":{StoreFormat.Current + 1}}}\n")
            .Append(good.Replace("\"good\"", "\"newer\"")).Append('\n')
            .ToString();
        await using (var file = File.Create(_archive.FileFor("2026-10")))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(text));
        }

        var index = await _archive.IndexAsync("2026-10");

        Assert.Equal(["good"], index.Newest.Select(entry => entry.Id));
        Assert.Equal(3, index.UnreadableLines);
        Assert.Equal(["good"], await PageAsync(index, index.Newest, 0, 100));
    }

    [Fact]
    public async Task 商品名で絞ると_大文字小文字とかなの種類と全角半角を区別せず_新しい順のまま()
    {
        await _archive.AppendAsync(
        [
            Record("dress-1", At(1), "ふわふわドレス Ver2"),
            Record("hair", At(2), "作り物の髪型"),
            Record("dress-2", At(3), "フワフワどれす　ｖｅｒ２"),
            Record("dress-3", At(4), "FUWAFUWA ドレス"),
        ]);

        var index = await _archive.IndexAsync("2026-10");
        var japanese = CultureInfo.GetCultureInfo("ja-JP");

        Assert.Equal(["dress-2", "dress-1"], index.Matching("ふわふわドレス", japanese).Select(entry => entry.Id));
        Assert.Equal(["dress-2", "dress-1"], index.Matching(" VER2 ", japanese).Select(entry => entry.Id));
        Assert.Equal(["dress-3"], index.Matching("fuwafuwa", japanese).Select(entry => entry.Id));
        Assert.Empty(index.Matching("靴", japanese));
        Assert.Equal(4, index.Matching("  ", japanese).Count);

        var matched = index.Matching("ドレス", japanese);
        Assert.Equal(["dress-3", "dress-2"], await PageAsync(index, matched, 0, 2));
        Assert.Equal(["dress-1"], await PageAsync(index, matched, 2, 2));
    }

    [Fact]
    public async Task 月のファイルが無ければ空()
    {
        var index = await _archive.IndexAsync("2026-10");

        Assert.Empty(index.Newest);
        Assert.Equal(0, index.UnreadableLines);
        Assert.Empty(await _archive.ReadEntriesAsync(index, [new NotificationArchiveEntry(0, "a", "題", At(1))]));
    }

    /// <summary>目録の後で月が書き直された（閉じるときの整理）ら、その位置にある別の知らせは出さない。</summary>
    [Fact]
    public async Task 目録の後で月が書き直されたら_位置の違う知らせは出さない()
    {
        await _archive.AppendAsync(Enumerable.Range(0, 6).Select(i => Record($"r{i}", At(1, i))).ToList());
        var index = await _archive.IndexAsync("2026-10");

        File.Delete(_archive.FileFor("2026-10"));
        await _archive.AppendAsync(Enumerable.Range(3, 3).Select(i => Record($"r{i}", At(1, i))).ToList());

        var page = await PageAsync(index, index.Newest, 0, 100);

        Assert.Empty(page);
    }
}
