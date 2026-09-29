using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 走らせている最中に対象を足せること。
///
/// 「1ファイルだけ後から見つかった」は普通に起きるので、終わるのを待たせない。
/// パイプラインは1本のまま、対象の集合だけが増える。
/// </summary>
public class ImportStackingTests : IDisposable
{
    private readonly string _root;
    private readonly ImportPipeline _pipeline;

    public ImportStackingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-stack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new OfflineHandler()), settings);

        _pipeline = new ImportPipeline(
            new DataStore(paths),
            client,
            new ImagePipeline(client, paths, settings),
            settings);
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

    /// <summary>呼ばれたら失敗させる。この試験では商品IDが決まらないので、通信は起きないはず。</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験ではBOOTHへ行かないはず");
    }

    /// <summary>
    /// その場で呼ぶ <see cref="IProgress{T}"/>。
    ///
    /// <see cref="Progress{T}"/> はコールバックを別スレッドへ投げるので、
    /// 「走査の途中で積む」が周回の判定に間に合うかどうかが運任せになる。
    /// 試験は時間に依存させない。
    /// </summary>
    private sealed class InlineProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }

    /// <summary>取り込み元のフォルダを作る。中身は商品IDの手がかりを持たない普通のファイル。</summary>
    private string CreateFolder(string name, int fileCount)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        for (var index = 0; index < fileCount; index++)
        {
            File.WriteAllText(Path.Combine(folder, $"{name}_{index}.zip"), $"{name}-{index}");
        }

        return folder;
    }

    /// <summary>**A2の本体。**走っている最中に足したフォルダを、同じ実行が拾う。</summary>
    [Fact]
    public async Task PicksUpAFolderAddedWhileItWasAlreadyRunning()
    {
        var first = CreateFolder("first", 2);
        var second = CreateFolder("second", 3);

        var work = new ImportWorkSet([first]);
        var stacked = false;

        // 走査の途中で、ユーザが2つ目のフォルダを積んだことにする
        var progress = new InlineProgress(_ =>
        {
            if (!stacked)
            {
                stacked = true;
                work.Add([second]);
            }
        });

        var summary = await _pipeline.RunAsync(work, progress);

        // 押し直していないのに、両方が同じ実行で走査されている
        Assert.Equal(5, summary.FilesScanned);
        Assert.Equal(5, summary.UnresolvedFiles);
    }

    /// <summary>足さなければ1周で終わる。空回りしない。</summary>
    [Fact]
    public async Task StopsAfterOneRoundWhenNothingWasStacked()
    {
        var summary = await _pipeline.RunAsync(new ImportWorkSet([CreateFolder("only", 2)]));

        Assert.Equal(2, summary.FilesScanned);
    }

    /// <summary>
    /// 周回をまたいでも未確定は積み上がる。
    /// 保存し直すときに前の周回ぶんを消すと、後から足した方だけが残ってしまう。
    /// </summary>
    [Fact]
    public async Task KeepsUnresolvedFilesFromEveryRound()
    {
        var first = CreateFolder("first", 2);
        var second = CreateFolder("second", 1);

        var work = new ImportWorkSet([first]);
        var stacked = false;

        var progress = new InlineProgress(_ =>
        {
            if (!stacked)
            {
                stacked = true;
                work.Add([second]);
            }
        });

        await _pipeline.RunAsync(work, progress);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        var unresolved = new DataStore(paths).Unresolved.Load();

        Assert.Equal(3, unresolved.Count);
    }

    /// <summary>
    /// **別の取り込みで別のフォルダを取り込んでも、前に取り込んだフォルダの未確定は消さない**（大容量の確かめ E・2026-09-30）。
    /// 対象は「今積んだ物」だけなので、今回走査していない取り込み元の物は、見つからなかったのではなく見ていないだけ。
    /// 落とすと、走査の控えに載っているので監視も新しいと数えず、商品にも未確定にも出なくなっていた
    /// </summary>
    [Fact]
    public async Task KeepsUnresolvedFilesOfAFolderImportedEarlier()
    {
        var first = CreateFolder("first", 4);
        var second = CreateFolder("second", 2);

        await _pipeline.RunAsync(new ImportWorkSet([first]));
        await _pipeline.RunAsync(new ImportWorkSet([second]));

        var unresolved = new DataStore(new AppPaths(Path.Combine(_root, "library"))).Unresolved.Load();
        Assert.Equal(6, unresolved.Count);
    }

    /// <summary>今回走査したフォルダの中で無くなった物は、片付いたとして今のとおり落とす。</summary>
    [Fact]
    public async Task DropsUnresolvedFilesThatAreGoneFromAFolderScannedAgain()
    {
        var first = CreateFolder("first", 3);
        var second = CreateFolder("second", 1);
        await _pipeline.RunAsync(new ImportWorkSet([first, second]));

        File.Delete(Path.Combine(first, "first_0.zip"));
        await _pipeline.RunAsync(new ImportWorkSet([first]));

        var unresolved = new DataStore(new AppPaths(Path.Combine(_root, "library"))).Unresolved.Load();
        Assert.Equal(3, unresolved.Count);
        Assert.DoesNotContain(unresolved, file => file.Paths.Any(path => path.EndsWith("first_0.zip", StringComparison.Ordinal)));
    }

    /// <summary>実行中に同じフォルダを積み直しても、二度は走査しない。</summary>
    [Fact]
    public async Task DoesNotRescanAFolderStackedTwice()
    {
        var folder = CreateFolder("same", 2);

        var work = new ImportWorkSet([folder]);
        var progress = new InlineProgress(_ => work.Add([folder]));

        var summary = await _pipeline.RunAsync(work, progress);

        Assert.Equal(2, summary.FilesScanned);
    }
}
