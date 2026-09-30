using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 未確定の記録の展開元（Zone.Identifier の ReferrerUrl）は、取り込みの走査が読んで記録に持つ。
/// 画面と札は保存した値だけで束ねる（ユーザ判断 2026-09-30）ので、**取り込み直せば、保存した値が
/// 今の読み方の値で書き直される**ことが要る——古い版が化けたまま保存した値の直し方はこれだけ。
/// </summary>
public class UnresolvedReferrerReimportTests : IDisposable
{
    private const string OriginOnDisk = @"C:\dl\Outfit_v1.zip";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ImportPipeline _pipeline;

    public UnresolvedReferrerReimportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-zr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new NoBooth()), settings, TestWait.None);
        _pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, paths, settings), settings);
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

    /// <summary>手掛かりの無いファイルしか置かないので、BOOTH へは1本も出ない。出たら落とす。</summary>
    private sealed class NoBooth : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("BOOTH へ問い合わせない: " + request.RequestUri);
    }

    /// <summary>zip を展開した中身の形：商品の手掛かりは無く、印に元の zip の場所だけがある。</summary>
    private string CreateExtractedFile()
    {
        var folder = Path.Combine(_root, "source");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "texture.png");
        File.WriteAllText(path, "pixels");

        // 名前は ASCII にする。日本語の印は機械の ANSI コードページで読まれるので、試験の結果が機械に左右される
        File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={OriginOnDisk}\r\n");

        return folder;
    }

    private Task OverwriteStoredReferrerAsync(string? value)
        => _store.Unresolved.UpdateAsync(current => [.. current.Select(file => new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = file.Paths,
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = file.ModifiedAtUtc,
            FirstSeenAt = file.FirstSeenAt,
            Contents = file.Contents,
            ZoneHostUrl = file.ZoneHostUrl,
            ZoneReferrerUrl = value,
            CandidateItemIds = file.CandidateItemIds,
        })]);

    [Fact]
    public async Task TheScanStoresWhatTheFileSays()
    {
        await _pipeline.RunAsync(new ImportWorkSet([CreateExtractedFile()]));

        var stored = Assert.Single(_store.Unresolved.Load());
        Assert.Equal(OriginOnDisk, stored.ZoneReferrerUrl);
        Assert.Equal("Outfit_v1.zip", UnresolvedOrigin.For(stored)?.ArchiveName);
    }

    /// <summary>
    /// 古い版が化けたまま保存した値（U+FFFD 入り）は、読むときには救わない。取り込み直すと書き直される。
    /// **ハッシュは控えから使う回でも、印は読み直す**（控えが当たると読まない作りだと、取り込み直しても直らない）。
    /// </summary>
    [Fact]
    public async Task ReimportRewritesAGarbledStoredValueEvenWhenTheHashComesFromTheCache()
    {
        var source = CreateExtractedFile();
        await _pipeline.RunAsync(new ImportWorkSet([source]));
        await OverwriteStoredReferrerAsync("C:\\dl\\\uFFFDA\uFFFDo.zip");

        // 直す前の記録は束ねられない（名前が化けている）。ファイルの印が正しくても、読むときには見ない
        Assert.Null(UnresolvedOrigin.For(Assert.Single(_store.Unresolved.Load())));

        var summary = await _pipeline.RunAsync(new ImportWorkSet([source]));

        Assert.Equal(0, summary.FilesHashed);
        Assert.Equal(1, summary.FilesReusedFromCache);
        var stored = Assert.Single(_store.Unresolved.Load());
        Assert.Equal(OriginOnDisk, stored.ZoneReferrerUrl);
        Assert.Equal("Outfit_v1.zip", UnresolvedOrigin.For(stored)?.ArchiveName);
    }

    /// <summary>値を持たない記録（商品から外して戻した物は印を読まずに作られる）も、取り込み直すと入る。</summary>
    [Fact]
    public async Task ReimportFillsInAMissingStoredValue()
    {
        var source = CreateExtractedFile();
        await _pipeline.RunAsync(new ImportWorkSet([source]));
        await OverwriteStoredReferrerAsync(null);

        await _pipeline.RunAsync(new ImportWorkSet([source]));

        Assert.Equal(OriginOnDisk, Assert.Single(_store.Unresolved.Load()).ZoneReferrerUrl);
    }
}
