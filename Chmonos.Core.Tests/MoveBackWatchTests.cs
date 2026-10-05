using System.Security.Cryptography;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 監視フォルダの中で移したファイルを元の場所へ戻すと、監視が新着と数える（2026-10-05・見つからない・移動の点検 5）。
///
/// 前は、移した先を取り込むと記録は移した先へ差し替わるのに、元の場所の走査の控えは残っていた
/// （監視の取り込みはフォルダではなく新着のファイルだけを対象にするので、取り込み元の下の片付けが元の場所に届かない）。
/// 戻すと控えと大きさ・日時が合うので新着と数えず、記録は移した先のままで「見つかりません」が残っていた。
/// </summary>
public sealed class MoveBackWatchTests : IDisposable
{
    private const string ItemId = "9900201";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-move-back-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public MoveBackWatchTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _watched = Path.Combine(_root, "download");
        Directory.CreateDirectory(Path.Combine(_watched, "sub"));

        var settings = new AppSettings { SaveImages = false };
        var client = new OffUiThreadTests.OfflineClient();
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
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

    /// <summary>手で結んだファイル（手掛かりが無い）を監視フォルダに置き、商品に記録して控えにも載せる。</summary>
    private async Task<string> OwnedFileAsync()
    {
        var path = Path.Combine(_watched, "asset.png");
        File.WriteAllText(path, "作り物の画像");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物の商品" },
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = new FileInfo(path).Length }],
            },
        });
        await _store.Items.SaveDescriptionHtmlAsync(ItemId, "<p>説明</p>");
        await _pipeline.RunAsync(new ImportWorkSet([_watched]));
        return path;
    }

    /// <summary>監視の新着を、起動時と同じく新着のファイルだけを対象に取り込む。</summary>
    private async Task<IReadOnlyList<string>> ImportWatchedNewAsync()
    {
        var found = await new FolderWatch(_store).FindNewAsync([_watched]);
        if (found.HasNew)
        {
            await _pipeline.RunAsync(new ImportWorkSet(found.NewFiles));
        }

        return found.NewFiles;
    }

    private async Task<IReadOnlyList<string>> RecordedPathsAsync()
        => Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles).Paths;

    [Fact]
    public async Task 移して戻したファイルを監視が新着と数え_記録が元の場所に戻る()
    {
        var original = await OwnedFileAsync();
        var moved = Path.Combine(_watched, "sub", "asset.png");

        File.Move(original, moved);
        Assert.Equal([moved], await ImportWatchedNewAsync());
        Assert.Equal([moved], await RecordedPathsAsync());

        File.Move(moved, original);
        Assert.Equal([original], await ImportWatchedNewAsync());
        Assert.Equal([original], await RecordedPathsAsync());
    }

    /// <summary>
    /// 「見つからないファイルを探す」で結び直した後に戻しても同じ。差し替えた元の場所の控えを残すと、戻した時に新着と数えない。
    /// </summary>
    [Fact]
    public async Task 探して結び直した後に戻したファイルも監視が新着と数える()
    {
        var original = await OwnedFileAsync();
        var moved = Path.Combine(_watched, "sub", "asset.png");

        File.Move(original, moved);
        var search = await new MissingFileFinder(_store).FindAsync([_watched]);
        Assert.Equal(1, search.Relinked);
        Assert.Equal([moved], await RecordedPathsAsync());

        File.Move(moved, original);
        Assert.Equal([original], await ImportWatchedNewAsync());
        Assert.Equal([original], await RecordedPathsAsync());
    }
}
