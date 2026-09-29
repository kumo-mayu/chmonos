using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの「追加した」の足跡を溜めて書く。1件ずつ書くと recent.json の読み書きが件数の2乗になっていた。
/// 書く回数が件数に比例しないこと・書いた中身が1件ずつ書いたときと同じこと・間に打たれた閲覧を消さないこと・
/// 中止しても溜めた分を書くことを確かめる。時刻は決め打ちで、時計には頼らない。
/// </summary>
public sealed class RecentStampBufferTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-recent-buffer-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public RecentStampBufferTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    /// <summary>窓口の書き込みの数は1回で2つ進む（書き始めと書き終わり）。</summary>
    private int Writes => _store.Recent.WriteCount / 2;

    private Task TouchAsync(string itemId, RecentKind kind, DateTimeOffset at)
        => _store.Recent.UpdateAsync(log => new RecentLog { Entries = RecentActivity.Touch(log.Entries, itemId, kind, at) });

    [Fact]
    public void TouchAllMatchesTouchingOneByOne()
    {
        IReadOnlyList<RecentEntry> existing =
        [
            new RecentEntry { ItemId = "11", ViewedAt = At.AddDays(-3) },
            new RecentEntry { ItemId = "22", UsedAt = At.AddDays(-2) },
            new RecentEntry { ItemId = "22", AddedAt = At.AddDays(-9) }, // 手で直して重なった行。Touch は先の行を書き換える
        ];
        RecentStamp[] stamps =
        [
            new("33", RecentKind.Added, At),
            new("11", RecentKind.Added, At.AddMinutes(1)),
            new("22", RecentKind.Viewed, At.AddMinutes(2)),
            new("33", RecentKind.Added, At.AddMinutes(3)), // 同じ商品が2回。後の方が勝ち、行は初めの位置のまま
            new("44", RecentKind.Used, At.AddMinutes(4)),
            new("aa", RecentKind.Added, At.AddMinutes(5)),
            new("AA", RecentKind.Viewed, At.AddMinutes(6)), // 大文字小文字は同じ商品
        ];

        var oneByOne = stamps.Aggregate(existing, (entries, stamp) => RecentActivity.Touch(entries, stamp.ItemId, stamp.Kind, stamp.At));

        Assert.Equal(oneByOne, RecentActivity.TouchAll(existing, stamps));
    }

    [Fact]
    public async Task WritesOnceEveryFlushEveryAndOnFlush()
    {
        var buffer = new RecentStampBuffer(_store.Recent);
        var count = RecentStampBuffer.FlushEvery * 2 + 20;

        for (var index = 0; index < count; index++)
        {
            await buffer.AddAsync($"{1000 + index}", RecentKind.Added, At.AddSeconds(index));
        }

        Assert.Equal(2, Writes);
        Assert.Equal(20, buffer.PendingCount);

        await buffer.FlushAsync();
        await buffer.FlushAsync(); // 溜まっていなければ書かない

        Assert.Equal(3, Writes);
        Assert.Equal(0, buffer.PendingCount);

        var oneByOne = Enumerable.Range(0, count).Aggregate(
            (IReadOnlyList<RecentEntry>)[],
            (entries, index) => RecentActivity.Touch(entries, $"{1000 + index}", RecentKind.Added, At.AddSeconds(index)));
        Assert.Equal(oneByOne, _store.Recent.Load().Entries);
    }

    [Fact]
    public async Task KeepsViewsStampedWhileBuffering()
    {
        var buffer = new RecentStampBuffer(_store.Recent);
        await buffer.AddAsync("11", RecentKind.Added, At);

        // 溜めている間に、人が今足したばかりの商品と別の商品を開いた
        await TouchAsync("11", RecentKind.Viewed, At.AddMinutes(1));
        await TouchAsync("99", RecentKind.Viewed, At.AddMinutes(2));
        await buffer.AddAsync("22", RecentKind.Added, At.AddMinutes(3));

        await buffer.FlushAsync();

        var entries = _store.Recent.Load().Entries.ToDictionary(entry => entry.ItemId);
        Assert.Equal(At, entries["11"].AddedAt);
        Assert.Equal(At.AddMinutes(1), entries["11"].ViewedAt);
        Assert.Equal(At.AddMinutes(2), entries["99"].ViewedAt);
        Assert.Null(entries["99"].AddedAt);
        Assert.Equal(At.AddMinutes(3), entries["22"].AddedAt);
    }

    // ── 取り込み全体で ──

    private sealed class Handler(Func<string, int, Task>? onJson, int failAfter) : HttpMessageHandler
    {
        private int _json;
        private int _seen;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _seen) > failAfter)
            {
                throw new OperationCanceledException("ここで閉じた");
            }

            var url = request.RequestUri!.ToString();
            if (!url.EndsWith(".json", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") };
            }

            var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
            if (onJson is not null)
            {
                await onJson(id, Interlocked.Increment(ref _json));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""
                    { "id": {{id}}, "name": "商品 {{id}}",
                      "url": "https://booth.pm/ja/items/{{id}}",
                      "images": [], "variations": [] }
                    """),
            };
        }
    }

    private ImportPipeline Pipeline(Func<string, int, Task>? onJson = null, int failAfter = int.MaxValue)
    {
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        // 間隔は実際には待たない（120件の①②で6分かかる）
        var client = new BoothClient(new HttpClient(new Handler(onJson, failAfter)), settings, delay: (_, _) => Task.CompletedTask);
        return new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
    }

    private string CreateSource(int count)
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        for (var index = 0; index < count; index++)
        {
            var itemId = $"{5000 + index}";
            var path = Path.Combine(folder, $"item_{itemId}.zip");
            File.WriteAllText(path, itemId);
            File.WriteAllText(
                path + ":Zone.Identifier",
                $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{itemId}\r\n");
        }

        return folder;
    }

    /// <summary>
    /// 1回の取り込みで足跡を書く回数が、新しい商品の数に比例しない（1件ずつなら120回）。
    /// 足した商品にはどれも「追加した」が付き、取り込みの間に開いた商品の「閲覧」も残る。
    /// </summary>
    [Fact]
    public async Task AnImportWritesTheFootprintsInAFewGoes()
    {
        const int count = 120;
        await TouchAsync("1", RecentKind.Viewed, At); // 前からある足跡
        var before = Writes;

        var pipeline = Pipeline(onJson: async (_, nth) =>
        {
            // ①の途中で、人がもう足された商品と別の商品を開いた
            if (nth == 30)
            {
                await TouchAsync("5010", RecentKind.Viewed, At.AddMinutes(1));
                await TouchAsync("2", RecentKind.Viewed, At.AddMinutes(2));
            }
        });

        var summary = await pipeline.RunAsync([CreateSource(count)]);
        Assert.Equal(count, summary.ItemsAdded);

        // 50件ごとに2回と①の終わりに1回。閲覧の2回は人の分
        Assert.Equal(3 + 2, Writes - before);

        var entries = _store.Recent.Load().Entries.ToDictionary(entry => entry.ItemId);
        Assert.All(_store.Items.EnumerateItemIds(), itemId => Assert.NotNull(entries[itemId].AddedAt));
        Assert.Equal(At, entries["1"].ViewedAt);
        Assert.Equal(At.AddMinutes(1), entries["5010"].ViewedAt);
        Assert.NotNull(entries["5010"].AddedAt);
        Assert.Equal(At.AddMinutes(2), entries["2"].ViewedAt);
    }

    /// <summary>①の途中で閉じても、それまでに保存した商品の「追加した」は書いてから抜ける。</summary>
    [Fact]
    public async Task StoppingMidwayStillWritesWhatWasBuffered()
    {
        // 70件目までの①を通し、71件目で閉じる。50件目で1回書き、残りの20件は抜けるときに書く
        var pipeline = Pipeline(failAfter: 70);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.RunAsync([CreateSource(100)]));

        var saved = _store.Items.EnumerateItemIds();
        Assert.Equal(70, saved.Count);

        var entries = _store.Recent.Load().Entries.ToDictionary(entry => entry.ItemId);
        Assert.All(saved, itemId => Assert.NotNull(entries[itemId].AddedAt));
        Assert.Equal(70, entries.Count);
        Assert.Equal(2, Writes);
    }
}
