using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

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
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
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

        var added = await _service.ExcludeAsync([file], "試験");
        Assert.Empty(_store.Unresolved.Load());

        await _service.UndoExcludeAsync([file], added);

        Assert.Empty(_store.Excluded.Load());
        var restored = Assert.Single(_store.Unresolved.Load());
        Assert.Equal(["123"], restored.CandidateItemIds);
    }

    /// <summary>
    /// 戻すのは今回足した除外だけ。前から除外していた物の記録（日時・理由）は残す（2026-10-05・file-lifecycle.md「気になった所」18）。
    /// 除外は既にあるハッシュを足さないのに、戻すはハッシュで全部消していたので、前に外した記録まで消えていた。
    /// </summary>
    [Fact]
    public async Task 戻しても前から除外していた物の記録は残る()
    {
        UnresolvedFile Make(string hash) => new()
        {
            Hash = hash,
            Paths = [$@"D:\a\{hash}.zip"],
            SizeBytes = 10,
            ModifiedAtUtc = DateTimeOffset.Now,
            FirstSeenAt = DateTimeOffset.Now,
        };
        var before = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var old = Make("OLD1");
        var fresh = Make("NEW1");
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = "OLD1", Paths = old.Paths, ExcludedAt = before, Reason = "前に外した" }]);

        // 前から除外していた物が、古い一覧の写しから未確定にも残っていた（取り込みと画面の行き違い）
        await _store.Unresolved.SaveAsync([old, fresh]);

        var excluded = await _service.ExcludeAsync([old, fresh], "試験");
        await _service.UndoExcludeAsync([old, fresh], excluded);

        var kept = Assert.Single(_store.Excluded.Load());
        Assert.Equal("OLD1", kept.Hash);
        Assert.Equal(before, kept.ExcludedAt);
        Assert.Equal("前に外した", kept.Reason);

        // 未確定は外す前のまま（どちらも戻る）
        Assert.Equal(["NEW1", "OLD1"], _store.Unresolved.Load().Select(file => file.Hash).Order().ToArray());
    }
}
