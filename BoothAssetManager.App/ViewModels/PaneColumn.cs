using System.Windows;
using BoothAssetManager.App.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// ドラッグで幅を変えられる列1つ（ユーザ判断 2026-09-14）。列の Width を <see cref="Width"/> へ TwoWay で結び、
/// 境目（<see cref="Controls.PaneSplitter"/>）の ResetCommand を <see cref="ResetCommand"/> へ結ぶ。
///
/// 畳める列（ナビ・検索の絞り込み）は、畳んだ幅は変えずに開いたときの幅だけを変える。
/// 決め打ちの幅にして境目を出さないこともできる（<see cref="Fixed"/>。今は未確定を組み込んだときの、隠した一覧だけ）。
/// 商品ページ・改変を組み込んだときは動かせる（ユーザ指示 2026-09-14）。そのときは単独の画面とは別の鍵で覚える。
/// </summary>
public sealed class PaneColumn : ViewModelBase
{
    private readonly PaneWidths _widths;
    private readonly string _key;
    private readonly double _collapsedWidth;
    private bool _isCollapsed;
    private RelayCommand? _resetCommand;

    /// <param name="followsResetAll">開きっぱなしの画面だけ true（設定画面の「全部戻す」をその場で映す）。</param>
    public PaneColumn(PaneWidths widths, string key, double collapsedWidth = 0, bool followsResetAll = false)
    {
        _widths = widths;
        _key = key;
        _collapsedWidth = collapsedWidth;

        if (followsResetAll)
        {
            widths.AllReset += () => OnPropertyChanged(nameof(Width));
        }
    }

    /// <summary>決め打ちの幅（組み込んだとき）。ある間は動かせない。</summary>
    public GridLength? Fixed { get; init; }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetField(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(CanResize));
                OnPropertyChanged(nameof(MinWidth));
                OnPropertyChanged(nameof(MaxWidth));
            }
        }
    }

    /// <summary>境目を出すか。</summary>
    public bool CanResize => Fixed is null && !IsCollapsed;

    public GridLength Width
    {
        get => Fixed ?? (IsCollapsed ? new GridLength(_collapsedWidth) : _widths.Column(_key));
        set
        {
            if (!CanResize || value.GridUnitType != GridUnitType.Pixel)
            {
                return;
            }

            _widths.Set(_key, value.Value);

            // 範囲の外まで引かれたら、範囲の端へ戻して見せる
            OnPropertyChanged();
            OnPropertyChanged(nameof(Pixels));
        }
    }

    /// <summary>今の幅（px）。畳んだ・決め打ちのときも、開いたときの幅を返す（全体の最小幅の計算に使う）。</summary>
    public double Pixels => _widths.Get(_key);

    /// <summary>列の最小。畳んだ幅が最小を下回るので、動かせるときだけ効かせる。</summary>
    public double MinWidth => CanResize ? PaneWidths.All[_key].Min : 0;

    public double MaxWidth => CanResize ? PaneWidths.All[_key].Max : double.PositiveInfinity;

    /// <summary>既定の幅に戻す（境目のダブルクリック）。</summary>
    public RelayCommand ResetCommand => _resetCommand ??= new RelayCommand(() =>
    {
        _widths.Reset(_key);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Pixels));
    });
}
