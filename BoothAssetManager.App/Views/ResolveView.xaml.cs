using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ResolveView : UserControl
{
    private ResolveViewModel? _model;

    public ResolveView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.DecisionFocusRequested -= OnDecisionFocusRequested;
            }

            _model = DataContext as ResolveViewModel;
            if (_model is not null)
            {
                _model.DecisionFocusRequested += OnDecisionFocusRequested;
            }
        };
    }

    /// <summary>
    /// 商品IDの欄の上に落とされた物も、ウィンドウと同じ振り分けに回す（ユーザ指示 2026-09-17：画面のどこに落としても同じ）。
    /// 欄は文字のドロップを自分で受けて止めるので、ウィンドウまで届かない。欄のドロップを切ると、今度はその場所が受け先から外れて何も起きない
    /// （実際にドラッグして確かめた）。先に受けて、欄には渡さない。
    /// </summary>
    private void OnIdBoxPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || MainWindow.ReadText(e.Data) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnIdBoxPreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Window.GetWindow(this)?.DataContext is MainViewModel main)
        {
            MainWindow.Handle(main, e.Data.GetData(DataFormats.FileDrop) as string[], MainWindow.ReadText(e.Data),
                e.Data.GetDataPresent(DataFormats.Bitmap));
        }
    }

    /// <summary>「商品IDを決める」の欄を画面に入れる。候補は欄より下にあり、押した結果が見えなかった（ユーザ判断 2026-09-17）。</summary>
    private void OnDecisionFocusRequested() => DecisionCard.BringIntoView();
}
