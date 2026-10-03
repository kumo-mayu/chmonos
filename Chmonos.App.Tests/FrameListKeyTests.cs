using System.Windows.Input;
using Chmonos.App.Controls;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件の並び（<see cref="FrameList"/>）で、枠の上のキーが何をするか。
/// フォーカスを実際に移す動きは窓が要るので、描く台（<c>ViewShot tabs search-many --keys …</c>）で通しで見る
/// </summary>
public class FrameListKeyTests
{
    [Fact]
    public void 枠の上のTabは並びの後ろへ抜け_中へは入らない()
    {
        // ユーザ判断 2026-10-02「3はそのように直してくれ」：前は枠の上の Tab で中へ入っていた
        Assert.Equal(FrameKey.ExitAfter, FrameList.OnFrame(Key.Tab, ModifierKeys.None));
    }

    [Fact]
    public void 枠の上のShiftTabは並びの前へ抜ける()
    {
        Assert.Equal(FrameKey.ExitBefore, FrameList.OnFrame(Key.Tab, ModifierKeys.Shift));
    }

    [Fact]
    public void 中へはEnterだけで入る()
    {
        Assert.Equal(FrameKey.Enter, FrameList.OnFrame(Key.Enter, ModifierKeys.None));
        Assert.Equal(FrameKey.None, FrameList.OnFrame(Key.Space, ModifierKeys.None));
    }

    [Fact]
    public void 上下とHomeEndは枠の間を移る()
    {
        Assert.Equal(FrameKey.Next, FrameList.OnFrame(Key.Down, ModifierKeys.None));
        Assert.Equal(FrameKey.Previous, FrameList.OnFrame(Key.Up, ModifierKeys.None));
        Assert.Equal(FrameKey.First, FrameList.OnFrame(Key.Home, ModifierKeys.None));
        Assert.Equal(FrameKey.Last, FrameList.OnFrame(Key.End, ModifierKeys.None));
    }

    [Theory]
    [InlineData(4, 3, false, 0)] // 最後の部品から Tab：最初へ戻る
    [InlineData(4, 0, true, 3)] // 最初の部品から Shift+Tab：最後へ戻る
    [InlineData(1, 0, false, 0)] // 部品が1つ：そこに止まる
    [InlineData(1, 0, true, 0)]
    public void 枠の中の端の部品からのTabは反対の端へ戻る(int count, int index, bool backward, int expected)
    {
        Assert.Equal(expected, FrameList.WrapTarget(count, index, backward));
    }

    [Theory]
    [InlineData(4, 1, false)]
    [InlineData(4, 2, true)]
    [InlineData(4, 0, false)] // 先頭からの Tab は WPF に任せる
    [InlineData(4, 3, true)]
    [InlineData(0, 0, false)]
    public void 枠の中の端でなければ戻さずWPFに任せる(int count, int index, bool backward)
    {
        Assert.Null(FrameList.WrapTarget(count, index, backward));
    }

    [Fact]
    public void 修飾キー付きは枠では受けない()
    {
        // Shift+F10 は条件のメニュー、Ctrl+Tab は画面の外の決まりに任せる
        Assert.Equal(FrameKey.None, FrameList.OnFrame(Key.Tab, ModifierKeys.Control));
        Assert.Equal(FrameKey.None, FrameList.OnFrame(Key.Down, ModifierKeys.Shift));
    }
}
