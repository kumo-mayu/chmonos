using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 仮想化しない一覧に、**画面に見えている分までは最初の配置で、画面の外の分は後から1行ずつ**足す添付プロパティ。
/// <c>ItemsSource</c> の代わりに <c>ProgressiveItems.Source</c> へ結ぶ。
///
/// 商品の説明は見出しごとに1行で、1行に選べる文字の箱（<see cref="RichTextBox"/>）が1つある。
/// 見出し21個の商品では、全部を1回で作ると画面のスレッドが約280ms 止まり、商品ページを開くのに約340ms かかっていた
/// （短い商品は約60ms。<c>docs/research/item-page-open-2026-09-30.md</c>）。止まりのほとんどは、開いたときには見えていない行の分。
///
/// 守ること（形を5つ試して決めた。同じ記録の「B1」）：
/// <list type="bullet">
/// <item>**最初に出るコマは、全部を1回で作ったときと同じ。**見えている行を後へ回すと、行が1つずつ増えるたびに下の欄が押されて跳ねた</item>
/// <item>**見えている物を動かさない。**足すのは一覧の末尾だけで、先頭から流していないときにだけ分ける（下の「分けないとき」）</item>
/// <item>**全部が在ることを当てにする処理は、先に <see cref="FeedAllNow"/> を呼ぶ**（画面内検索）。呼ばないと、まだ無い行を探せない</item>
/// </list>
///
/// 分けないとき（その場で全部足す。前と同じ動き）：
/// <list type="bullet">
/// <item>見えていない一覧（畳んだ欄の中）。並べないので手間が掛からず、開いたときに分ける理由も無い</item>
/// <item>流せる画面の中に無い一覧。どこまでが見えているかを測れない</item>
/// <item>使い回された画面で、前の中身の流した位置が残るとき（同じ商品の開き直し）。一覧を短く始めると、流せる長さが足りずに位置が手前へ詰められる</item>
/// </list>
///
/// 別の商品へ進むときは、画面が先頭へ戻すと印を付ける（<see cref="ReturnsToTopProperty"/>）ので分けて足す。
/// 戻る・進むで途中の位置へ戻すときは、戻す先まで中身が要るので、画面の側が <see cref="FeedAllNow"/> で足し切ってから位置を当てる（<c>ItemView</c>）
/// </summary>
public static class ProgressiveItems
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(IEnumerable),
            typeof(ProgressiveItems),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// ページを流す部品に付ける印：次の配置で先頭へ戻る（<c>ScrollToHome</c> を頼んである）。
    /// 先頭へ戻す命令は次の配置まで効かないので、中身が替わった時点ではまだ前の位置が読める。
    /// 印が無いと「前の位置が残る」と見て分けずに全部足してしまう。付けた側が、配置が済んだら外す
    /// </summary>
    public static readonly DependencyProperty ReturnsToTopProperty =
        DependencyProperty.RegisterAttached(
            "ReturnsToTop",
            typeof(bool),
            typeof(ProgressiveItems),
            new PropertyMetadata(false));

    public static void SetSource(DependencyObject element, IEnumerable? value) => element.SetValue(SourceProperty, value);

    public static IEnumerable? GetSource(DependencyObject element) => (IEnumerable?)element.GetValue(SourceProperty);

    public static void SetReturnsToTop(DependencyObject element, bool value) => element.SetValue(ReturnsToTopProperty, value);

    public static bool GetReturnsToTop(DependencyObject element) => (bool)element.GetValue(ReturnsToTopProperty);

    /// <summary>
    /// 先読みの量（px）。少し流しただけで空きが見えないように、見える範囲の下にも足しておく。
    /// 300 は本文の十数行ぶん（1行 21px）で、ホイール2〜3目盛りの送り（1目盛り 48px×3行ほど）に当たる。
    /// 残りは1行 十数ms で足されるので、流し始めてから空きに追い付かれるまでに、この分の余裕がある
    /// </summary>
    internal const double LookAhead = 300;

    /// <summary>足している途中の一覧。全部が要る所が、待たずに足し切るために持つ（済んだら外すので、画面を握り続けない）。</summary>
    private static readonly List<Feeder> Active = [];

    private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsControl list)
        {
            return;
        }

        // 前の分が足している途中なら止める（元が差し替わった）
        Active.RemoveAll(feeder => ReferenceEquals(feeder.List, list));

        var target = new ObservableCollection<object>();
        if (args.NewValue is not IEnumerable source)
        {
            list.ItemsSource = target;
            return;
        }

        var feeder = new Feeder(list, target, new Queue<object>(source.Cast<object>()));

        // 使い回された画面で、前の中身の流した位置が残るとき。分けると、最初の配置では一覧が空で流せる長さが足りず、
        // 位置が手前へ詰められる（詰められた位置は、行を足しても戻らない）。前と同じく全部をその場で足す
        if (FindScroller(list) is { VerticalOffset: > 0 } scroller && !GetReturnsToTop(scroller))
        {
            Drain(feeder);
            list.ItemsSource = target;
            return;
        }

        list.ItemsSource = target;
        if (feeder.Pending.Count == 0)
        {
            return;
        }

        Active.Add(feeder);

        // LayoutUpdated は配置の回の終わりに来る。そこで1行足すと、描く前にもう一度配置が回るので、同じ1コマに入る。
        // 一覧の下端が見える範囲（と先読みの分）を越えたら、残りは Background の段で1行ずつ足す
        EventHandler? onLayout = null;
        onLayout = (_, _) =>
        {
            if (!Active.Contains(feeder))
            {
                list.LayoutUpdated -= onLayout;
                return;
            }

            var scroller = feeder.Scroller ??= FindScroller(list);
            if (!list.IsVisible || scroller is not { ViewportHeight: > 0, Content: UIElement content })
            {
                list.LayoutUpdated -= onLayout;
                Finish(feeder);
                return;
            }

            if (NeedsMore(feeder, scroller, content))
            {
                feeder.Target.Add(feeder.Pending.Dequeue());
                if (feeder.Pending.Count > 0)
                {
                    return;
                }
            }

            list.LayoutUpdated -= onLayout;
            if (feeder.Pending.Count > 0)
            {
                list.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Feed(feeder));
            }
            else
            {
                Active.Remove(feeder);
            }
        };
        list.LayoutUpdated += onLayout;
    }

    /// <summary>
    /// 一覧の下端が、先頭から見える範囲（＋先読み）に届いていないか。
    ///
    /// **今の流した位置ではなく、先頭からの距離で測る。**分けて足すのは先頭から始まるときだけだが、
    /// 「先頭へ戻る」と印の付いた画面では、測る時点でまだ前の位置のままのことがある：配置の終わりの知らせは、
    /// 誰かが並べ直しを頼むと残りの相手へ配られず、こちらが1行足すたびに、流す部品が先頭へ戻す番が後へ回る
    /// （今の位置で測ると、前の位置の下まで行を作ってしまう。試験で、8行のはずが32行になった）。
    /// 流す部品の中身の上端からの距離なら、流した位置がどうなっていても変わらない
    /// </summary>
    private static bool NeedsMore(Feeder feeder, ScrollViewer scroller, UIElement content)
    {
        var bottom = feeder.List.TranslatePoint(new Point(0, feeder.List.ActualHeight), content).Y;
        return bottom < scroller.ViewportHeight + LookAhead;
    }

    private static ScrollViewer? FindScroller(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            // 部品の型の中の流す部品（一覧や箱が自分で持つ物）ではなく、画面に置いた「ページを流す部品」を探す
            if (node is ScrollViewer { TemplatedParent: null } scroller)
            {
                return scroller;
            }
        }

        return null;
    }

    /// <summary>
    /// 1行足して、続きを頼む。Background の段は入力と描画より後なので、間に流す操作とコマが通り、1回の止まりは1行ぶんで済む。
    /// まとめて足すと、止まりが2つに分かれるだけで、2つ目の間は流せなかった（約270ms）
    /// </summary>
    private static void Feed(Feeder feeder)
    {
        if (!Active.Contains(feeder))
        {
            return;
        }

        // 画面から外れた（別の画面へ移った）。見る人がいないので、分ける意味が無い。載っていない一覧は並べないので、全部足しても手間は掛からない
        if (!feeder.List.IsLoaded)
        {
            Finish(feeder);
            return;
        }

        feeder.Target.Add(feeder.Pending.Dequeue());
        if (feeder.Pending.Count > 0)
        {
            feeder.List.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Feed(feeder));
            return;
        }

        Active.Remove(feeder);
    }

    private static void Finish(Feeder feeder)
    {
        Drain(feeder);
        Active.Remove(feeder);
    }

    private static void Drain(Feeder feeder)
    {
        while (feeder.Pending.Count > 0)
        {
            feeder.Target.Add(feeder.Pending.Dequeue());
        }
    }

    /// <summary>
    /// 足している途中の一覧を、今ここで全部足す。<paramref name="scope"/> を渡すと、その中の一覧だけ。
    /// 足しただけでは並んでいないので、部品の位置や文字を読む前に、呼んだ側が <c>UpdateLayout</c> する。
    /// 足した物があれば true
    /// </summary>
    public static bool FeedAllNow(DependencyObject? scope = null)
    {
        var fed = false;
        foreach (var feeder in Active.ToList())
        {
            if (scope is Visual visual && !ReferenceEquals(visual, feeder.List) && !visual.IsAncestorOf(feeder.List))
            {
                continue;
            }

            Finish(feeder);
            fed = true;
        }

        return fed;
    }

    /// <summary>まだ足していない行の数（試験が読む）。</summary>
    internal static int PendingCount(ItemsControl list)
        => Active.FirstOrDefault(feeder => ReferenceEquals(feeder.List, list))?.Pending.Count ?? 0;

    private sealed class Feeder(ItemsControl list, ObservableCollection<object> target, Queue<object> pending)
    {
        public ItemsControl List { get; } = list;

        public ObservableCollection<object> Target { get; } = target;

        public Queue<object> Pending { get; } = pending;

        /// <summary>ページを流す部品（最初に探して控える）。</summary>
        public ScrollViewer? Scroller { get; set; }
    }
}
