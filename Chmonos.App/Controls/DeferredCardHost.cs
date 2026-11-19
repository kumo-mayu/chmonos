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

    /// <summary>中身を作って受け持っている商品。外されたときの目印に使う（そのときにはもう DataContext が切れている）。</summary>
    private object? _realizedItem;

    /// <summary>
    /// 一度中身を作ったことのある商品（カードの ViewModel）。入れ物が作り直されたとき、後回しにせずその場で作る目印。
    /// 弱い参照の表なので、一覧から消えたカードを持ち続けない
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> RealizedBefore = new();

    public DeferredCardHost()
    {
        Focusable = false;
        IsTabStop = false;

        // 画面を移って戻ってきたときも、まだ作っていなければ並び直す。控えている間（見えている所から遠い行。ViewportHold）は並ばない
        Loaded += (_, _) =>
        {
            if (!_realized && !ViewportHold.GetIsHeld(this))
            {
                // 前に一度作った商品のカードで、ほかに作る順番を待つカードも無ければ、その場で作る（2026-11-10）。
                // 上へ1段流すたびに、WPF の一覧が一番下の行の入れ物を作り直していた。後回しにすると、次に画面のスレッドが
                // 空くまでの1コマだけ中身の無い枠が見え、一番下のカードが点滅した（ユーザの報告。ホイールで4回撮って3回出た）。
                // 1枚なら約2ms。初めて見る行がまとめて入るとき（速く流している）は今どおり4枚ずつ後回しにする
                //
                // 同じ画面の仕事の中で、いま外された入れ物と同じ商品なら、4枚の枠と「ほかに待つカードが無い」の条件を当てずに作る（2026-11-19）。
                // 一番下の行が丸ごと作り直されるのは、見えていたカードの入れ替えでしかない。行が5枚以上（広い窓・小さいカード）だと、
                // 4枚の枠を使い切った5枚目から順番待ちに回り、末尾の行の右のカードが流すたびに空の枠になっていた（足跡：5列で、1段ごとに5枚目が順番待ち）。
                // 外された数より多くは作らないので、1回の量は見えていた行の枚数まで（1枚約2ms・8列で約16ms）。前に見た行を速く戻すときに
                // 入ってくる行は、外したのとは別の仕事で入るので、今どおり4枚までにする（5列・401件で速く戻して、1回にその場で作ったのは最大8枚）
                if (DataContext is { } item && RealizedBefore.TryGetValue(item, out _)
                    && (CardBuildQueue.TakeJustTornDown(item)
                        || (CardBuildQueue.IsIdle && CardBuildQueue.TryTakeImmediateSlot(Dispatcher))))
                {
                    Realize();
                    return;
                }

                CardBuildQueue.Enqueue(this);
            }
        };

        // 作ったカードの入れ物が外されたら、その商品を「いま外された」として覚える。外れた時点の DataContext はもう切れている
        // （WPF の一覧が入れ物を外すと、中の DataContext は「切れた項目」に変わる。足跡で確かめた）ので、作ったときの商品を使う
        Unloaded += (_, _) =>
        {
            if (_realized && _realizedItem is { } item)
            {
                CardBuildQueue.NoteTornDown(item, Dispatcher);
            }
        };

        // 作った後に別のカードを受け持つことになったら、中身も差し替える
        DataContextChanged += (_, e) =>
        {
            if (_realized)
            {
                Content = e.NewValue;

                // 外されるときに来る「切れた項目」では覚え直さない（外された印に使うのは、それまで受け持っていた商品）
                if (e.NewValue is { } next && next != System.Windows.Data.BindingOperations.DisconnectedSource)
                {
                    _realizedItem = next;
                }
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
            _realizedItem = null;
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
        if (DataContext is { } item)
        {
            RealizedBefore.AddOrUpdate(item, RealizedBefore);
            _realizedItem = item;
        }

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

    /// <summary>作る順番を待つカードが無い（まとめて入ってくる最中ではない）。</summary>
    public static bool IsIdle => Pending.Count == 0 && !_scheduled;

    private static int _immediateThisTurn;
    private static bool _immediateResetScheduled;

    /// <summary>
    /// 後回しにせずその場で作ってよいか。1回の画面の仕事（次に画面のスレッドが空くまで）で <see cref="CardsPerPass"/> 枚まで。
    /// 前に見た行を速く戻すと、作り直す入れ物がまとめて入る。全部その場で作ると、後回しにした理由の固まり（1回で100〜200ms）が戻るので、
    /// 後回しの1回分と同じ量（約8ms）を超えた分は今どおり順番待ちに回す
    /// </summary>
    public static bool TryTakeImmediateSlot(Dispatcher dispatcher)
    {
        if (_immediateThisTurn >= CardsPerPass)
        {
            return false;
        }

        _immediateThisTurn++;
        if (!_immediateResetScheduled)
        {
            _immediateResetScheduled = true;
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _immediateThisTurn = 0;
                _immediateResetScheduled = false;
            });
        }

        return true;
    }

    /// <summary>
    /// この画面の仕事の中で外された、作ってあったカードの商品。次に画面のスレッドが空いたら忘れる。
    /// 同じ物（参照）で引くので、別の商品が同じ値に見えても混ざらない
    /// </summary>
    private static readonly HashSet<object> JustTornDown = new(ReferenceEqualityComparer.Instance);
    private static bool _tornDownResetScheduled;

    /// <summary>作ってあったカードの入れ物が外された。同じ仕事の中で同じ商品の入れ物が入ってきたら、待たせずに作る。</summary>
    public static void NoteTornDown(object item, Dispatcher dispatcher)
    {
        JustTornDown.Add(item);
        if (!_tornDownResetScheduled)
        {
            _tornDownResetScheduled = true;

            // 入れ物の外しと入れ直しは、同じ並べ直しの後に続けて知らされる（足跡で、外れた2〜3ms後に入っていた）。
            // 順番待ちと同じ Background で忘れるので、後の仕事で入ってきた（速く戻した）行には効かない
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                JustTornDown.Clear();
                _tornDownResetScheduled = false;
            });
        }
    }

    /// <summary>この仕事の中で外された商品なら真を返し、印を消す（1回の外しにつき1枚だけ作る）。</summary>
    public static bool TakeJustTornDown(object item) => JustTornDown.Remove(item);

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
