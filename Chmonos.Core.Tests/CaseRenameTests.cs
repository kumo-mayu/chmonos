using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 名前の大文字小文字だけを変えた物を、取り込みが記録の綴りに写す（2026-10-05・見つからない・移動の点検の14）。
/// 前は場所を大文字小文字を区別せずに比べていたので、同じ場所と見て何もせず、記録は古い名前のまま残った。
/// </summary>
public sealed class CaseRenameTests : IDisposable
{
    private const string ItemId = "local-9900003";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-case-rename-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly DataStore _store;

    public CaseRenameTests()
    {
        _watched = Directory.CreateDirectory(Path.Combine(_root, "watched")).FullName;
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
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

    [Fact]
    public async Task 大文字小文字だけを変えた名前を_取り込みが記録に写す()
    {
        var renamed = Path.Combine(_watched, "Manual.pdf");
        await File.WriteAllTextAsync(renamed, "作り物の説明書");
        var recorded = Path.Combine(_watched, "manual.pdf");
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = await FileHasher.ComputeSha256Async(renamed),
                        Paths = [recorded],
                        SizeBytes = new FileInfo(renamed).Length,
                    },
                ],
            },
        });

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        await new ImportPipeline(_store, client, new ImagePipeline(client, _store.Paths, settings), settings)
            .RunAsync(new ImportWorkSet([_watched]));

        var file = Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles);
        Assert.Equal([renamed], file.Paths);
        Assert.Equal(renamed, Assert.Single(_store.ScanCache.Load()).Path);
        Assert.Equal(0, client.Calls);
    }
}
