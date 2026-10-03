using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 商品ページ。画像・対応アバター・手元のファイルの欄は、編集画面と共有する部品
/// （<see cref="ItemGalleryPanel"/>・<see cref="ItemAvatarsPanel"/>・<see cref="ItemFilesPanel"/>）。
/// </summary>
public partial class ItemView : UserControl
{
    /// <summary>
    /// 戻る・進むで戻す先の位置。当て終えて、届いたら null。
    /// 当てた時点で流せる長さが足りなければ（右の列の行は後から届く）残しておき、伸びるたびに送り直す
    /// </summary>
    private double? _restoreTarget;

    /// <summary>最初の1回を当て終えたか（<see cref="ApplyRestore"/>）。ここから先は、伸びた知らせに乗せて送り直すだけ。</summary>
    private bool _restoreApplied;

    public ItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 人が触ったら、戻す先を追うのをやめる。触った後に中身が伸びて（見出しを開いた・ファイルを足した）、
        // 前の位置へ引き戻されると、見ている所が勝手に動く。時計で打ち切らないのは、読み込みの長さが商品で違うため
        PreviewMouseDown += (_, _) => StopChasing();
        PreviewMouseWheel += (_, _) => StopChasing();
        PreviewKeyDown += (_, _) => StopChasing();
    }

    /// <summary>中身が替わったときの、流した位置の扱い。</summary>
    internal enum ScrollOnOpen
    {
        /// <summary>今の位置のまま。</summary>
        Keep,

        /// <summary>先頭へ戻す。</summary>
        Top,

        /// <summary>画面の履歴が覚えた位置へ戻す（<see cref="ItemViewModel.TakeRestoreScrollOffset"/>）。</summary>
        Restore,
    }

    /// <summary>
    /// 同じ型の ViewModel に替わるとき、WPF は画面を作り直さず DataContext だけを替える。流した位置は画面が持つので、
    /// 何もしないと前の商品の位置のまま次の商品が出る（説明の途中や、右の列の下の方）。
    ///
    /// 主の窓（ユーザ判断 2026-09-30。ショップの画面と同じ決まり。<c>ItemViewModel.Scroll.cs</c>）：
    /// 進むときは先頭から、戻る・進むは離れたときの位置へ、同じ商品の開き直しは今の位置を保つ。
    /// フォルダビューと改変の画面に組み込んだ商品ページ（同じ日のユーザ判断）：別の商品を選び直したら先頭へ、同じ商品の開き直しは保つ。
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var previous = e.OldValue as ItemViewModel;
        var next = e.NewValue as ItemViewModel;

        // 前の商品へ戻している途中だった分は捨てる（次の商品へ引き継がない）
        CancelRestore();

        if (previous is not null)
        {
            previous.ScrollReader = null;
            previous.ChangeRevealRequested -= RevealChange;
            previous.ChangesClearing -= KeepPlaceWhileClearing;
        }

        if (next is not null)
        {
            next.ChangeRevealRequested += RevealChange;
            next.ChangesClearing += KeepPlaceWhileClearing;

            // 戻している途中で離れたら、着くはずだった位置を答える（まだ並んでいない・右の列が届いていない間に戻る・進むを続けて押したとき）
            next.ScrollReader = () => _restoreTarget ?? Body.VerticalOffset;
        }

        switch (ScrollFor(previous, next))
        {
            case ScrollOnOpen.Restore when next?.TakeRestoreScrollOffset() is { } offset and > 0:
                BeginRestore(offset);
                break;

            // 先頭で離れた商品へ戻ったときも、先頭へ
            case ScrollOnOpen.Restore:
            case ScrollOnOpen.Top:
                ReturnToTop();
                break;
        }
    }

    /// <summary>中身が替わったとき、流した位置をどうするか。</summary>
    internal static ScrollOnOpen ScrollFor(ItemViewModel? previous, ItemViewModel? next)
    {
        if (next is null)
        {
            return ScrollOnOpen.Keep;
        }

        if (next.IsEmbedded)
        {
            // 左の一覧で選び直すたびに、同じ画面へ次の商品が差し込まれる。履歴は無いので、同じ商品かどうかだけで決める
            return previous is not null && !string.Equals(previous.Item.Id, next.Item.Id, StringComparison.Ordinal)
                ? ScrollOnOpen.Top
                : ScrollOnOpen.Keep;
        }

        // 戻る・進むは、画面が作り直されていても戻す（間に別の種類の画面を挟むと、作りたての画面へ差し込まれる）
        if (next.ArrivedByHistory)
        {
            return ScrollOnOpen.Restore;
        }

        // 作りたての画面は先頭から始まるので、戻す物が無い。
        // 開き直しは商品の ID では見分けない——ID を変えた後の開き直しは、同じ商品でも ID が違う
        return previous is null || next.KeepsScrollPosition ? ScrollOnOpen.Keep : ScrollOnOpen.Top;
    }

    private void ReturnToTop()
    {
        // 先頭へ戻す命令は次の配置で効く。説明の見出しを後から足す側（ProgressiveItems）は中身が替わった時点で位置を読むので、
        // 「先頭へ戻る」と印で伝える（伝えないと、前の位置が残ると見て、見出しを全部その場で作る）。配置が済んだら外す
        Body.ScrollToHome();
        ProgressiveItems.SetReturnsToTop(Body, true);
        Body.LayoutUpdated += ClearReturnsToTop;
    }

    private void ClearReturnsToTop(object? sender, EventArgs e)
    {
        Body.LayoutUpdated -= ClearReturnsToTop;
        Body.ClearValue(ProgressiveItems.ReturnsToTopProperty);
    }

    // ---- 戻る・進むで来たときに、離れたときの位置へ戻す ----

    private void BeginRestore(double offset)
    {
        _restoreTarget = offset;
        _restoreApplied = false;

        // 位置は、次の商品の中身が並んでから当てる（今はまだ前の商品の中身。先に当てると、前の商品の長さで詰められる）。
        // 配置の回の終わりに当てると、描く前にもう一度配置が回るので、先頭の姿が1コマ出ることが無い
        Body.LayoutUpdated += ApplyRestore;
    }

    private void ApplyRestore(object? sender, EventArgs e)
    {
        if (_restoreTarget is not { } target)
        {
            Body.LayoutUpdated -= ApplyRestore;
            return;
        }

        // 作りたての画面（間に別の種類の画面を挟んで戻ったとき）は、最初の配置の時点では中身がまだ無い：
        // WPF は、作った時点で DataContext の無かった結び付けを、最初の配置の終わりにまとめて付ける。
        // 中身の無いまま当てると、その長さで手前に詰められる（測ると、流せる長さが 4,280 のはずの所で 489 だった）。
        // Body に結んだ値（Tag）が届いていれば、ほかの結び付けも届いている
        if (Body.ViewportHeight <= 0 || !ReferenceEquals(Body.Tag, DataContext))
        {
            return;
        }

        // **戻す先まで中身が在る必要がある。**説明の見出しは画面の外の分を後から足すので（ProgressiveItems）、ここで足し切る。
        // 見える分だけ足す形（戻す位置の周りだけ作る）にしなかったのは、上の行の高さが決まらないと位置が決まらないため
        // （作っていない行の高さは測れない）。戻る・進むで途中へ戻るときだけ、前と同じ「全部を1回で作る」手間が掛かる。
        // 足した分はまだ並んでいないので、次の配置の終わりにもう一度ここへ来る
        if (ProgressiveItems.FeedAllNow(Body))
        {
            return;
        }

        Body.LayoutUpdated -= ApplyRestore;
        _restoreApplied = true;
        Body.ScrollToVerticalOffset(target);

        if (Body.ScrollableHeight >= target)
        {
            _restoreTarget = null;
            return;
        }

        // 流せる長さが足りない。右の列の行（zip の中の Unity へ送れる物・この商品を使った改変）は裏で読んで後から届くので、
        // 説明の短い商品では、届く前の長さで手前に詰められる。伸びた知らせに乗せて送り直す（検索の一覧の位置と同じやり方）
        Body.ScrollChanged += ContinueRestore;
    }

    private void ContinueRestore(object sender, ScrollChangedEventArgs e)
    {
        // 説明の中の文字の箱や一覧も同じ知らせを上げる。ページを流す部品の分だけ見る
        if (!ReferenceEquals(e.OriginalSource, Body))
        {
            return;
        }

        if (_restoreTarget is not { } target)
        {
            Body.ScrollChanged -= ContinueRestore;
            return;
        }

        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            Body.ScrollToVerticalOffset(target);
            if (Body.ScrollableHeight >= target)
            {
                CancelRestore();
            }

            return;
        }

        // 伸びていないのに位置が動いた。こちらが送った分は届く端で止まっているはずなので、違う所にいれば人か別の処理（画面内検索）が流した
        if (e.VerticalChange != 0 && Math.Abs(e.VerticalOffset - Math.Min(target, Body.ScrollableHeight)) > 1)
        {
            CancelRestore();
        }
    }

    /// <summary>人が触った。当て終えた後なら、届いていなくても追うのをやめる（当てる前は、同じコマの中なので触れない）。</summary>
    private void StopChasing()
    {
        if (_restoreApplied)
        {
            CancelRestore();
        }
    }

    // ---- BOOTH で変わった所（メモ17） ----

    /// <summary>上の帯の「ほか n 件」：入り切らなかった分も含めて、変わった所を全部並べる。</summary>
    private void OnMoreChangesClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>
    /// 変わった所まで流す。畳んだ欄は ViewModel が開いてあるので、**並べ直してから位置を測る**
    /// （開く前・並ぶ前に測ると、開いて伸びた分だけ行き先がずれる。ユーザ指示 2026-10-03）。
    /// 説明の見出しは画面の外の分を後から足すので（ProgressiveItems）、行き先が在るように先に足し切る
    /// </summary>
    private void RevealChange(ChangeTarget target)
    {
        CancelRestore();
        ProgressiveItems.FeedAllNow(Body);
        Body.UpdateLayout();

        // 開いた欄の中に、足している途中の一覧ができていることがある（畳んでいた説明の見出し）
        if (ProgressiveItems.FeedAllNow(Body))
        {
            Body.UpdateLayout();
        }

        FrameworkElement? element = target.Place switch
        {
            ChangePlace.Name => NameAnchor,
            ChangePlace.Variations => VariationsAnchor,
            ChangePlace.Gallery => GalleryAnchor,
            ChangePlace.Sale => SaleAnchor,
            ChangePlace.Description => DescriptionAnchor,
            ChangePlace.Section when target.Section is { } section => SectionList.ItemContainerGenerator.ContainerFromItem(section) as FrameworkElement,
            _ => null,
        };

        if (element is null || !element.IsVisible)
        {
            return;
        }

        // 行き先の少し上（見出しの帯の線が上端に貼り付かないように）
        var top = element.TransformToAncestor(Body).Transform(new Point(0, 0)).Y + Body.VerticalOffset;
        Body.ScrollToVerticalOffset(Math.Max(0, top - 12));
    }

    /// <summary>既読にする前に測った、見ている所より上で消える帯の高さ。</summary>
    private double _clearShift;

    /// <summary>
    /// 既読にして帯を外すと、消えた行・消えた見出し・消えたバリエーションが無くなり、その分だけ下の物が上へ詰まる。
    /// 見ている所より上で消える分だけ流した位置を戻し、**見ている所を動かさない**（ユーザ指示 2026-10-03）。
    /// 測るのは左の列（説明文）：消えた物の大半はここにあり、左右の列は高さが別々なので、両方を同時に保つことはできない
    /// </summary>
    private void KeepPlaceWhileClearing()
    {
        _clearShift = RemovedHeightAbove(LeftColumn);
        if (_clearShift > 0)
        {
            // 外した後の配置の回の終わりに当てる（描く前にもう一度配置が回るので、詰まった姿が1コマ出ることが無い）
            Body.LayoutUpdated += ApplyClearShift;
        }
    }

    private void ApplyClearShift(object? sender, EventArgs e)
    {
        Body.LayoutUpdated -= ApplyClearShift;
        Body.ScrollToVerticalOffset(Math.Max(0, Body.VerticalOffset - _clearShift));
        _clearShift = 0;
    }

    /// <summary>
    /// <paramref name="scope"/> の中で、消える帯（<see cref="SelectableText.RemovedLineTag"/> の付いた部品と本文の段落）のうち、
    /// 見えている所の上端より上にある高さの合計。
    /// </summary>
    private double RemovedHeightAbove(DependencyObject scope)
    {
        var total = 0.0;
        void Add(double top, double bottom)
        {
            if (top < 0)
            {
                total += Math.Min(bottom, 0) - top;
            }
        }

        void Visit(DependencyObject node)
        {
            if (node is FrameworkElement { IsVisible: false })
            {
                return;
            }

            if (node is FrameworkElement element && ReferenceEquals(element.Tag, SelectableText.RemovedLineTag))
            {
                var top = element.TransformToAncestor(Body).Transform(new Point(0, 0)).Y;
                Add(top, top + element.ActualHeight);
                return;
            }

            if (node is RichTextBox box)
            {
                var origin = box.TransformToAncestor(Body).Transform(new Point(0, 0)).Y;
                foreach (var paragraph in box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                             .Where(paragraph => ReferenceEquals(paragraph.Tag, SelectableText.RemovedLineTag)))
                {
                    var first = paragraph.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                    var last = paragraph.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                    Add(origin + first.Top, origin + last.Bottom);
                }

                return;
            }

            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); index++)
            {
                Visit(System.Windows.Media.VisualTreeHelper.GetChild(node, index));
            }
        }

        Visit(scope);
        return total;
    }

    private void CancelRestore()
    {
        _restoreTarget = null;
        _restoreApplied = false;
        Body.LayoutUpdated -= ApplyRestore;
        Body.ScrollChanged -= ContinueRestore;
    }
}
