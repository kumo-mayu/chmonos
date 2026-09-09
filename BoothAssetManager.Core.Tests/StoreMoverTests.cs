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
