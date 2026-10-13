using Chmonos.App.Services;

namespace Chmonos.App.Tests;

/// <summary>編集画面と API の書き込みの順序（点検37の[大]）。</summary>
public sealed class ItemEditGateTests
{
    [Fact]
    public void 編集画面で開いている商品には_札を渡さない()
    {
        var gate = new ItemEditGate();
        var editor = new object();
        Assert.True(gate.BeginEditAsync(editor, "1").IsCompleted);

        Assert.Null(gate.TryBeginApiWrite("1"));
        Assert.NotNull(gate.TryBeginApiWrite("2"));

        gate.EndEdit(editor);
        Assert.NotNull(gate.TryBeginApiWrite("1"));
    }

    [Fact]
    public void 書いている途中に開いた編集画面は_書き終えるまで読まずに待つ()
    {
        var gate = new ItemEditGate();
        var first = gate.TryBeginApiWrite("1")!;
        var second = gate.TryBeginApiWrite("1")!;

        var waiting = gate.BeginEditAsync(new object(), "1");
        Assert.False(waiting.IsCompleted);

        first.Dispose();
        first.Dispose();
        Assert.False(waiting.IsCompleted);

        second.Dispose();
        Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void 前の画面の閉じた知らせは_同じ商品を開き直した新しい画面の印を外さない()
    {
        var gate = new ItemEditGate();
        var old = new object();
        var current = new object();
        gate.BeginEditAsync(old, "1");
        gate.BeginEditAsync(current, "1");

        gate.EndEdit(old);

        Assert.Equal("1", gate.EditingItemId);
        Assert.Null(gate.TryBeginApiWrite("1"));
    }

    /// <summary>離れた画面の待っていた処理が後から「開いた」と言っても、今の画面の印を置き換えない（点検38）。</summary>
    [Fact]
    public void 離れた画面の開いたは_後から来ても受け付けない()
    {
        var gate = new ItemEditGate();
        var old = new object();
        var current = new object();
        gate.BeginEditAsync(old, "1");
        gate.EndEdit(old);
        gate.BeginEditAsync(current, "2");

        gate.BeginEditAsync(old, "3");

        Assert.Equal("2", gate.EditingItemId);
        Assert.Null(gate.TryBeginApiWrite("2"));
        Assert.NotNull(gate.TryBeginApiWrite("3"));
    }
}
