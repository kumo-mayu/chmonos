using Chmonos.Core.Diagnostics;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>失敗を書き残すファイル（技術的負債 2-1、2026-09-14）。</summary>
public sealed class LogFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-log-" + Guid.NewGuid().ToString("N"));

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
    public void 何をしていたかと例外の中身を書き足す()
    {
        var log = new LogFile(Path.Combine(_root, "logs", "app.log"));

        log.Write("失敗", "期限の来た商品の取り直し", "つながりませんでした", new InvalidOperationException("つながりませんでした"));
        log.Write("注意", "取り込み", "2行目");

        var lines = File.ReadAllLines(log.Path);
        Assert.Contains("[期限の来た商品の取り直し] つながりませんでした", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("    System.InvalidOperationException", StringComparison.Ordinal));
        Assert.Contains("[取り込み] 2行目", lines[^1]);
    }

    [Fact]
    public void 大きくなったら1つ前へ回して2つだけ残す()
    {
        var log = new LogFile(Path.Combine(_root, "app.log"), maxBytes: 100);

        log.Write("注意", "一", new string('あ', 60));
        log.Write("注意", "二", "回した後の最初の行");

        Assert.Contains("[一]", File.ReadAllText(log.OldPath));
        Assert.DoesNotContain("[一]", File.ReadAllText(log.Path));
        Assert.Contains("[二]", File.ReadAllText(log.Path));
    }

    [Fact]
    public void 書き先が決まる前は何もしない()
    {
        AppLog.Error("試験", new InvalidOperationException());
    }
}
