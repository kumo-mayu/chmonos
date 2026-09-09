using System.Windows;
using System.Linq;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 前回の位置と大きさで開く。
    ///
    /// 保存した矩形が今あるモニタのどれとも重ならなければ捨てて中央に開く。
    /// モニタを外したり配置を変えたりすると画面の外に開いてしまい、
    /// タイトルバーが掴めず動かせなくなるため。
    /// </summary>
    public void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < 320 || placement.Height < 240)
        {
            return;
        }

        if (!IsOnAnyScreen(placement))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;

        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 今の姿を保存できる形にする。
    /// 最大化中は <see cref="Window.RestoreBounds"/>（解除したときの姿）を採る。
    /// 最大化中の値を書くと、次に解除したときに画面いっぱいのまま戻らなくなる。
    /// </summary>
    public WindowPlacement CurrentPlacement()
    {
        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized || WindowState == WindowState.Minimized ? RestoreBounds : new Rect(Left, Top, Width, Height);

        return new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = maximized,
        };
    }

    /// <summary>
    /// 今の画面の範囲に掛かっているか。
    ///
    /// 全モニタを囲む矩形（仮想画面）と重なるかで見る。WinFormsのScreenやWin32を使うと
    /// 物理ピクセルで返るので、DIPで持っているこちらの値と混ざる（複数DPIだと実際にずれる）。
    /// 判定は緩いが、狙いは「モニタを外したときに画面外へ開かない」ことなので、これで足りる。
    ///
    /// 少しでも掛かっていればよい。掴んで動かせるなら、そこから直せる。
    /// </summary>
    private static bool IsOnAnyScreen(WindowPlacement placement)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        return screen.IntersectsWith(new Rect(placement.Left, placement.Top, placement.Width, placement.Height));
    }

    /// <summary>
    /// 落ちてくるものは2種類しかない（ファイルかBOOTHのURL）ので、
    /// 画面ごとではなくウィンドウで受ける。画面ごとに受けると、
    /// 同じものを落としたのに画面によって結果が変わる。
    /// </summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel main)
        {
            return;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var text = e.Data.GetData(DataFormats.UnicodeText) as string;

        e.Handled = true;
        Handle(main, paths, text);
    }

    /// <summary>
    /// 落ちてきたものを振り分ける。
    ///
    /// 投げっぱなしにしないのは、**失敗すると何も起きないように見える**ため。
    /// 落としたのに無反応だと、受け付けていないのか壊れているのか分からない。
    /// </summary>
    private static async void Handle(MainViewModel main, IReadOnlyList<string>? paths, string? text)
    {
        try
        {
            await main.HandleDropAsync(paths, text);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"受け取ったものを処理できませんでした。\n\n{exception.Message}",
                "BOOTH Asset Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 貼り付けもドロップと同じ扱い。マウスだけで完結させたいならドロップ、
    /// キーボードが使えるなら貼り付けの方が速い。
    ///
    /// 入力欄にいるときは何もしない。そこでの Ctrl+V は文字を貼る操作で、
    /// 横取りすると打てなくなる。
    /// </summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.V
            || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0
            || DataContext is not MainViewModel main)
        {
            return;
        }

        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return;
        }

        var paths = Clipboard.ContainsFileDropList()
            ? Clipboard.GetFileDropList().Cast<string>().Where(path => path is not null).ToList()
            : null;

        var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;

        if (paths is { Count: > 0 } || !string.IsNullOrWhiteSpace(text))
        {
            e.Handled = true;
            Handle(main, paths, text);
        }
    }
}
