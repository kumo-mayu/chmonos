using System.IO.Compression;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品が持つファイルを別の取り込み元へ移したら、次の取り込みで新しい場所へ結び直る（大容量の確かめ A・2026-09-30）。
///
/// 同一性はハッシュなので、手掛かりが無くても、同じ中身を持つ商品が分かっていれば場所を足せる。
/// 前は手掛かりから決まらない物（未確定で手で結んだ物）を「もう持っている」として黙って飛ばし、
/// 商品の記録は古い場所のまま「見つからない」になっていた。
/// </summary>
public sealed class MovedFileRelinkTests : IDisposable
{
    private const string BoothItemId = "7654321";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public MovedFileRelinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-relink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 結び直すのは手元の JSON だけで済む。BOOTH へ行ったら失敗させる
        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new OfflineHandler()), settings, TestWait.None);
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, paths, settings), settings);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
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

        GC.SuppressFinalize(this);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験では BOOTH へ行かないはず");
    }

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>手掛かりの無いファイル。</summary>
    private static string PlainFile(string folder, string name, string body)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, body);
        return path;
    }

    /// <summary>中の文章に商品のURLを持つ zip（手掛かりで商品が決まる）。</summary>
    private static string ZipWithItemUrl(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
        writer.Write($"購入ページ https://booth.pm/ja/items/{BoothItemId}");
        return path;
    }

    private static string Move(string path, string folder)
    {
        var moved = Path.Combine(folder, Path.GetFileName(path));
        File.Move(path, moved);
        return moved;
    }

    /// <summary>未確定から「この名前で登録する」で商品にしたファイル。</summary>
    private async Task<(string ItemId, string Hash)> RegisterAsLocalItemAsync(string folder)
    {
        await _pipeline.RunAsync(new ImportWorkSet([folder]));
        var unresolved = Assert.Single(_store.Unresolved.Load());
        var itemId = await _service.RegisterLocalItemAsync(unresolved.Hash, "名前を付けた商品");
        Assert.NotNull(itemId);
        return (itemId, unresolved.Hash);
    }

    private async Task<LocalFileRecord> FileOfAsync(string itemId, string hash)
    {
        var item = await _store.Items.LoadAsync(itemId);
        Assert.NotNull(item);
        return Assert.Single(item.Local.LocalFiles, file => file.Hash == hash);
    }

    [Fact]
    public async Task 手で結んだファイルを移すと_次の取り込みで新しい場所が足され古い場所が落ちる()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = PlainFile(before, "mystery.zip", "no clue here");
        var (itemId, hash) = await RegisterAsLocalItemAsync(before);

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        var record = await FileOfAsync(itemId, hash);
        Assert.Equal([moved], record.Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    [Fact]
    public async Task 写しを別の場所に置いたら_両方の場所を持つ()
    {
        var before = Folder("before");
        var copies = Folder("copies");
        var original = PlainFile(before, "mystery.zip", "no clue here");
        var (itemId, hash) = await RegisterAsLocalItemAsync(before);

        var copy = Path.Combine(copies, "mystery.zip");
        File.Copy(original, copy);
        await _pipeline.RunAsync(new ImportWorkSet([copies]));

        var record = await FileOfAsync(itemId, hash);
        Assert.Equal([original, copy], record.Paths);
    }

    [Fact]
    public async Task 商品IDで決まるファイルを移しても_同じ商品へ結び直る()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = ZipWithItemUrl(before, "outfit.zip");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        // 取得済みの商品（説明も置いてあるので②にも行かない）
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = BoothItemId,
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = new FileInfo(original).Length }] },
        });
        await File.WriteAllTextAsync(_store.Paths.ItemHtmlFile(BoothItemId), "");

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        var record = await FileOfAsync(BoothItemId, hash);
        Assert.Equal([moved], record.Paths);
    }

    [Fact]
    public async Task 外した印のファイルは移しても結び直さず_未確定に出す()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = PlainFile(before, "detached.zip", "detached body");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = BoothItemId,
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1, Detached = true }],
            },
        });

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        var record = await FileOfAsync(BoothItemId, hash);
        Assert.True(record.Detached);
        Assert.Equal([original], record.Paths);
        Assert.Equal([moved], Assert.Single(_store.Unresolved.Load()).Paths);
    }

    /// <summary>
    /// 同じ中身を2つの商品が持つなら、両方へ足す。どちらの物かは人が決めたことで、場所はその中身の場所だから。
    /// </summary>
    [Fact]
    public async Task 同じ中身を2つの商品が持つなら_両方へ新しい場所を足す()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = PlainFile(before, "shared.zip", "shared body");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        foreach (var id in new[] { "local-aaaa0001", "local-aaaa0002" })
        {
            await _store.Items.SaveAsync(new ItemRecord
            {
                Id = id,
                Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 }] },
            });
        }

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([moved], (await FileOfAsync("local-aaaa0001", hash)).Paths);
        Assert.Equal([moved], (await FileOfAsync("local-aaaa0002", hash)).Paths);
    }

    /// <summary>取得済みの商品（説明も置いてあるので②にも行かない）。</summary>
    private async Task SaveFetchedItemAsync(string itemId, params LocalFileRecord[] files)
    {
        await _store.Items.SaveAsync(new ItemRecord { Id = itemId, Local = new LocalBlock { LocalFiles = [.. files] } });
        await File.WriteAllTextAsync(_store.Paths.ItemHtmlFile(itemId), "");
    }

    /// <summary>
    /// 手掛かりで片方の商品に決まっても、同じ中身を手で結んだもう一方の商品にも新しい場所を足す（ユーザ判断 2026-09-30）。
    /// 前は決まった商品にだけ足し、もう一方は古い場所のまま「見つかりません」になっていた。
    /// </summary>
    [Fact]
    public async Task 手掛かりで片方に決まっても_同じ中身を持つほかの商品へ新しい場所を足す()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = ZipWithItemUrl(before, "outfit.zip");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        await SaveFetchedItemAsync(BoothItemId, new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-aaaa0001",
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 }] },
        });

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([moved], (await FileOfAsync(BoothItemId, hash)).Paths);
        Assert.Equal([moved], (await FileOfAsync("local-aaaa0001", hash)).Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>手掛かりの商品がまだそのファイルを持っていなくても（後から手掛かりが効いた）、持っている商品の場所は新しくする。</summary>
    [Fact]
    public async Task 手掛かりの商品が持っていなくても_持っているほかの商品へ新しい場所を足す()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = ZipWithItemUrl(before, "outfit.zip");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        await SaveFetchedItemAsync(BoothItemId);
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-aaaa0001",
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 }] },
        });

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([moved], (await FileOfAsync(BoothItemId, hash)).Paths);
        Assert.Equal([moved], (await FileOfAsync("local-aaaa0001", hash)).Paths);
    }

    [Fact]
    public async Task 手掛かりで片方に決まっても_外した印の商品には足さない()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = ZipWithItemUrl(before, "outfit.zip");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        await SaveFetchedItemAsync(BoothItemId, new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-aaaa0001",
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1, Detached = true }],
            },
        });

        var moved = Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([moved], (await FileOfAsync(BoothItemId, hash)).Paths);
        var detached = await FileOfAsync("local-aaaa0001", hash);
        Assert.True(detached.Detached);
        Assert.Equal([original], detached.Paths);
    }

    [Fact]
    public async Task 手掛かりで片方に決まっても_管理から外した中身はどの商品にも足さない()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = ZipWithItemUrl(before, "outfit.zip");
        var hash = await FileHasher.ComputeSha256Async(original, CancellationToken.None);

        await SaveFetchedItemAsync(BoothItemId, new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-aaaa0001",
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [original], SizeBytes = 1 }] },
        });
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = hash, Paths = [original], ExcludedAt = DateTimeOffset.UnixEpoch }]);

        Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([original], (await FileOfAsync(BoothItemId, hash)).Paths);
        Assert.Equal([original], (await FileOfAsync("local-aaaa0001", hash)).Paths);
    }

    [Fact]
    public async Task 管理から外した中身は_移しても商品に足さない()
    {
        var before = Folder("before");
        var after = Folder("after");
        var original = PlainFile(before, "excluded.zip", "excluded body");
        var (itemId, hash) = await RegisterAsLocalItemAsync(before);

        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = hash, Paths = [original], ExcludedAt = DateTimeOffset.UnixEpoch }]);

        Move(original, after);
        await _pipeline.RunAsync(new ImportWorkSet([after]));

        Assert.Equal([original], (await FileOfAsync(itemId, hash)).Paths);
    }
}
