using System.Windows;
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
}
