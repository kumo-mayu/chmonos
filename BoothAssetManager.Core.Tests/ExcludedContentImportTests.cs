using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 管理から外したのはその中身で、場所ではない（ユーザ判断 2026-09-23）。
/// 前は外したパスと一致すればハッシュを取らずに飛ばしていたので、同じ名前で落とし直した更新版まで外し続けていた。
/// 手掛かりの無いファイルで試す（未確定へ回るだけで BOOTH へは行かない）。
/// </summary>
public sealed class ExcludedContentImportTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _file;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public ExcludedContentImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-excluded-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);
        _file = Path.Combine(_source, "backup.zip");

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 本物の通信の口で組まない（既定の一式に通信する試験を入れない）。行けば落ちる偽物にする
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), settings, TestWait.None);
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, paths, settings), settings);
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
    }

    private async Task<string> ExcludeCurrentContentAsync()
    {
        var hash = await FileHasher.ComputeSha256Async(_file);
        var excluded = _store.Excluded.Load();
        excluded.Add(new ExcludedEntry { Hash = hash, Paths = [_file], ExcludedAt = DateTimeOffset.Now });
        await _store.Excluded.SaveAsync(excluded);

        // 未確定の画面で外したのと同じく、未確定からは消す
        await _store.Unresolved.UpdateAsync(current =>
        {
            current.RemoveAll(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase));
            return current;
        });
        return hash;
    }

    private Task<ImportSummary> ImportAsync() => _pipeline.RunAsync(new ImportWorkSet([_source]));

    /// <summary>控えが今のファイルと合い、控えのハッシュが外した物なら、読まずに外したまま。</summary>
    [Fact]
    public async Task KeepsTheSameContentAtTheSamePathExcludedWithoutHashing()
    {
        File.WriteAllText(_file, "バックアップ");
        await ImportAsync();
        await ExcludeCurrentContentAsync();

        var summary = await ImportAsync();

        Assert.Equal(0, summary.FilesHashed);
        Assert.Equal(0, summary.FilesScanned);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>同じ名前で落とし直した更新版は、新しい物として取り込まれる。</summary>
    [Fact]
    public async Task TakesInOtherContentThatCameToTheExcludedPath()
    {
        File.WriteAllText(_file, "バックアップ");
        await ImportAsync();
        var excludedHash = await ExcludeCurrentContentAsync();

        File.WriteAllText(_file, "同じ名前で落とし直した更新版");
        var summary = await ImportAsync();

        Assert.Equal(1, summary.FilesHashed);
        Assert.Equal(0, summary.FilesExcluded);
        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.NotEqual(excludedHash, unresolved.Hash, StringComparer.OrdinalIgnoreCase);
        Assert.Equal([_file], unresolved.Paths);
    }

    /// <summary>控えが無ければハッシュを取って決める。同じ中身なら外したまま。</summary>
    [Fact]
    public async Task HashesAndKeepsTheSameContentExcludedWhenThereIsNoCache()
    {
        File.WriteAllText(_file, "バックアップ");
        await ExcludeCurrentContentAsync();

        var summary = await ImportAsync();

        Assert.Equal(1, summary.FilesHashed);
        Assert.Equal(1, summary.FilesExcluded);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>控えが無く、中身が外した物と違えば取り込む。</summary>
    [Fact]
    public async Task HashesAndTakesInOtherContentWhenThereIsNoCache()
    {
        File.WriteAllText(_file, "バックアップ");
        await ExcludeCurrentContentAsync();
        File.WriteAllText(_file, "別の中身");

        var summary = await ImportAsync();

        Assert.Equal(0, summary.FilesExcluded);
        Assert.Single(_store.Unresolved.Load());
    }

    /// <summary>ハッシュで外す段は今のまま：外した中身を別の場所へ移しても外れる。</summary>
    [Fact]
    public async Task StillExcludesTheSameContentMovedElsewhere()
    {
        File.WriteAllText(_file, "バックアップ");
        await ImportAsync();
        await ExcludeCurrentContentAsync();

        File.Move(_file, Path.Combine(_source, "renamed.zip"));
        var summary = await ImportAsync();

        Assert.Equal(1, summary.FilesExcluded);
        Assert.Empty(_store.Unresolved.Load());
    }
}
