using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class StoreMoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "booth-move-" + Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    public StoreMoverTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "items"));
        Directory.CreateDirectory(Path.Combine(Source, "images", "123"));

        File.WriteAllText(Path.Combine(Source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Source, "items", "123.json"), "{ \"id\": \"123\" }");
        File.WriteAllBytes(Path.Combine(Source, "images", "123", "a.webp"), new byte[64]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 今の保存先が選んだ先の内側にあると、置き換えは始める前に断る。
    /// 前は選んだ先を退ける所で今の保存先ごと退けてしまい、運ぶ物が空のまま「成功」か、元を消す所で落ちていた。
    /// </summary>
    [Fact]
    public void RefusesToReplaceALibraryThatContainsTheCurrentOne()
    {
        var outer = Path.Combine(_root, "outer");
        var inner = Path.Combine(outer, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(outer, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(inner, "settings.json"), "{\"a\":1}");

        var result = StoreMover.Replace(inner, outer);

        Assert.False(result.Succeeded);
        Assert.Null(result.ParkedAt);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(Path.Combine(inner, "settings.json")));
        Assert.Equal(["inner", "settings.json"], Directory.EnumerateFileSystemEntries(outer).Select(entry => Path.GetFileName(entry)!).Order().ToArray());
    }

    /// <summary>運ぶ先が今の保存先の内側でも断る（運んだ物をまた運び、突き合わせで必ず落ちる）。</summary>
    [Fact]
    public void RefusesToMoveIntoItself()
    {
        var result = StoreMover.Move(Source, Path.Combine(Source, "next"));

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Copied);
        Assert.False(Directory.Exists(Path.Combine(Source, "next")));
    }

    /// <summary>
    /// 運んでいる間に書かれるログは突き合わせない。ログは門を通らずに書き足されるので、
    /// 大きさが食い違って「コピーの確認に失敗」になっていた。
    /// </summary>
    [Fact]
    public void DoesNotFailWhenTheLogGrowsWhileMoving()
    {
        Directory.CreateDirectory(Path.Combine(Source, "logs"));
        var log = Path.Combine(Source, "logs", "app.log");
        File.WriteAllText(log, "1行目\n");

        var progress = new SyncProgress(report =>
        {
            if (report.Copied == report.Total)
            {
                File.AppendAllText(log, "運んでいる間に書いた行\n");
            }
        });

        var result = StoreMover.Move(Source, Destination, progress);

        Assert.True(result.Succeeded, result.Error);
    }

    /// <summary>その場で呼ぶ進み具合（<see cref="Progress{T}"/> は後で呼ぶので、運び終わる前に書けない）。</summary>
    private sealed class SyncProgress(Action<StoreMoveProgress> report) : IProgress<StoreMoveProgress>
    {
        public void Report(StoreMoveProgress value) => report(value);
    }

    [Fact]
    public void CopiesEveryFileKeepingTheLayout()
    {
        var result = StoreMover.Move(Source, Destination);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Copied);
        Assert.True(File.Exists(Path.Combine(Destination, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Destination, "items", "123.json")));
        Assert.True(File.Exists(Path.Combine(Destination, "images", "123", "a.webp")));
    }

    /// <summary>コピーが済んでから元を消す。順序が逆だと、失敗したときに何も残らない。</summary>
    [Fact]
    public void RemovesTheSourceAfterCopying()
    {
        var result = StoreMover.Move(Source, Destination);

        Assert.True(result.SourceRemoved);
        Assert.False(File.Exists(Path.Combine(Source, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(Source, "items")));
    }

    /// <summary>
    /// 実行中のロックは持ち出さない。握ったまま運ぶことも、
    /// 移した先で二重に効かせることもできない。
    /// </summary>
    [Fact]
    public void DoesNotCarryTheLockFile()
    {
        File.WriteAllText(Path.Combine(Source, "app.lock"), string.Empty);

        var result = StoreMover.Move(Source, Destination);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Copied);
        Assert.False(File.Exists(Path.Combine(Destination, "app.lock")));
    }

    /// <summary>中断しても元は消さない。保存先を古いままにしておけば何も失われない。</summary>
    [Fact]
    public void LeavesTheSourceAloneWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = StoreMover.Move(Source, Destination, cancellationToken: cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(Source, "settings.json")));
    }

    /// <summary>
    /// 置き換えでも、選んだ場所にあったものは消さずに退ける。
    /// 消してからコピーすると、途中で失敗したときに両方失う。
    /// </summary>
    [Fact]
    public void ParksTheExistingLibraryInsteadOfDeletingIt()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "items"));
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{ \"old\": true }");
        File.WriteAllText(Path.Combine(Destination, "items", "999.json"), "{ \"id\": \"999\" }");

        var result = StoreMover.Replace(Source, Destination);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ParkedAt);

        // 新しい方が入っている
        Assert.Contains("\"id\": \"123\"", File.ReadAllText(Path.Combine(Destination, "items", "123.json")));

        // 古い方は退けてあるだけで、消えていない
        Assert.Contains("old", File.ReadAllText(Path.Combine(result.ParkedAt!, "settings.json")));
        Assert.True(File.Exists(Path.Combine(result.ParkedAt!, "items", "999.json")));
    }

    /// <summary>件数だけでは新旧が分からないので、最終更新も一緒に返す。</summary>
    [Fact]
    public void ReportsTheNewestWriteTime()
    {
        var summary = StoreMover.Summarize(Source);

        Assert.Equal(3, summary.Files);
        Assert.NotNull(summary.LastWrite);
        Assert.True(summary.LastWrite > DateTime.Now.AddMinutes(-5));
    }

    [Fact]
    public void ReportsAnEmptyFolderAsEmpty()
    {
        Directory.CreateDirectory(Destination);

        var summary = StoreMover.Summarize(Destination);

        Assert.Equal(0, summary.Files);
        Assert.Null(summary.LastWrite);
    }

    [Fact]
    public void MeasuresWhatWillBeMoved()
    {
        var (files, bytes) = StoreMover.Measure(Source);

        Assert.Equal(3, files);
        Assert.True(bytes >= 64);
    }
}

public class StoreLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "booth-loc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>商品が1件も無くても、こちらの管理ファイルがあれば「使われている場所」と見る。</summary>
    [Fact]
    public void RecognisesAFolderThatAlreadyHoldsAStore()
    {
        Directory.CreateDirectory(_root);
        Assert.False(StoreLocation.LooksLikeStore(_root));

        File.WriteAllText(Path.Combine(_root, "settings.json"), "{}");
        Assert.True(StoreLocation.LooksLikeStore(_root));
    }

    [Fact]
    public void TreatsAMissingFolderAsEmpty()
    {
        Assert.True(StoreLocation.IsEmpty(Path.Combine(_root, "nope")));

        Directory.CreateDirectory(_root);
        Assert.True(StoreLocation.IsEmpty(_root));

        File.WriteAllText(Path.Combine(_root, "x.txt"), "x");
        Assert.False(StoreLocation.IsEmpty(_root));
    }
}
