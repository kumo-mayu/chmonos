using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 登録したフォルダ（2026-09-24）。周回のたびに中を全部並べ直して測っていたのを取り込み1回につき1度にし、
/// 「この中か」を登録の数だけ前方一致で比べていたのを、パスの区切りごとに集合で引く形にした。
/// </summary>
public sealed class RegisteredFolderOnceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-registered-" + Guid.NewGuid().ToString("N"));

    public RegisteredFolderOnceTests() => Directory.CreateDirectory(_root);

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

    /// <summary>前の作り（登録ごとに前方一致と区切りを見る）と同じ答えになる。</summary>
    [Theory]
    [InlineData(@"D:\assets\rurune_v1", true)]
    [InlineData(@"D:\assets\rurune_v1\a.png", true)]
    [InlineData(@"D:\ASSETS\Rurune_V1\sub\b.png", true)]
    [InlineData(@"D:\assets\rurune_v1.1.3\a.png", false)]
    [InlineData(@"D:\assets\rurune_v", false)]
    [InlineData(@"D:\assets", false)]
    [InlineData(@"D:/assets/rurune_v1/c.png", true)]
    [InlineData(@"D:\assets\rurune_v1\", true)]
    [InlineData(@"E:\other\x.png", false)]
    [InlineData(@"E:\x.png", false)]
    [InlineData(@"E:\", true)]
    public void AnswersTheSameAsBefore(string path, bool expected)
    {
        string[] folders = [@"D:\assets\rurune_v1\", @"D:\assets\衣装", @"E:\"];

        Assert.Equal(expected, new RegisteredFolderSet(folders).Contains(path));
        Assert.Equal(BeforeContains(folders, path), new RegisteredFolderSet(folders).Contains(path));
    }

    private static bool BeforeContains(IEnumerable<string> folders, string path)
    {
        static string Normalize(string text)
            => Path.TrimEndingDirectorySeparator(text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

        var normalized = Normalize(path);
        return folders.Select(Normalize).Any(folder =>
            normalized.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || (normalized.Length > folder.Length
                && normalized.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                && normalized[folder.Length] == Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 取り込みの途中で積んで周回が増えても、登録したフォルダを測るのは最初の周回だけ。
    /// 登録したフォルダの中は、どの周回でも走査から外す（測り直さなくても登録は毎回読む）。
    /// </summary>
    [Fact]
    public async Task MeasuresRegisteredFoldersOncePerImport()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        var store = new DataStore(paths);

        var registered = Path.Combine(_root, "登録した");
        Directory.CreateDirectory(registered);
        File.WriteAllBytes(Path.Combine(registered, "1.psd"), [1]);
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-1",
            Local = new LocalBlock { LocalFolders = [new LocalFolderRecord { Path = registered }] },
        });

        var first = Path.Combine(_root, "一つ目");
        var second = Path.Combine(_root, "二つ目");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllBytes(Path.Combine(first, "a.psd"), [1]);
        File.WriteAllBytes(Path.Combine(second, "b.psd"), [1]);

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(store, client, new ImagePipeline(client, paths, settings), settings);

        // 1周目を走査している最中に、登録したフォルダへファイルを足し、2周目（登録したフォルダも含む）を積む
        var work = new ImportWorkSet([first]);
        var stacked = 0;
        var progress = new Stacker(() =>
        {
            if (Interlocked.Exchange(ref stacked, 1) == 0)
            {
                File.WriteAllBytes(Path.Combine(registered, "2.psd"), [1]);
                work.Add([second, registered]);
            }
        });

        var summary = await pipeline.RunAsync(work, progress);

        var folder = (await store.Items.LoadAsync("local-1"))!.Local.LocalFolders.Single();
        Assert.Equal(1, folder.FileCount);
        Assert.NotNull(folder.LastSeenAt);
        Assert.Equal(2, summary.UnresolvedFiles);
        Assert.Equal(0, client.Calls);
    }

    /// <summary>
    /// 取り込みの数え直しは、中の unitypackage の一覧も書き直す（メモ65-③）。数と大きさが同じでも、一覧が違えば書く
    /// （前の記録に一覧が無い・中で入れ替えた）。
    /// </summary>
    [Fact]
    public async Task RemeasureRecordsUnityPackagesEvenWhenCountAndSizeAreUnchanged()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        var store = new DataStore(paths);

        var registered = Path.Combine(_root, "登録した");
        Directory.CreateDirectory(Path.Combine(registered, "Unity"));
        File.WriteAllBytes(Path.Combine(registered, "Unity", "作り物.unitypackage"), [1]);
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-1",
            Local = new LocalBlock
            {
                LocalFolders = [new LocalFolderRecord { Path = registered, FileCount = 1, TotalBytes = 1, LastSeenAt = DateTimeOffset.Now }],
            },
        });

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(store, client, new ImagePipeline(client, paths, settings), settings);

        await pipeline.RunAsync(new ImportWorkSet([registered]), null);

        var folder = (await store.Items.LoadAsync("local-1"))!.Local.LocalFolders.Single();
        Assert.Equal(["Unity/作り物.unitypackage"], folder.UnityPackages);
    }

    private sealed class Stacker(Action onScanning) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value)
        {
            if (value.Phase == ImportPhase.Scanning)
            {
                onScanning();
            }
        }
    }
}
