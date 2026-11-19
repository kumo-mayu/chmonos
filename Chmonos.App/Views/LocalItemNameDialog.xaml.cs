using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>ファイルを持たない「BOOTHに無い商品」を作るときに名前を聞く小窓。</summary>
public partial class LocalItemNameDialog : Window
{
    public LocalItemNameDialog(LocalItemNameDialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);

        // 開いたらすぐ打てるように名前の欄に止める。Enter で決まる
        Loaded += (_, _) => NameBox.Focus();
    }

    // ドロップは窓のどこでも受ける（未確定の登録の欄が、枠の中のどこでも受けるのと揃える）
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (DroppedImages(e) is not null)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedImages(e) is { } images && DataContext is LocalItemNameDialogViewModel model)
        {
            e.Handled = true;
            model.AddLocalImages(images);
        }
    }

    private static string[]? DroppedImages(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            && paths.All(Core.Services.DropRouting.LooksLikeImage)
                ? paths
                : null;

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
