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

    /// <summary>
    /// 外から来た値の改行・制御文字・向きを変える字は見える形にして1行に収め、長すぎる文は切る（外部の点検 2026-10-06）。
    /// 例外の中身は、今までどおり字下げした複数行で残す
    /// </summary>
    [Fact]
    public void 文の改行と制御文字は1行に収め_例外は複数行のまま()
    {
        var path = Path.Combine(_root, "app.log");
        var log = new LogFile(path);

        var rtl = (char)0x202E;
        log.Write("WARN", "読む\n2026-01-01 00:00:00.000 ERROR [偽]", "ID「a\r\n2026-01-01 00:00:00.000 ERROR [偽] 消した」" + rtl + "は扱わない" + new string('x', 5000), new InvalidOperationException("一\n二"));

        var lines = File.ReadAllLines(path);
        Assert.DoesNotContain(lines, line => line.StartsWith("2026-01-01", StringComparison.Ordinal));
        Assert.Contains(@"\u000A", lines[0]);
        Assert.Contains(@"\u202E", lines[0]);
        Assert.DoesNotContain(rtl, lines[0]);
        Assert.True(lines[0].Length < 2300);
        Assert.Contains(lines.Skip(1), line => line.Trim() == "二");
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
