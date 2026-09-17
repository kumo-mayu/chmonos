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

    /// <summary>「商品IDを決める」の欄を画面に入れる。候補は欄より下にあり、押した結果が見えなかった（ユーザ判断 2026-09-17）。</summary>
    private void OnDecisionFocusRequested() => DecisionCard.BringIntoView();
}
