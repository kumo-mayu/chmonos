using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品を外すときと、外した商品の画像。
/// 前は JSON を先に消していたので、画像フォルダが消せないと「商品は無いのに画像だけ残る」状態になり、
/// 外した後に回ってきた画像の取得がフォルダを作り直してもいた。
/// </summary>
public sealed class ItemDeleteTests : IDisposable
{
    private const string ItemId = "777";

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-delete-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImagePipeline _images;

    public ItemDeleteTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new PngHandler()), settings, (_, _) => Task.CompletedTask);
        _images = new ImagePipeline(client, _paths, settings);
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

    private sealed class PngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) });
    }

    private Task SaveItemAsync()
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow },
            Local = new LocalBlock(),
        });

    private static BoothImage Image(string name) => new() { OriginalUrl = $"https://booth.pximg.net/{name}.png" };

    /// <summary>画像フォルダが消せなければ、商品は残る（もう一度外せば済む）。</summary>
    [Fact]
    public async Task KeepsTheItemWhenItsImagesCannotBeDeleted()
    {
        await SaveItemAsync();
        await _images.SyncAsync(ItemId, [Image("a")]);

        var directory = _paths.ItemImagesDir(ItemId);
        var file = Directory.EnumerateFiles(directory, "*.webp").Single();

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _store.Items.DeleteAsync(ItemId));
        }

        Assert.True(_store.Items.Exists(ItemId));

        await _store.Items.DeleteAsync(ItemId);
        Assert.False(_store.Items.Exists(ItemId));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>外した後に回ってきた画像の取得は、画像フォルダを作り直さない。</summary>
    [Fact]
    public async Task DoesNotRecreateImagesOfARemovedItem()
    {
        await SaveItemAsync();
        await _store.Items.DeleteAsync(ItemId);

        var result = await _images.SyncAsync(ItemId, [Image("a")]);
        var present = await _images.SyncOneAsync(ItemId, Image("b"));

        Assert.Equal(0, result.Downloaded);
        Assert.False(present);
        Assert.False(Directory.Exists(_paths.ItemImagesDir(ItemId)));
    }

    /// <summary>画像の保存に失敗したら一時ファイルを消す（前は固定の名前で、失敗すると残り続けた）。</summary>
    [Fact]
    public async Task RemovesTheTemporaryFileWhenSavingAnImageFails()
    {
        await SaveItemAsync();
        var directory = _paths.ItemImagesDir(ItemId);

        // 置く先の名前がフォルダで塞がっているので、最後の置き換えで必ず失敗する
        Directory.CreateDirectory(Path.Combine(directory, UserImageName.For(TinyPng)));

        await Assert.ThrowsAnyAsync<Exception>(() => _images.SaveUserImageAsync(ItemId, TinyPng));

        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    /// <summary>
    /// 条件付きの削除は、錠の中で読んだ今の値で決める。
    /// 「空になったら消す」を錠の外で決めていた頃は、その後に取り込みが足したファイルごと消えていた。
    /// </summary>
    [Fact]
    public async Task DeletesOnlyWhenTheCurrentValueStillMatches()
    {
        await SaveItemAsync();
        await _store.Items.ChangeLocalAsync(
            ItemId,
            local => local with { LocalFiles = [new LocalFileRecord { Hash = "AAAA1", Paths = [@"D:\a.zip"], SizeBytes = 1 }] },
            [LocalField.LocalFiles]);

        Assert.False(await _store.Items.DeleteIfAsync(ItemId, item => item.Local.LocalFiles.Count == 0));
        Assert.True(_store.Items.Exists(ItemId));

        Assert.True(await _store.Items.DeleteIfAsync(ItemId, item => item.Local.LocalFiles.Count == 1));
        Assert.False(_store.Items.Exists(ItemId));
        Assert.False(await _store.Items.DeleteIfAsync(ItemId, _ => true));
    }

    /// <summary>
    /// 移してから消す間は元の商品の錠を持つ。その間に来た書き込みは消した後に回り、**消えた商品を作り直さずに断られる**
    /// （前は読み直してから消すまでの間に書けて、書いた分が元と一緒に黙って消えていた）。
    /// </summary>
    [Fact]
    public async Task WritesDuringAMoveWaitUntilTheSourceIsGone()
    {
        await SaveItemAsync();
        Task<bool>? late = null;

        var moved = await _store.Items.MoveAwayAsync(ItemId, async current =>
        {
            late = _store.Items.SaveLocalAsync(ItemId, current.Local with { Memo = "移している間のメモ" }, [LocalField.Memo]);
            await Task.Delay(50);
            Assert.False(late.IsCompleted);
            return true;
        });

        Assert.True(moved);
        Assert.False(await late!);
        Assert.False(_store.Items.Exists(ItemId));
    }

    /// <summary>移す先が断ったら、元は消さない。元が無ければ null。</summary>
    [Fact]
    public async Task KeepsTheSourceWhenTheMoveIsRefused()
    {
        await SaveItemAsync();

        Assert.False(await _store.Items.MoveAwayAsync(ItemId, _ => Task.FromResult(false)));
        Assert.True(_store.Items.Exists(ItemId));
        Assert.Null(await _store.Items.MoveAwayAsync("無い商品", _ => Task.FromResult(true)));
    }

    /// <summary>無ければ作り、あれば今の値へ重ねる（在るかを見てから書くまでを錠の中で）。</summary>
    [Fact]
    public async Task CreatesOrChangesUnderTheLock()
    {
        ItemRecord Empty() => new() { Id = ItemId, Booth = new BoothBlock(), Local = new LocalBlock() };

        Assert.True(await _store.Items.CreateOrChangeLocalAsync(
            ItemId, Empty, local => local with { Memo = "作った" }, [LocalField.Memo]));
        Assert.Equal("作った", (await _store.Items.LoadAsync(ItemId))!.Local.Memo);

        await _store.Items.ChangeLocalAsync(ItemId, local => local with { IsFavorite = true }, [LocalField.IsFavorite]);
        Assert.True(await _store.Items.CreateOrChangeLocalAsync(
            ItemId, Empty, local => local with { Memo = "重ねた" }, [LocalField.Memo]));

        var saved = (await _store.Items.LoadAsync(ItemId))!.Local;
        Assert.Equal("重ねた", saved.Memo);
        Assert.True(saved.IsFavorite);
    }

    /// <summary>
    /// 記録を消せずに外せなかったら、画像は消えずに残る（外部の点検 2026-10-07）。
    /// 前は画像を先に消していたので、自分で足した画像（BOOTH から取り直せない）だけが消えた
    /// </summary>
    [Fact]
    public async Task 記録を消せずに外せなければ_画像は残る()
    {
        await SaveItemAsync();
        var imagesDir = _paths.ItemImagesDir(ItemId);
        Directory.CreateDirectory(imagesDir);
        File.WriteAllBytes(Path.Combine(imagesDir, "mine.png"), TinyPng);

        using (new FileStream(_paths.ItemFile(ItemId), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _store.Items.DeleteAsync(ItemId));
        }

        Assert.True(File.Exists(Path.Combine(imagesDir, "mine.png")));
        Assert.NotNull(await _store.Items.LoadAsync(ItemId));
        Assert.Empty(Directory.EnumerateDirectories(_paths.ImagesDir, "*.removing-*"));
    }

    /// <summary>
    /// 逆向きの付け替え（甲→乙と乙→甲）が重なっても、待ち合わずに終わる（外部の点検 2026-10-07）。
    /// 元の錠を持ったまま先の錠を取るので、前は互いの錠を待ち続けた
    /// </summary>
    [Fact]
    public async Task 逆向きの付け替えが重なっても_待ち合わずに終わる()
    {
        foreach (var id in new[] { "801", "802" })
        {
            await _store.Items.SaveAsync(new ItemRecord { Id = id, Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow }, Local = new LocalBlock() });
        }

        var bothInside = new TaskCompletionSource();
        var inside = 0;
        Func<ItemRecord, Task<bool>> MoveInto(string other) => async _ =>
        {
            // 両方が元の錠を持った所まで進めてから、先の錠を取りに行く（前の作りでは、ここで輪になる）
            if (Interlocked.Increment(ref inside) == 2)
            {
                bothInside.TrySetResult();
            }

            await Task.WhenAny(bothInside.Task, Task.Delay(300));
            await _store.Items.ChangeLocalAsync(other, local => local, [LocalField.Memo]);
            return false;
        };

        var first = _store.Items.MoveAwayAsync("801", MoveInto("802"));
        var second = _store.Items.MoveAwayAsync("802", MoveInto("801"));

        var both = Task.WhenAll(first, second);
        await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(both.IsCompleted, "付け替えどうしが待ち合って終わらなかった");

        // 終わったことに加えて、例外で終わっていないことも確かめる（点検24：IsCompleted は例外で終わっても真なので、
        // 両方が投げて終わっても通っていた）
        await both;
        Assert.False(await first);
        Assert.False(await second);
    }

    /// <summary>
    /// 商品を消す途中で止まって残った画像の退避は、起動の後の片付けで、記録が残っていれば戻し、無ければ消す（外部の点検 2026-10-07）。
    /// </summary>
    [Fact]
    public async Task 消す途中で残った画像の退避は_記録が残れば戻し_無ければ消す()
    {
        await SaveItemAsync();
        var kept = _paths.ItemImagesDir(ItemId) + ".removing-1a2b3c4d";
        Directory.CreateDirectory(kept);
        File.WriteAllBytes(Path.Combine(kept, "mine.png"), TinyPng);
        var orphan = _paths.ItemImagesDir("778") + ".removing-5e6f7a8b";
        Directory.CreateDirectory(orphan);

        // アプリが作る形でない名前は、退避ではないので触らない
        var notOurs = _paths.ItemImagesDir("779") + ".removing-note";
        Directory.CreateDirectory(notOurs);

        Assert.Equal(2, await _store.Items.RecoverRemovingImagesAsync());

        Assert.True(File.Exists(Path.Combine(_paths.ItemImagesDir(ItemId), "mine.png")));
        Assert.False(Directory.Exists(kept));
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(notOurs));
    }
}
