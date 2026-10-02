using System.Windows.Input;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の「スキップ」「← 前へ」のキー（ユーザ判断 2026-10-02 メモ4：Ctrl+N＝スキップ・Ctrl+P＝前へ。どちらも入力欄の中でも効く）。
/// 前は Ctrl+Shift+→／← で、前へだけは欄の中で効かず、Shift が要るのかどうかもぶれて見えた。キーは画面のどこにも出ていなかった
/// </summary>
public class EditStepKeysTests
{
    [Fact]
    public void 既定は_スキップが_Ctrl_N_で_前へが_Ctrl_P()
    {
        var shortcuts = new ShortcutSettings();

        Assert.Equal((Key.N, ModifierKeys.Control), Shortcuts.Parse(Shortcuts.GestureOf(shortcuts, ShortcutAction.Skip)));
        Assert.Equal((Key.P, ModifierKeys.Control), Shortcuts.Parse(Shortcuts.GestureOf(shortcuts, ShortcutAction.Previous)));
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.Control)]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.Return, ModifierKeys.Control)]
    [InlineData(Key.Left, ModifierKeys.Alt)]
    [InlineData(Key.Right, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Left, ModifierKeys.Control | ModifierKeys.Shift)]
    public void 入力欄の中でも_アプリが受けるキー(Key key, ModifierKeys modifiers)
        // 前へ（Ctrl+P）も欄の中で効く。前は前へだけ欄に譲っていた
        => Assert.False(Shortcuts.YieldsToText(key, modifiers));

    [Theory]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.P, ModifierKeys.Shift)]
    [InlineData(Key.Left, ModifierKeys.None)]
    [InlineData(Key.Left, ModifierKeys.Shift)]
    [InlineData(Key.Right, ModifierKeys.Control)]
    [InlineData(Key.Home, ModifierKeys.Control)]
    public void 入力欄に譲るキー_文字を打つ_カーソルを動かす(Key key, ModifierKeys modifiers)
        => Assert.True(Shortcuts.YieldsToText(key, modifiers));

    private static async Task<EditViewModel> OpenEditAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        return Assert.IsType<EditViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task ボタンの吹き出しに_今の割り当てのキーを出す() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);

        Assert.Equal("入力を書きかけのまま、次の商品へ進みます（Ctrl + N）", edit.SkipTip);
        Assert.Equal("前の商品へ戻ります（Ctrl + P）", edit.BackTip);
    });

    [Fact]
    public Task 設定でキーを変えたら_吹き出しも追い_外したらキーを出さない() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);

        await app.ChangeSettingsAsync(settings => settings with
        {
            Shortcuts = settings.Shortcuts with { Skip = "Ctrl+Shift+Right", Previous = string.Empty },
        });

        Assert.Equal("入力を書きかけのまま、次の商品へ進みます（Ctrl + Shift + 右矢印）", edit.SkipTip);
        Assert.Equal("前の商品へ戻ります。", edit.BackTip);
    });

    [Fact]
    public Task Ctrl_N_でスキップし_Ctrl_P_で前へ戻る() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app);
        var first = edit.CurrentItemId;

        Assert.True(app.Main.RunShortcut(Match(Key.N, ModifierKeys.Control)));
        await app.SettleAsync();
        Assert.NotEqual(first, edit.CurrentItemId);

        Assert.True(app.Main.RunShortcut(Match(Key.P, ModifierKeys.Control)));
        await app.SettleAsync();
        Assert.Equal(first, edit.CurrentItemId);

        // 押されたキーに当たる操作（窓のキーの受け口と同じ探し方）
        ShortcutAction Match(Key key, ModifierKeys modifiers)
            => Enum.GetValues<ShortcutAction>().Single(action =>
                Shortcuts.Parse(Shortcuts.GestureOf(app.Main.Shortcuts, action)) is { } gesture && Shortcuts.Matches(gesture, key, modifiers));
    });
}
