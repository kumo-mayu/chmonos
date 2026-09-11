using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ImportViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            // 落としたらそのまま始める（#38。設定で切れる）
            viewModel.AddDroppedPaths(paths, startImmediately: viewModel.StartsOnDrop);
        }

        e.Handled = true;
    }
}
