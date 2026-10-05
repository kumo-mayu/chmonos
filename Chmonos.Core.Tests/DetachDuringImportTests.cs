using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 取り込みの最中に人が「この商品から外す」を押しても、取り込みが外した印を下ろさない（2026-10-05・見つからない・移動の点検 1）。
///
/// 取り込みは読んだ時点の外した印で行き先を決め、書くのは後（商品の錠の中）。前は錠の中で今の値に当てるとき、
/// 外した印のあるハッシュを見つけた物から除かずに <see cref="LocalFileMerger.Merge"/> へ渡していたので、
/// 「両方が外していた時だけ残す」の決まりで印が下りた（同じ形の結び直しの道は除いていた）。
/// 重なりは時計で作らない（<see cref="ItemLockRace"/>・BOOTH から取っている最中に人が書く）。
/// </summary>
public sealed class DetachDuringImportTests : IDisposable
{
    private const string ItemId = "9900101";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-detach-during-import-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly AppSettings _settings = new() { SaveImages = false, FetchIntervalMs = 0 };

    /// <summary>BOOTH から商品の情報を返す直前に呼ぶ（取り込みの①の最中に人が書くため）。</summary>
    private Action<string>? _duringFetch;

    public DetachDuringImportTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
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

    private ImportPipeline Pipeline()
    {
        var client = new BoothClient(new HttpClient(new FakeBooth(this)), _settings, TestWait.None);
        return new ImportPipeline(_store, client, new ImagePipeline(client, _paths, _settings), _settings);
    }

    private sealed class FakeBooth(DetachDuringImportTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                var id = url.Split('/')[^1].Replace(".json", string.Empty, StringComparison.Ordinal);
                owner._duringFetch?.Invoke(id);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{id}},
                          "name": "作り物の商品 {{id}}",
                          "url": "https://booth.pm/ja/items/{{id}}",
                          "shop": { "name": "shop", "subdomain": "shop", "thumbnail_url": "", "url": "https://shop.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 100 } ]
                        }
                        """),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><body></body></html>") });
        }
    }

    /// <summary>商品の URL を中に書いた zip（手掛かりで商品が1つに決まる）。</summary>
    private string MakeZip()
    {
        var zip = Path.Combine(_source, "asset.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write($"https://booth.pm/ja/items/{ItemId}");
        }

        return zip;
    }

    private static string HashOf(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static LocalBlock Detach(LocalBlock local, string hash) => local with
    {
        LocalFiles = [.. local.LocalFiles.Select(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
            ? file with { Detached = true }
            : file)],
    };

    /// <summary>既にある商品へ足す道：取り込みが読んだ後・書く前に外した印が付いても、印は下りない。</summary>
    [Fact]
    public async Task 既にある商品へ足す間に外したファイルは外したまま残る()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物の商品" },
        });
        await _store.Items.SaveDescriptionHtmlAsync(ItemId, "<p>説明</p>");
        var zip = MakeZip();
        await Pipeline().RunAsync([_source]);
        var hash = HashOf(zip);
        Assert.False(Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles).Detached);

        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            ItemId,
            local => Detach(local, hash),
            () => Pipeline().RunAsync([_source]));

        var file = Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles);
        Assert.True(file.Detached);
    }

    /// <summary>
    /// 作る道：読んだ時は無かった商品を、BOOTH から取っている間に人が作り、そのファイルを外していた。
    /// 作る直前に在ったので今の値に重ねるが、外した印は下ろさない。
    /// </summary>
    [Fact]
    public async Task 作る直前に在った商品で外していたファイルは外したまま残る()
    {
        var zip = MakeZip();
        var hash = HashOf(zip);
        _duringFetch = id =>
        {
            if (id != ItemId)
            {
                return;
            }

            _store.Items.SaveAsync(new ItemRecord
            {
                Id = ItemId,
                Booth = new BoothBlock(),
                Local = new LocalBlock
                {
                    LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [zip], SizeBytes = new FileInfo(zip).Length, Detached = true }],
                },
            }).GetAwaiter().GetResult();
        };

        await Pipeline().RunAsync([_source]);

        var file = Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles);
        Assert.True(file.Detached);
    }
}
