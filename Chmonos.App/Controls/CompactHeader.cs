using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 名前の段が流れ去ったら、流す中身の上に**重ねて**出す1行の見出し（ショップの中・商品ページ・編集の画面の左）。
/// 重ねるだけで中身の大きさは変えないので、出入りで見ている所が動かない（ユーザ判断 2026-10-03 のショップの決まりを、2026-11-11 に商品ページと編集の画面へ広げた）。
/// 出す境目・高さ・帯の上のホイールの渡し方は、3つの画面で同じ物を使う。
/// </summary>
public static class CompactHeader
{
    /// <summary>
    /// 1行の見出しの高さ。帯の XAML の Height と揃える。ショップでは「このショップの商品」の行が止まる高さにもなる。
    /// 32 の絵と2行にならない名前（15）が上下に余白を持って収まる高さ（ショップで決めた値。ほかの画面も揃える）
    /// </summary>
    public const double Height = 52;

    /// <summary>
    /// 1行の見出しを出すか：名前の段の下端が上端に着いたとき。名前の段が見えている間は同じ物が2つ並ぶので出さない。
    /// 段の下端がまだ測れていない間（0以下）は出さない。端数（拡大率・画素の丸め）は0.5まで一番上として扱う
    /// </summary>
    /// <param name="offset">流れの位置（一番上が0）。</param>
    /// <param name="anchorBottom">名前の段の下端（流す中身の上からの位置）。</param>
    public static bool Shows(double offset, double anchorBottom) => anchorBottom > 0 && offset >= anchorBottom - 0.5;

    /// <summary>
    /// 部品の所まで流す位置。行き先の上に <paramref name="margin"/> を空け、流した先で見出しが出るなら、その高さの分も空ける
    /// （空けないと、行き先が重ねた帯の下に隠れる）。
    /// </summary>
    /// <param name="top">行き先の上端（流す中身の上からの位置）。</param>
    /// <param name="margin">上に空ける余白。</param>
    /// <param name="anchorBottom">名前の段の下端。</param>
    public static double RevealOffset(double top, double margin, double anchorBottom)
    {
        var plain = Math.Max(0, top - margin);
        return Shows(plain, anchorBottom) ? Math.Max(0, plain - Height) : plain;
    }

    /// <summary>名前の段の下端を、流す中身の上からの位置で測る（流れの位置に左右されない）。まだ並んでいない・木から外れた間は null。</summary>
    public static double? AnchorBottom(FrameworkElement anchor, ScrollViewer viewer)
    {
        if (viewer.Content is not Visual content || !anchor.IsVisible)
        {
            return null;
        }

        try
        {
            return anchor.TransformToAncestor(content).Transform(new Point(0, anchor.ActualHeight)).Y;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 帯の上のホイールも、下の中身を流す。帯は流す中身の外に重ねてあるので、そのままでは受け手が無い
    /// </summary>
    public static void ForwardWheel(UIElement bar, ScrollViewer viewer)
    {
        bar.MouseWheel += (_, e) =>
        {
            viewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = bar,
            });
            e.Handled = true;
        };
    }
}

/// <summary>
/// 商品ページと編集の画面の左で、1行の見出しの出し入れを流れに合わせる。
/// 状態は画面の側に持つ：商品ページは同じ画面に別の商品が差し込まれる（DataContext だけが替わる）ので、
/// ViewModel に持たせると、位置を保ったままの開き直しで帯が消えたままになる。
/// 中身が替わる・名前の段の高さが変わる・流れる、のどれでも測り直す。
/// </summary>
public sealed class CompactHeaderTracker
{
    private readonly InsetScrollViewer _viewer;
    private readonly FrameworkElement _anchor;
    private readonly FrameworkElement _bar;

    public CompactHeaderTracker(InsetScrollViewer viewer, FrameworkElement anchor, FrameworkElement bar)
    {
        _viewer = viewer;
        _anchor = anchor;
        _bar = bar;

        // 中の文字の箱や一覧も同じ知らせを上げる。流す部品の分だけ見る（伸び縮み・窓の大きさの変化もこの知らせで来る）
        viewer.ScrollChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, viewer))
            {
                Update();
            }
        };
        anchor.SizeChanged += (_, _) => Update();
        anchor.IsVisibleChanged += (_, _) => Update();
        CompactHeader.ForwardWheel(bar, viewer);
        Update();
    }

    /// <summary>今、帯を出しているか。</summary>
    public bool IsShown { get; private set; }

    /// <summary>名前の段の下端（測れなければ0＝出さない）。部品へ流す位置を決めるときにも使う。</summary>
    public double AnchorBottom => CompactHeader.AnchorBottom(_anchor, _viewer) ?? 0;

    /// <summary>今の流れの位置で、出すかを決め直す。</summary>
    public void Update()
    {
        IsShown = CompactHeader.Shows(_viewer.VerticalOffset, AnchorBottom);
        _bar.Visibility = IsShown ? Visibility.Visible : Visibility.Collapsed;

        // 右はスクロールバーの幅だけ空ける（バーを隠さない）。流す部品の左右の内側の余白は見える所の差に入るので引く
        var padding = _viewer.Padding;
        _bar.Margin = new Thickness(0, 0, Math.Max(0, _viewer.ActualWidth - _viewer.ViewportWidth - padding.Left - padding.Right), 0);

        // Tab や画面内検索で流すときは、帯の下に出す。流す部品の内側の余白（編集の左は上に20）は帯がもともと覆っているので、その分は引く
        _viewer.TopInset = IsShown ? Math.Max(0, CompactHeader.Height - _viewer.Padding.Top) : 0;
    }
}
