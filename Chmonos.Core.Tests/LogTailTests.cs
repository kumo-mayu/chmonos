using System.Text;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>書き足されていく Editor.log を、前回の続きから読む（#69・§11-3）。</summary>
public sealed class LogTailTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "logtail-" + Guid.NewGuid().ToString("N")[..8] + ".log");

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // 後片付けで落ちる必要はない
        }
    }

    private void Append(string text) => File.AppendAllText(_path, text, new UTF8Encoding(false));

    [Fact]
    public void 作った時点より前の行は読まない()
    {
        Append("送る前の行\n");
        var tail = new LogTail(_path);
        Append("送った後の行\n");

        Assert.Equal(["送った後の行"], tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());
    }

    [Fact]
    public void 途中で切れた行は次に回す()
    {
        // 1行が2回に割れて届くと、パスの突き合わせを誤る
        Append("");
        var tail = new LogTail(_path);
        Append("Start importing Assets/nHaru");
        Assert.Empty(tail.ReadNewLines());

        Append("ka/Pen.asset using Guid(1)\r\n");
        Assert.Equal(["Start importing Assets/nHaruka/Pen.asset using Guid(1)"], tail.ReadNewLines());
    }

    [Fact]
    public void 縮んだら先頭から読み直す()
    {
        // 後から起動したエディタは、共有の Editor.log を先頭から書き直す
        Append("前のエディタの長い行がたくさん\n前のエディタの長い行がたくさん\n");
        var tail = new LogTail(_path);
        File.WriteAllText(_path, "新しいエディタの行\n", new UTF8Encoding(false));

        Assert.Equal(["新しいエディタの行"], tail.ReadNewLines());
    }

    [Fact]
    public void 空白の塊は読み飛ばす()
    {
        // 2つのエディタが同じファイルの別の位置に書くと、間が NUL で埋まる
        Append("");
        var tail = new LogTail(_path);
        Append("\0\0\0\0\n行\n");

        Assert.Equal(["行"], tail.ReadNewLines());
    }

    [Fact]
    public void 書き手が開いたままでも読める()
    {
        using var writer = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var tail = new LogTail(_path);
        var bytes = Encoding.UTF8.GetBytes("Unity が書いている最中の行\n");
        writer.Write(bytes);
        writer.Flush();

        Assert.Equal(["Unity が書いている最中の行"], tail.ReadNewLines());
    }

    [Fact]
    public void ファイルが無ければ空()
        => Assert.Empty(new LogTail(_path + ".none").ReadNewLines());
}
