using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 幅を変えられる列（<see cref="PaneColumn"/>）を持つ Grid。覚えた幅を、親から渡された幅に合わせて頭打ちにする。
///
/// 覚えた幅は窓とは無関係に決まるので、広い窓で広げたあと窓を狭めると、反対側の列が最小を割って潰れていた
/// （点検 2026-09-23：商品ページの右が約130pxになり、商品名とタグが切れた）。
///
/// **大きさの変化（SizeChanged）ではなく、測る段階で親が渡す幅を使う。**列の最小の合計が親の幅を超えると、
/// WPF は Grid を膨らんだ幅のまま並べて外を切るので、窓を狭めても Grid の幅は変わらず、知らせが来なかった。
/// 測る段階の幅は膨らむ前の「親が使ってよいと言った幅」なので、ここでだけ正しく分かる。
///
/// 反対側に残す幅は、「*」の列の MinWidth の合計（どの画面も反対側を「*」と最小で書いている）。
/// 画面ごとに数字を書き直させないのは、XAML の最小と二重に持つと片方だけ直す事故が起きるため。
/// 横に送れる入れ物の中（幅に上限が無い）では何もしない。
/// </summary>
public sealed class PaneGrid : Grid
{
    public static readonly DependencyProperty PaneProperty = DependencyProperty.Register(
        nameof(Pane), typeof(PaneColumn), typeof(PaneGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public PaneColumn? Pane
    {
        get => (PaneColumn?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    private ViewportFitHost? _host;

    public PaneGrid()
    {
        Loaded += (_, _) => AttachToHost();
        Unloaded += (_, _) =>
        {
            _host?.Untrack(this);
            _host = null;
        };
    }

    /// <summary>
    /// 窓が左右の列の最小の合計より狭いとき、画面ごと横に送れるよう外側の入れ物に知らせる（ユーザ判断 2026-09-23）。
    /// 自分で横に送る画面（商品ページ・改変の詳細）の中にあるときは、その画面が届かせるので知らせない
    /// </summary>
    private void AttachToHost()
    {
        _host?.Untrack(this);
        _host = null;
        for (var node = System.Windows.Media.VisualTreeHelper.GetParent(this); node is not null;
             node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is ViewportFitHost host)
            {
                _host = host;
                host.Track(this);
                return;
            }

            if (node is ScrollViewer { HorizontalScrollBarVisibility: not ScrollBarVisibility.Disabled })
            {
                return;
            }
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Pane is { } pane && !double.IsInfinity(constraint.Width))
        {
            var reserve = 0.0;
            foreach (var column in ColumnDefinitions)
            {
                if (column.Width.IsStar)
                {
                    reserve += column.MinWidth;
                }
            }

            // 幅が変わると列の Width が結び先から書き換わり、この Grid の測り直しがもう一度来る。
            // 同じ幅なら Fit は何もしないので、2回目で落ち着く
            pane.Fit(constraint.Width, reserve);
        }

        return base.MeasureOverride(constraint);
    }
}
