using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// ファイルの割り振りのサブ画面（plan 30）。主の窓を塞がない別の窓（docs/spec/ui-subwindows.md）。
///
/// 持ち主は主の窓：主の窓を閉じる・畳むと一緒に閉じる・畳まれ、主の窓の後ろに隠れない。
/// 小窓（<see cref="Services.DialogFit"/>）と違って開いている間に表示の大きさ・色が変わるので、
/// 倍率は資源を指して後から変わっても当て直し、題の帯は色を変えたときにアプリの窓すべてへ当て直される（<see cref="AppTheme"/>）。
/// 位置と大きさはアプリを閉じるまで覚え、ファイル（ui-state.json）には書かない（形を変えない）。
/// </summary>
public partial class FilePoolWindow : Window, ISubWindowHandle
{
    private static Rect? s_lastBounds;

    internal FilePoolWindow(FilePoolViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } main ? main : null;
        Root.SetResourceReference(LayoutTransformProperty, AppZoom.TransformResourceKey);
        Width *= Services.DisplayScale.Zoom;
        Height *= Services.DisplayScale.Zoom;
        AppTheme.Watch(this);

        if (s_lastBounds is { } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        // 位置は閉じる直前に採る（Closed の後は RestoreBounds が空になる。主の窓と同じ）
        Closing += (_, _) => s_lastBounds = RestoreBounds.IsEmpty ? null : RestoreBounds;
        Closed += (_, _) => model.NoteWindowClosed();
    }

    /// <summary>作って出す。主の窓は止めない（Show。ShowDialog にしない）。</summary>
    internal static ISubWindowHandle Open(FilePoolViewModel model)
    {
        var window = new FilePoolWindow(model);
        window.Show();
        return window;
    }

    /// <summary>2回目に開こうとしたとき：畳んでいれば戻して、前に出す。</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
