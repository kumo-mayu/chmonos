using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>未確定の画面で管理対象から外した直後に戻す（ユーザ判断 2026-09-17）。</summary>
public sealed class UndoExcludeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-undo-exclude-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private readonly ItemService _service;

    public UndoExcludeTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        // 本物の通信の口で組まない（既定の一式に通信する試験を入れない）。行けば落ちる偽物にする
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
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task 外したファイルを除外から消し未確定に元の記録のまま戻す()
    {
        var file = new UnresolvedFile
        {
            Hash = "ABCD",
            Paths = [@"D:\a\b.zip"],
            SizeBytes = 10,
            ModifiedAtUtc = DateTimeOffset.Now,
            FirstSeenAt = DateTimeOffset.Now,
            CandidateItemIds = ["123"],
        };
        await _store.Unresolved.UpdateAsync(current => { current.Add(file); return current; });

        await _service.ExcludeAsync(file.Hash, file.Paths, "試験");
        Assert.Empty(_store.Unresolved.Load());

        await _service.UndoExcludeAsync([file]);

        Assert.Empty(_store.Excluded.Load());
        var restored = Assert.Single(_store.Unresolved.Load());
        Assert.Equal(["123"], restored.CandidateItemIds);
    }
}
