using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 検索のカード1枚の入れ物。中身は、画面のスレッドが空いたときに少しずつ作る（U12・4回目・ユーザ判断 2026-09-12）。
///
/// WPFの一覧は、流して新しく入った行（画面外に先に作っておく上下1画面ぶんを含む）のカードを
/// 1回の処理でまとめて作る。カードは部品が多く、まとめて作ると1回で100〜200ms画面が固まっていた。
/// 2000件を端まで流して測ると、33ms（2コマ）を超える固まりの合計が約20秒 → 4枚ずつ作ると0.3秒になった。
/// 代わりに、速く流している間は中身の無い白い枠が一瞬見える（ユーザ判断：カクつくよりはその方がよい）。
/// </summary>
public sealed class DeferredCardHost : ContentControl
{
    public static readonly DependencyProperty RealTemplateProperty = DependencyProperty.Register(
        nameof(RealTemplate), typeof(DataTemplate), typeof(DeferredCardHost));

    /// <summary>作るときに当てるカードの見た目。</summary>
    public DataTemplate? RealTemplate
    {
        get => (DataTemplate?)GetValue(RealTemplateProperty);
        set => SetValue(RealTemplateProperty, value);
    }

    private bool _realized;

    internal bool IsRealized => _realized;

    public DeferredCardHost()
    {
        Focusable = false;
        IsTabStop = false;

        // 画面を移って戻ってきたときも、まだ作っていなければ並び直す。控えている間（見えている所から遠い行。ViewportHold）は並ばない
        Loaded += (_, _) =>
        {
            if (!_realized && !ViewportHold.GetIsHeld(this))
            {
                CardBuildQueue.Enqueue(this);
            }
        };

        // 作った後に別のカードを受け持つことになったら、中身も差し替える
        DataContextChanged += (_, e) =>
        {
            if (_realized)
            {
                Content = e.NewValue;
            }
        };
    }

    /// <summary>
    /// 中のまだ作っていないカードを、今すぐ作る。作った物があれば真（呼んだ側が配置を済ませる）。
    /// キーボードで隣の段へ移るとき（<see cref="ArrowGroup"/>）に使う。流して入ったばかりの段は白い枠だけで、止まる物が無い
    /// </summary>
    public static bool RealizeWithin(DependencyObject parent)
    {
        var made = false;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is DeferredCardHost host)
            {
                if (!host._realized)
                {
                    host.Realize();
                    made = true;
                }

                continue;
            }

            made |= RealizeWithin(child);
        }

        return made;
    }

    /// <summary>
    /// 控えの印（<see cref="ViewportHold"/>）が変わった。外れたら作る順番に並び、付いたら中身を捨てて白い枠だけに戻す
    /// （仮想化した一覧が、流れ去った行の入れ物を使い回すのと同じ量に保つ）。キーボードで止まっているカードは捨てない
    /// </summary>
    internal void OnHeldChanged(bool held)
    {
        if (!held)
        {
            if (!_realized && IsLoaded)
            {
                CardBuildQueue.Enqueue(this);
            }

            return;
        }

        if (_realized && !IsKeyboardFocusWithin)
        {
            _realized = false;
            Content = null;
            ContentTemplate = null;
        }
    }

    internal void Realize()
    {
        if (_realized)
        {
            return;
        }

        _realized = true;

        // 中身を渡すのは作るときだけ。先に渡すと、見た目が当たるまでの間に型の名前を出す文字の部品が作られて見えてしまう
        ContentTemplate = RealTemplate;
        Content = DataContext;
    }
}

/// <summary>カードの中身を作る順番待ち。1回に <see cref="CardsPerPass"/> 枚作ってレイアウトまで済ませ、画面のスレッドを返す。</summary>
internal static class CardBuildQueue
{
    /// <summary>
    /// 1回に作る枚数。1枚あたり約2msなので、4枚で1回平均約8ms（最長でも約36ms）に収まる。今の列数（8列）でちょうど半行。
    /// 1枚ずつでも固まりはほとんど変わらなかったが、全部作り終わるまでの待ちが少し延びた（最長 約240ms、4枚ずつは約190ms）
    /// </summary>
    private const int CardsPerPass = 4;

    private static readonly Queue<DeferredCardHost> Pending = new();
    private static bool _scheduled;

    public static void Enqueue(DeferredCardHost host)
    {
        Pending.Enqueue(host);
        Schedule(host.Dispatcher);
    }

    private static void Schedule(Dispatcher dispatcher)
    {
        if (!_scheduled)
        {
            _scheduled = true;

            // 入力と描画を先に通す。流している最中の操作を待たせない
            dispatcher.BeginInvoke(DispatcherPriority.Background, Pump);
        }
    }

    private static void Pump()
    {
        _scheduled = false;
        var made = new List<DeferredCardHost>(CardsPerPass);

        while (made.Count < CardsPerPass && Pending.Count > 0)
        {
            var host = Pending.Dequeue();

            // 作る前に流れて外れた枠・控えに戻った枠は作らない
            if (host.IsRealized || !host.IsLoaded || ViewportHold.GetIsHeld(host))
            {
                continue;
            }

            host.Realize();
            made.Add(host);
        }

        // 見た目を当てただけでは、中身は次のレイアウトでまとめて作られ、分けた意味が無くなる。
        // 作った分のレイアウトまでこの1回で済ませる
        if (made.Count > 0)
        {
            made[0].UpdateLayout();
        }

        // カードの一覧は Tab で1回だけ入る並び（ArrowGroup）。後からできたカードを、並びの止まらない物にする
        foreach (var host in made)
        {
            ArrowGroup.Adopt(host);
        }

        if (Pending.Count > 0)
        {
            Schedule(Dispatcher.CurrentDispatcher);
        }
    }
}
