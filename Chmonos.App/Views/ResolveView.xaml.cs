using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

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
                _model.CandidatesFocusRequested -= OnCandidatesFocusRequested;
                _model.PropertyChanged -= OnModelPropertyChanged;
            }

            _model = DataContext as ResolveViewModel;
            if (_model is not null)
            {
                _model.DecisionFocusRequested += OnDecisionFocusRequested;
                _model.CandidatesFocusRequested += OnCandidatesFocusRequested;
                _model.PropertyChanged += OnModelPropertyChanged;
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

    /// <summary>
    /// 「BOOTHに無い商品として登録する」の枠に落とした画像は、登録と一緒に入れる画像として添える（ユーザ判断 2026-09-29）。
    /// **画像だけのときに限って受ける。**zip や商品ページはいつも通りウィンドウの振り分け（取り込み・商品IDとして入れる）に任せる。
    /// 画像も普段は振り分けで「取り込み」に積まれてしまうので、この枠の上だけは先に受けて止める。
    /// </summary>
    private void OnLocalBoxPreviewDragOver(object sender, DragEventArgs e)
    {
        if (DroppedImages(e) is not null)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnLocalBoxPreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedImages(e) is { } images && _model is not null)
        {
            e.Handled = true;
            _model.AddLocalImages(images);
        }
    }

    private static string[]? DroppedImages(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            && paths.All(Core.Services.DropRouting.LooksLikeImage)
                ? paths
                : null;

    /// <summary>
    /// 選んだ行まで一覧を送る。「元zipで登録」や確定の後に選ぶ行が画面の外だと、左で何を選んでいるか分からなかった（ユーザ判断 2026-09-17）。
    /// 選択が変わった直後は検索を消した後の並べ直しが済んでいないことがあるので、描画の後に送る。
    /// </summary>
    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResolveViewModel.Selected) && _model?.Selected is { } selected)
        {
            Dispatcher.BeginInvoke(() => FilesList.ScrollIntoView(selected), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 右クリックした行を先に選ぶ（ユーザ指示 2026-09-20・M2）。
    /// 未確定の操作は「選んでいる物」に効くので、選ばずにメニューを出すと、別の行に効いてしまう。
    /// </summary>
    private void OnFileRowRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject source
            && ItemsControl.ContainerFromElement(FilesList, source) is System.Windows.Controls.ListBoxItem row)
        {
            row.IsSelected = true;
        }
    }

    /// <summary>「商品IDを決める」の欄を画面に入れる。候補は欄より下にあり、押した結果が見えなかった（ユーザ判断 2026-09-17）。</summary>
    private void OnDecisionFocusRequested() => DecisionCard.BringIntoView();

    /// <summary>「候補」の欄を画面に入れる。自動検索のボタンは上にあり、進み具合と結果は下の候補の欄に出る（ユーザ指示 2026-09-29）。</summary>
    private void OnCandidatesFocusRequested() => CandidatesCard.BringIntoView();
}
