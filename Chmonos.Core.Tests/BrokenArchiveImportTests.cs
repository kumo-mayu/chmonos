using System.IO.Compression;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 壊れていて開けない zip に、取り込みが印を付けること（大容量の確かめ 2026-09-30 の問題4・ユーザ判断 2026-09-30）。
///
/// 前は開けなかった zip を「中身の一覧が空の未確定」として置くだけで、途中で切れたダウンロードが
/// 普通の未確定と見分けられなかった。壊れた zip は試験の中で作る（本物の zip を途中で切る・でたらめな中身を書く）。
/// </summary>
public class BrokenArchiveImportTests : IDisposable
{
    private const string MissingId = "9999999";

    private readonly string _root;
    private readonly string _source;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public BrokenArchiveImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-broken-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_source);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new NotFoundHandler()), settings, TestWait.None);
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

        GC.SuppressFinalize(this);
    }

    /// <summary>何を聞かれても 404。手掛かりの無い zip は問い合わせまで行かないので、呼ばれるのは配布元の記録を付けた試験だけ。</summary>
    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>開ける zip。中身は圧縮せずに入れ、途中で切ったときに目録（末尾）が確実に無くなる大きさにする。</summary>
    private string GoodZip(string name)
    {
        var path = Path.Combine(_source, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entryName in new[] { "model/body.fbx", "textures/skin.png", "readme.txt" })
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(new byte[4096]);
        }

        return path;
    }

    /// <summary>途中で切れたダウンロード：本物の zip の前半だけ。目録は末尾にあるので、切ると開けなくなる。</summary>
    private string TruncatedZip(string name)
    {
        var path = GoodZip(name);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        return path;
    }

    /// <summary>中身がでたらめの zip。乱数は使わず、決まった並びで埋める（毎回同じ物で試す）。</summary>
    private string GarbageZip(string name)
    {
        var path = Path.Combine(_source, name);
        File.WriteAllBytes(path, [.. Enumerable.Range(0, 5000).Select(index => (byte)((index * 37) + 11))]);
        return path;
    }

    private UnresolvedFile Found(string path)
        => Assert.Single(_store.Unresolved.Load(), file => file.Paths.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task MarksATruncatedZip()
    {
        var path = TruncatedZip("cut.zip");

        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.True(Found(path).ArchiveBroken);
        Assert.Empty(Found(path).Contents);
        Assert.Equal(1, summary.FilesBrokenArchive);
    }

    [Fact]
    public async Task MarksAZipOfGarbage()
    {
        var path = GarbageZip("noise.zip");

        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.True(Found(path).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchive);
    }

    /// <summary>開ける zip と、zip ではない未確定（rar は中を開かないので、壊れているかは分からない）には付けない。</summary>
    [Fact]
    public async Task LeavesReadableZipsAndOtherFilesUnmarked()
    {
        var good = GoodZip("good.zip");
        var package = Path.Combine(_source, "other.rar");
        File.WriteAllBytes(package, [1, 2, 3, 4, 5]);
        var cut = TruncatedZip("cut.zip");

        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.False(Found(good).ArchiveBroken);
        Assert.Equal(3, Found(good).Contents.Count);
        Assert.False(Found(package).ArchiveBroken);
        Assert.True(Found(cut).ArchiveBroken);

        // 読めなかった物（取り込めていない）とは別の数。壊れた zip は未確定に入っている
        Assert.Equal(1, summary.FilesBrokenArchive);
        Assert.Equal(0, summary.FilesUnreadable);
        Assert.Equal(3, summary.UnresolvedFiles);
    }

    /// <summary>
    /// 記録に書くのは壊れていた事実だけ。開けた物に false を並べない（人が開いて読む JSON）。
    /// </summary>
    [Fact]
    public async Task WritesTheMarkOnlyOnBrokenOnes()
    {
        GoodZip("good.zip");
        TruncatedZip("cut.zip");

        await _pipeline.RunAsync(new ImportWorkSet([_source]));

        var json = File.ReadAllText(_paths.UnresolvedFile);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"archiveBroken\""));
        Assert.Contains("\"archiveBroken\": true", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 取り込み直しても印が残る（開けなかった zip は走査の控えに手掛かりを書かないので、毎回開き直して同じ答えになる）。
    /// </summary>
    [Fact]
    public async Task KeepsTheMarkOnReimport()
    {
        var path = TruncatedZip("cut.zip");

        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.True(Found(path).ArchiveBroken);
        Assert.Equal(1, summary.FilesBrokenArchive);
    }

    /// <summary>同じ場所に落とし直したら、前の記録ごと印が消える（中身が変わるので別の物として取り込まれる）。</summary>
    [Fact]
    public async Task DownloadingAgainClearsTheMark()
    {
        var path = TruncatedZip("pack.zip");
        await _pipeline.RunAsync(new ImportWorkSet([_source]));
        Assert.True(Found(path).ArchiveBroken);

        File.Delete(path);
        GoodZip("pack.zip");
        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.False(Found(path).ArchiveBroken);
        Assert.Equal(0, summary.FilesBrokenArchive);
    }

    /// <summary>
    /// 配布元の記録から商品が決まり、BOOTH に無くて未確定へ戻ってきた壊れた zip にも印が付く。
    /// 戻すときは商品のファイルの記録から作り直すので、そのままだと印が落ちる。
    /// </summary>
    [Fact]
    public async Task MarksABrokenZipThatComesBackFromBooth()
    {
        var path = TruncatedZip("cut.zip");
        File.WriteAllText(
            path + ":Zone.Identifier",
            $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/{MissingId}\r\n");

        var summary = await _pipeline.RunAsync(new ImportWorkSet([_source]));

        Assert.Equal(1, summary.NotFound);
        Assert.True(Found(path).ArchiveBroken);
        Assert.Contains(MissingId, Found(path).CandidateItemIds);
        Assert.Equal(1, summary.FilesBrokenArchive);
    }
}
