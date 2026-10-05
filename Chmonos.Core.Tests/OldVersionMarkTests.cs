using System.IO.Compression;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 販売者の更新版を同じ名前で上書きしたとき、場所の残らなかった古い中身の記録に「古い版」の印を付け、
/// 「見つかりません」の数（印・条件・統計・見回り・探す）から外すこと（2026-10-05・file-lifecycle.md 点検の8・ユーザ判断 8-A）。
///
/// 前は古い記録が場所の空のまま残り、その商品に「見つかりません」の印が付き続け、「探す」は毎回探して「見つかりませんでした」と数えていた。
/// </summary>
public class OldVersionMarkTests : IDisposable
{
    private const string ItemId = "9900001";

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;
    private readonly ItemService _service;

    public OldVersionMarkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-oldversion-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        // 手掛かりの無い zip だけを置くので、BOOTH へは行かない（行けば試験が落ちる）
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Refuse()), settings, TestWait.None);
        var images = new ImagePipeline(client, _paths, settings);
        _pipeline = new ImportPipeline(_store, client, images, settings);
        _service = new ItemService(_store, client, images);
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

    private sealed class Refuse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験は BOOTH へ問い合わせない。");
    }

    /// <summary>zip を置く（在れば上書き）。版ごとに中身の数が違うので、版が違えばハッシュも大きさも違う。</summary>
    private string PutZip(string name, int version)
    {
        var path = Path.Combine(_source, name);
        File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var index = 0; index <= version; index++)
        {
            var entry = archive.CreateEntry($"v{version}/file{index}.txt", CompressionLevel.NoCompression);

            // 中の時刻を決めておく。zip は各ファイルに作った時刻（2秒刻み）を持つので、そのままだと
            // 「同じ版を置き直す」試験で、作る間に時刻の区切りをまたぐと別の中身になり、まれに落ちていた
            entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(new byte[1024]);
        }

        return path;
    }

    private static async Task<LocalFileRecord> RecordOfAsync(string path) => new()
    {
        Hash = await FileHasher.ComputeSha256Async(path),
        Paths = [path],
        SizeBytes = new FileInfo(path).Length,
    };

    private Task SaveItemAsync(params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "試験の商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    private async Task<ItemRecord> ItemAsync() => (await _store.Items.LoadAsync(ItemId))!;

    private Task ImportAsync() => _pipeline.RunAsync(new ImportWorkSet([_source]));

    /// <summary>手で結んだ v1 を v2 で上書きして取り込んだ状態を作り、古い版のハッシュを返す。</summary>
    private async Task<(string Path, string OldHash)> OverwriteAsync()
    {
        var path = PutZip("pack.zip", version: 1);
        var record = await RecordOfAsync(path);
        await SaveItemAsync(record);
        PutZip("pack.zip", version: 2);
        await ImportAsync();
        return (path, record.Hash);
    }

    [Fact]
    public async Task 上書きで場所が残らない記録は古い版と印が付き_見つかりませんに数えない()
    {
        var before = DateTimeOffset.Now.AddSeconds(-1);
        var (path, _) = await OverwriteAsync();

        var item = await ItemAsync();
        var old = Assert.Single(item.Local.LocalFiles);
        Assert.Empty(old.Paths);
        Assert.True(old.IsOldVersion);
        Assert.Equal(path, old.Replaced!.Path);
        Assert.True(old.Replaced.At >= before);
        Assert.Null(old.MissingSince);
        Assert.False(item.HasMissingFile);

        // 人が読める欄として書く。計算で出せる「古い版か」は書かない
        var json = File.ReadAllText(Path.Combine(_paths.ItemsDir, ItemId + ".json"));
        Assert.Contains("\"replaced\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isOldVersion", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ほかの場所に同じ中身が残れば古い版の印は付かない()
    {
        var path = PutZip("pack.zip", version: 1);
        var copy = Path.Combine(_source, "copy.zip");
        File.Copy(path, copy);
        await SaveItemAsync(await RecordOfAsync(path) with { Paths = [path, copy] });

        PutZip("pack.zip", version: 2);
        await ImportAsync();

        var kept = Assert.Single((await ItemAsync()).Local.LocalFiles);
        Assert.Equal([copy], kept.Paths);
        Assert.Null(kept.Replaced);
    }

    [Fact]
    public async Task 古い版の中身をまた取り込むと印が下りる()
    {
        await OverwriteAsync();

        // 古い版を別の名前で置き直す（取っておいた写しを戻した）
        var back = PutZip("pack-old.zip", version: 1);
        await ImportAsync();

        var files = (await ItemAsync()).Local.LocalFiles;
        var old = files.Single(file => file.Paths.Contains(back));
        Assert.Null(old.Replaced);
        Assert.False(old.IsOldVersion);
    }

    [Fact]
    public void 見回りは古い版に見つからなくなった日時を付けない()
    {
        var old = new LocalFileRecord
        {
            Hash = "CD" + new string('0', 62),
            Paths = [],
            SizeBytes = 10,
            Replaced = new ReplacedVersion(@"D:\DL\pack.zip", DateTimeOffset.Now),
        };

        var applied = FileMissingMarks.Apply(
            [old],
            [new FileSighting(old.Hash, old.Paths, FilePresence.Missing)],
            DateTimeOffset.Now);

        Assert.Null(applied);
    }

    [Fact]
    public async Task 見つからないファイルを探すは古い版を探さない()
    {
        await OverwriteAsync();

        var result = await new MissingFileFinder(_store).FindAsync([_source]);

        Assert.Equal(0, result.MissingBefore);
    }

    [Fact]
    public async Task 片付けると古い版の行だけが消え_見つからない記録は残る()
    {
        var (_, oldHash) = await OverwriteAsync();
        var missing = new LocalFileRecord { Hash = "AB" + new string('0', 62), Paths = [], SizeBytes = 10 };
        await _store.Items.ChangeLocalAsync(
            ItemId,
            current => current with { LocalFiles = [.. current.LocalFiles, missing] },
            [LocalField.LocalFiles]);

        Assert.True(await _service.ForgetOldVersionAsync(ItemId, oldHash));

        var item = await ItemAsync();
        Assert.Equal(missing.Hash, Assert.Single(item.Local.LocalFiles).Hash);
        Assert.True(item.HasMissingFile);

        // 古い版でない記録は、同じ命令で消さない
        Assert.False(await _service.ForgetOldVersionAsync(ItemId, missing.Hash));
    }
}
