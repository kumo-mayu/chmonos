using System.IO.Compression;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込み直し。前は持っているファイル全部の zip を開き直し、変わっていない商品の JSON も1件ずつ書き直していた
/// （300本で毎回300件をディスクへ書き出していた）。**開かない・書かないが、結果は前と同じ**ことを確かめる。
/// </summary>
public sealed class ReimportTests : IDisposable
{
    private const string ItemId = "4242";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-reimport-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly OffUiThreadTests.OfflineClient _client = new();
    private readonly ImportPipeline _pipeline;

    public ReimportTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        var settings = new AppSettings { SaveImages = false };
        _pipeline = new ImportPipeline(_store, _client, new ImagePipeline(_client, _paths, settings), settings);
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

    /// <summary>取得済みの商品（説明もある）と、その商品のURLを中に書いた zip。</summary>
    private async Task<string> SetUpAsync()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "商品" },
        });
        await _store.Items.SaveDescriptionHtmlAsync(ItemId, "<p>説明</p>");

        var zip = Path.Combine(_source, "asset.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open()))
            {
                writer.Write($"https://booth.pm/ja/items/{ItemId}");
            }

            archive.CreateEntry("Assets/Model/model.fbx").Open().Dispose();
        }

        return zip;
    }

    [Fact]
    public async Task ReimportingLeavesUnchangedItemsUnwritten()
    {
        await SetUpAsync();
        var first = await _pipeline.RunAsync([_source]);
        Assert.Equal(1, first.ItemsAlreadyKnown);

        var file = _paths.ItemFile(ItemId);
        var written = File.GetLastWriteTimeUtc(file);
        var before = File.ReadAllText(file);

        var second = await _pipeline.RunAsync([_source]);

        Assert.Equal(1, second.ItemsAlreadyKnown);
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Equal(0, _client.Calls);
    }

    /// <summary>
    /// 持っている zip は開かず、控えた手掛かりと商品の中身の一覧を使う。
    /// 大きさと日時を保ったまま中身を壊しても（開けば手掛かりが取れず「持っている」止まりになる）、前と同じ商品へ結び付く。
    /// </summary>
    [Fact]
    public async Task OwnedArchivesAreNotOpenedAgain()
    {
        var zip = await SetUpAsync();
        await _pipeline.RunAsync([_source]);
        var contents = (await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles.Single().Contents;
        Assert.Contains("readme.txt", contents);

        var stamp = File.GetLastWriteTimeUtc(zip);
        var size = new FileInfo(zip).Length;
        File.WriteAllBytes(zip, new byte[size]);
        File.SetLastWriteTimeUtc(zip, stamp);

        var second = await _pipeline.RunAsync([_source]);

        Assert.Equal(1, second.ItemsAlreadyKnown);
        Assert.Equal(0, second.FilesAlreadyOwned);
        Assert.Equal(contents, (await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles.Single().Contents);
    }

    /// <summary>中身が変わった zip（大きさか日時が違う）は開き直す。</summary>
    [Fact]
    public async Task ChangedArchivesAreOpenedAgain()
    {
        var zip = await SetUpAsync();
        await _pipeline.RunAsync([_source]);

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry("Assets/Model/extra.png").Open().Dispose();
        }

        File.SetLastWriteTimeUtc(zip, File.GetLastWriteTimeUtc(zip).AddSeconds(5));
        await _pipeline.RunAsync([_source]);

        var files = (await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles;
        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.Contents.Contains("Assets/Model/extra.png"));
    }

    /// <summary>名指しした項目が今と同じなら書かないが、呼んだ側への答え（在ったか）は変えない。</summary>
    [Fact]
    public async Task SavingTheSameValuesDoesNotRewriteTheFile()
    {
        await _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Local = new LocalBlock { Memo = "メモ" } });
        var file = _paths.ItemFile(ItemId);
        var written = File.GetLastWriteTimeUtc(file);

        Assert.True(await _store.Items.SaveLocalAsync(ItemId, new LocalBlock { Memo = "メモ" }, [LocalField.Memo]));
        Assert.True(await _store.Items.ChangeLocalAsync(ItemId, local => local with { Memo = "メモ" }, [LocalField.Memo]));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));

        Assert.True(await _store.Items.ChangeLocalAsync(ItemId, local => local with { Memo = "直した" }, [LocalField.Memo]));
        Assert.Equal("直した", (await _store.Items.LoadAsync(ItemId))!.Local.Memo);
        Assert.Contains("直した", File.ReadAllText(file));
    }
}
