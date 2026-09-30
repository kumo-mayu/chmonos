using System.Runtime.ExceptionServices;
using BoothZipInspector;
using Xunit;

namespace BoothZipInspector.Tests;

/// <summary>Zone.Identifier を実際のファイルから読む所（<see cref="ZoneIdentifierReader.Read"/>）。</summary>
public sealed class ZoneIdentifierReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zone-reader-" + Guid.NewGuid().ToString("N"));

    public ZoneIdentifierReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string NewFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void ReadsStreamWhenPresent()
    {
        var path = NewFile("downloaded.zip");
        File.WriteAllText(
            path + ":Zone.Identifier",
            "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://booth.pm/ja/items/1234567\r\nHostUrl=https://booth.pm/\r\n");

        var info = ZoneIdentifierReader.Read(path);

        Assert.True(info.Found);
        Assert.Equal("3", info.ZoneId);
        Assert.Equal("1234567", info.BoothItemId);
    }

    [Fact]
    public void ReturnsNotFoundWithoutStream()
        => Assert.False(ZoneIdentifierReader.Read(NewFile("made-by-me.png")).Found);

    [Fact]
    public void ReturnsNotFoundForMissingFile()
        => Assert.False(ZoneIdentifierReader.Read(Path.Combine(_root, "gone.zip")).Found);

    /// <summary>
    /// 印の無いファイルで例外を飛ばさない。前は開いて失敗させていたので、ばらばらのファイル数万件の取り込みで
    /// 毎秒約3,000回の例外が飛んでいた。例外は同じスレッドで上がるので、ほかの試験の例外は数えない。
    /// </summary>
    [Fact]
    public void DoesNotThrowInternallyWithoutStream()
    {
        var path = NewFile("loose.png");
        var thread = Environment.CurrentManagedThreadId;
        var thrown = 0;
        void Count(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                thrown++;
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Count;
        try
        {
            for (var i = 0; i < 20; i++)
            {
                ZoneIdentifierReader.Read(path);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Count;
        }

        Assert.Equal(0, thrown);
    }
}
