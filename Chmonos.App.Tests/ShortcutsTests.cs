using System.Windows.Input;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// ショートカットの文字（設定に書く "Ctrl+Enter"）とキーの行き来、設定画面に見せる形、押されたキーが当たるか。
/// 前は設定の画面で割り当てて、実際にキーを押して確かめていた。
/// </summary>
public class ShortcutsTests
{
    [Theory]
    [InlineData("Ctrl+Enter", Key.Return, ModifierKeys.Control)]
    [InlineData("Ctrl+Shift+Right", Key.Right, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Alt+Left", Key.Left, ModifierKeys.Alt)]
    [InlineData("F5", Key.F5, ModifierKeys.None)]
    [InlineData(" ctrl + f ", Key.F, ModifierKeys.Control)]
    [InlineData("Control+Win+A", Key.A, ModifierKeys.Control | ModifierKeys.Windows)]
    public void 設定の文字をキーに読む(string text, Key key, ModifierKeys modifiers)
        => Assert.Equal((key, modifiers), Shortcuts.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+どこにも無いキー")]
    [InlineData("None")]
    public void 読めない文字は_割り当てなしとして読む(string? text)
        // 手で直した設定で落ちないように、読めない物は投げずに null
        => Assert.Null(Shortcuts.Parse(text));

    [Theory]
    [InlineData(Key.Return, ModifierKeys.Control, "Ctrl+Enter")]
    [InlineData(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+Right")]
    [InlineData(Key.F, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, "Ctrl+Alt+Shift+Win+F")]
    [InlineData(Key.F5, ModifierKeys.None, "F5")]
    public void 押されたキーを設定に書く形にする(Key key, ModifierKeys modifiers, string expected)
        => Assert.Equal(expected, Shortcuts.Format(key, modifiers));

    [Fact]
    public void 書いた文字は_同じキーとして読み戻せる()
    {
        foreach (var (key, modifiers) in new[]
                 {
                     (Key.Return, ModifierKeys.Control),
                     (Key.OemPlus, ModifierKeys.Control),
                     (Key.NumPad0, ModifierKeys.Control),
                     (Key.Left, ModifierKeys.Alt | ModifierKeys.Shift),
                 })
        {
            Assert.Equal((key, modifiers), Shortcuts.Parse(Shortcuts.Format(key, modifiers)));
        }
    }

    [Theory]
    [InlineData(null, "（割り当てなし）")]
    [InlineData("", "（割り当てなし）")]
    [InlineData("Ctrl+Enter", "Ctrl + Enter")]
    [InlineData("Alt+Left", "Alt + 左矢印")]
    [InlineData("Ctrl+Shift+Right", "Ctrl + Shift + 右矢印")]
    [InlineData("Ctrl+OemPlus", "Ctrl + プラス")]
    [InlineData("Ctrl+OemMinus", "Ctrl + マイナス")]
    [InlineData("Ctrl+D0", "Ctrl + 0")]
    [InlineData("Ctrl+NumPad0", "Ctrl + テンキーの0")]
    [InlineData("Ctrl+Add", "Ctrl + テンキーのプラス")]
    public void 設定画面には_読める言葉で見せる(string? gesture, string expected)
        // 記号の「→」は小さい字だと横棒に見え、OemPlus・D0 は .NET の名前のままだと読めない
        => Assert.Equal(expected, Shortcuts.Display(gesture));

    [Fact]
    public void 同じキーと同じ修飾なら当たり_修飾が違えば当たらない()
    {
        var gesture = (Key.Return, ModifierKeys.Control);

        Assert.True(Shortcuts.Matches(gesture, Key.Return, ModifierKeys.Control));
        Assert.False(Shortcuts.Matches(gesture, Key.Return, ModifierKeys.None));
        Assert.False(Shortcuts.Matches(gesture, Key.Return, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(Shortcuts.Matches(gesture, Key.Space, ModifierKeys.Control));
    }

    [Fact]
    public void テンキーのプラス_マイナス_数字は_上の段のキーと同じに当たる()
    {
        // ブラウザの拡大と同じ
        Assert.True(Shortcuts.Matches((Key.OemPlus, ModifierKeys.Control), Key.Add, ModifierKeys.Control));
        Assert.True(Shortcuts.Matches((Key.OemMinus, ModifierKeys.Control), Key.Subtract, ModifierKeys.Control));
        Assert.True(Shortcuts.Matches((Key.D0, ModifierKeys.Control), Key.NumPad0, ModifierKeys.Control));
        Assert.True(Shortcuts.Matches((Key.NumPad0, ModifierKeys.Control), Key.D0, ModifierKeys.Control));
        Assert.False(Shortcuts.Matches((Key.D0, ModifierKeys.Control), Key.NumPad1, ModifierKeys.Control));
    }

    [Fact]
    public void プラスの割り当ては_Shiftを足して押しても当たる()
    {
        // 「＋」を打つのに Shift が要る配列がある。Ctrl＋＋ と覚えた人は、そのまま Shift も押す
        var gesture = (Key.OemPlus, ModifierKeys.Control);

        Assert.True(Shortcuts.Matches(gesture, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(Shortcuts.Matches(gesture, Key.OemPlus, ModifierKeys.Control));

        // マイナスには当てはめない（Shift＋マイナスは別の記号）
        Assert.False(Shortcuts.Matches(
            (Key.OemMinus, ModifierKeys.Control), Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void 文字の欄でカーソルを動かすキーを見分ける()
    {
        foreach (var key in new[] { Key.Left, Key.Right, Key.Up, Key.Down, Key.Home, Key.End, Key.Back, Key.Delete })
        {
            Assert.True(Shortcuts.IsTextEditingKey(key), key.ToString());
        }

        Assert.False(Shortcuts.IsTextEditingKey(Key.Return));
        Assert.False(Shortcuts.IsTextEditingKey(Key.F));
    }

    [Fact]
    public void どの操作も_設定の欄と名前を持つ()
    {
        var defaults = new ShortcutSettings();

        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            // 操作を足したのに対応を書き忘れると、設定画面の行が空になる・割り当てても保存されない
            Assert.NotEqual(action.ToString(), Shortcuts.ActionLabel(action));
            Assert.NotNull(Shortcuts.Parse(Shortcuts.GestureOf(defaults, action)));

            var changed = Shortcuts.With(defaults, action, "Ctrl+F12");
            Assert.Equal("Ctrl+F12", Shortcuts.GestureOf(changed, action));
        }
    }

    [Fact]
    public void ある操作の割り当てを変えても_ほかの操作は変わらない()
    {
        var defaults = new ShortcutSettings();

        var changed = Shortcuts.With(defaults, ShortcutAction.Skip, "Ctrl+F12");

        foreach (var action in Enum.GetValues<ShortcutAction>().Where(action => action != ShortcutAction.Skip))
        {
            Assert.Equal(Shortcuts.GestureOf(defaults, action), Shortcuts.GestureOf(changed, action));
        }
    }

    [Fact]
    public void 既定の割り当ては_互いにぶつからない()
    {
        var defaults = new ShortcutSettings();

        var gestures = Enum.GetValues<ShortcutAction>()
            .Select(action => Shortcuts.Parse(Shortcuts.GestureOf(defaults, action)))
            .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }
}
