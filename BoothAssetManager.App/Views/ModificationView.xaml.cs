using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ModificationView : UserControl
{
    public ModificationView()
    {
        InitializeComponent();

        // **落とすのと貼るのを、選ぶのと同じ道に通す。**
        // 1つの改変には何枚も撮るので、まとめて受けられないと手数が合わない
        Drop += OnDrop;
        DragOver += OnDragOver;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// 使ったものの「Unity ▾」。押すと、インポートと選択の2択を下に出す（ユーザ指示 2026-09-14：「開く」がエクスプローラなのか
    /// Unity なのか分かりにくかった）。項目はボタンの ContextMenu に置き、左クリックでも開く（右クリックでも同じ物が出る）
    /// </summary>
    private void OnUnityMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ModificationViewModel view
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        e.Handled = true;
        view.AddImageFilesAsync(paths).Forget();
    }

    /// <summary>
    /// Ctrl+V でクリップボードの絵を貼る。
    ///
    /// スクリーンショットは**ファイルではなく絵そのもの**で置かれるので、
    /// 落とす経路では受け取れない。入力欄にいるときは横取りしない——
    /// そこでの Ctrl+V は文字の貼り付けで、取り上げると打てなくなる。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V
            || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control
            || DataContext is not ModificationViewModel view
            || Keyboard.FocusedElement is TextBox or ComboBox)
        {
            return;
        }

        if (ReadClipboardImage() is not { } bytes)
        {
            return;
        }

        e.Handled = true;
        view.PasteImageAsync(bytes).Forget();
    }

    /// <summary>クリップボードの絵をPNGの生データにする。商品ページと同じ扱い。</summary>
    private static byte[]? ReadClipboardImage()
    {
        try
        {
            if (Clipboard.GetImage() is not { } source)
            {
                return null;
            }

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or NotSupportedException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            return null;
        }
    }
}
