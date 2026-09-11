using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ファイルに種類（BOOTHのバリエーション）を付ける（#40）。
/// 以前は付ける操作がどこにも無く、友人のデータで327件中0件だった。
/// </summary>
public class FileVariationTests : IDisposable
{
    private const string ItemId = "111";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public FileVariationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-filevar-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 種類を付けるだけなのでBOOTHへは行かない。行ったら失敗させる
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 });
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("このテストではBOOTHへ行かないはず");
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

    private async Task SaveItemAsync(params LocalFileRecord[] files)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files, Memo = "自分で書いたメモ" },
        });

    private static LocalFileRecord File(string hash, long? variationId = null) => new()
    {
        Hash = hash,
        Paths = [$@"D:\dl\{hash}.zip"],
        SizeBytes = 5,
        VariationId = variationId,
    };

    private async Task<LocalFileRecord> LoadFileAsync(string hash)
        => (await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles.Single(file => file.Hash == hash);

    [Fact]
    public async Task 名指ししたファイルにだけ種類を付ける()
    {
        await SaveItemAsync(File("AAAA"), File("BBBB"));

        Assert.True(await _service.SetFileVariationsAsync(ItemId, new Dictionary<string, long?> { ["AAAA"] = 10 }));

        Assert.Equal(10, (await LoadFileAsync("AAAA")).VariationId);
        Assert.Null((await LoadFileAsync("BBBB")).VariationId);
    }

    [Fact]
    public async Task 同じ種類を複数のファイルに付けられる()
    {
        // 友人の報告「同じ商品の別zipでも、同じ種類由来のこともある」
        await SaveItemAsync(File("AAAA"), File("BBBB"));

        await _service.SetFileVariationsAsync(ItemId, new Dictionary<string, long?> { ["AAAA"] = 10, ["BBBB"] = 10 });

        Assert.Equal(10, (await LoadFileAsync("AAAA")).VariationId);
        Assert.Equal(10, (await LoadFileAsync("BBBB")).VariationId);
    }

    [Fact]
    public async Task nullで外せる()
    {
        await SaveItemAsync(File("AAAA", variationId: 10));

        await _service.SetFileVariationsAsync(ItemId, new Dictionary<string, long?> { ["AAAA"] = null });

        Assert.Null((await LoadFileAsync("AAAA")).VariationId);
    }

    [Fact]
    public async Task 種類のほかは読み直した値のまま残す()
    {
        await SaveItemAsync(File("AAAA"));

        // 画面を開いた後に取り込みがパスを足した、という状況
        var item = (await _store.Items.LoadAsync(ItemId))!;
        await _store.Items.SaveAsync(item with
        {
            Local = item.Local with
            {
                LocalFiles = [item.Local.LocalFiles[0] with { Paths = [@"D:\dl\AAAA.zip", @"E:\copy\AAAA.zip"] }, File("CCCC")],
            },
        });

        await _service.SetFileVariationsAsync(ItemId, new Dictionary<string, long?> { ["aaaa"] = 20 });

        var saved = (await _store.Items.LoadAsync(ItemId))!;
        Assert.Equal(2, saved.Local.LocalFiles.Count);
        Assert.Equal(2, saved.Local.LocalFiles.Single(file => file.Hash == "AAAA").Paths.Count);
        Assert.Equal(20, saved.Local.LocalFiles.Single(file => file.Hash == "AAAA").VariationId);
        Assert.Equal("自分で書いたメモ", saved.Local.Memo);
    }

    [Fact]
    public async Task 無い商品ではfalse()
        => Assert.False(await _service.SetFileVariationsAsync("999", new Dictionary<string, long?> { ["AAAA"] = 1 }));
}
