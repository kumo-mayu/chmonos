using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 打つ欄が1つの窓は、その欄にフォーカスを置いて開く（open.md 161・ui-dialogs.md「キー」）。
/// 前は窓そのものにフォーカスがあり、打つ前に欄を押す手間があった。
/// 見るのは窓の中の論理フォーカス（<see cref="FocusManager.GetFocusedElement(DependencyObject)"/>）。
/// キーボードのフォーカスは試験の窓が手前に来たかで変わるので、結果がマシンに左右される。
/// </summary>
public sealed class DialogInputFocusTests
{
    /// <summary>
    /// 窓を画面の外に出して開き、Loaded を走らせる。窓は主の窓を持ち主にするので、表示した主の窓の代わりを置く
    /// （無いと、作った窓そのものが主の窓になり、持ち主を自分に向けて落ちる）。
    /// </summary>
    private static async Task<IInputElement?> FocusedAfterOpenAsync(Func<Window> create, Action<Window>? inspect = null)
    {
        var owner = Offscreen(new Window { Width = 200, Height = 200 });
        owner.Show();
        var before = Application.Current.MainWindow;
        Application.Current.MainWindow = owner;
        Window? window = null;
        try
        {
            window = Offscreen(create());
            window.ShowActivated = true;
            window.Show();
            await UiThread.Settle();
            inspect?.Invoke(window);
            return FocusManager.GetFocusedElement(window);
        }
        finally
        {
            window?.Close();
            Application.Current.MainWindow = before;
            owner.Close();
        }
    }

    private static Window Offscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -3000;
        window.Top = 0;
        return window;
    }

    [Fact]
    public Task 名前を変更の窓は_新しい名前の欄に止まって開く() => UiThread.Run(async () =>
    {
        RenameTagDialog? window = null;
        var focused = await FocusedAfterOpenAsync(() =>
            window = new RenameTagDialog(new RenameTagDialogViewModel("大分類", "服", 3, ["衣装", "小物"])));

        var box = (SuggestBox)window!.FindName("NameBox");
        Assert.Same(box.FindName("Input"), focused);
    });

    [Fact]
    public Task 別の大分類へ移す窓は_大分類を選ぶ欄に止まって開く() => TestApp.Run(async app =>
    {
        MoveSubDialog? window = null;
        var focused = await FocusedAfterOpenAsync(() =>
            window = new MoveSubDialog(new MoveSubDialogViewModel(app.Services.UserTags, "服", "上着", ["衣装", "小物"])));

        var box = (SuggestBox)window!.FindName("TargetBox");
        Assert.Same(box.FindName("Input"), focused);
    });

    [Fact]
    public Task 名前を変更の窓は_開いた直後に候補を開かない() => UiThread.Run(async () =>
    {
        bool? open = null;
        await FocusedAfterOpenAsync(
            () => new RenameTagDialog(new RenameTagDialogViewModel("大分類", "服", 3, ["衣装", "小物"])),
            w => open = ((System.Windows.Controls.Primitives.Popup)((SuggestBox)w.FindName("NameBox")).FindName("DropDown")).IsOpen);

        Assert.False(open);
    });

    [Fact]
    public Task 別の大分類へ移す窓は_開いた直後に候補を開かない() => TestApp.Run(async app =>
    {
        bool? open = null;
        await FocusedAfterOpenAsync(
            () => new MoveSubDialog(new MoveSubDialogViewModel(app.Services.UserTags, "服", "上着", ["衣装", "小物"])),
            w => open = ((System.Windows.Controls.Primitives.Popup)((SuggestBox)w.FindName("TargetBox")).FindName("DropDown")).IsOpen);

        Assert.False(open);
    });

    [Fact]
    public Task 商品のIDを変える窓は_移し先の商品IDの欄に止まって開く() => TestApp.Run(async app =>
    {
        ChangeItemIdDialog? window = null;
        var focused = await FocusedAfterOpenAsync(() =>
            window = new ChangeItemIdDialog(new ChangeItemIdDialogViewModel(app.Services, "9900101", "作り物のコート")));

        Assert.IsType<TextBox>(focused);
        Assert.Same(window!.FindName("IdBox"), focused);
    });
}
