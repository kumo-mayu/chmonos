using System.Windows.Threading;
using Chmonos.Api;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Services;

/// <summary>
/// 複数選択の変化を、短い間まとめてからソフトへ知らせる（plan 44 の①・docs/spec/plugins.md）。
/// 選ぶ操作は続けて起きる（Ctrl+クリックを重ねる・Shift で範囲を伸ばす）ので、1回ごとに流すとソフトが途中の選択を何度も受ける。
/// 誰も購読していなければ印も立てない。ID を数えるのは、まとめの時間が切れたときだけ（2万件を選んでも、変化のたびには数えない）
/// </summary>
internal sealed class PluginSelectionBatcher(PluginEventHub hub)
{
    /// <summary>
    /// まとめる間。人が続けて選ぶ間隔（クリックとクリックの間）はおおむね数百ms より短く、ソフトの側から見て遅れが気にならない長さ。
    /// API の見張りの知らせをまとめる 300ms と同じにそろえた
    /// </summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private string? _screen;
    private Func<IReadOnlyList<string>>? _read;
    private DispatcherTimer? _timer;

    /// <summary>前に流した選択（同じなら流さない：帯の知らせは選択が変わらなくても来ることがある）。</summary>
    private (string Screen, IReadOnlyList<string> Ids)? _last;

    /// <summary>まだ流していない変化があるか（試験）。</summary>
    internal bool IsPending => _read is not null;

    /// <summary>
    /// 選択が変わった（画面のスレッドで呼ぶ）。<paramref name="read"/> は流すときに今の選択の ID を読む。
    /// 別の画面の変化が来たら、前の画面の分を先に流す（画面ごとの選択を混ぜない）
    /// </summary>
    public void Note(string screen, Func<IReadOnlyList<string>> read)
    {
        if (!hub.Watches(PluginEventHub.SelectionChanged))
        {
            return;
        }

        if (_screen is not null && _screen != screen)
        {
            Flush();
        }

        _screen = screen;
        _read = read;
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
            _timer.Tick += (_, _) => Flush();
        }

        // 変化のたびに数え直す（最後の変化から Delay 経ったら流す）
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>溜めた変化を今流す（時間切れ。試験は時計を使わずにここを呼ぶ）。</summary>
    internal void Flush()
    {
        _timer?.Stop();
        if (_read is not { } read || _screen is not { } screen)
        {
            return;
        }

        _read = null;
        _screen = null;
        var ids = read();
        if (_last is { } last && last.Screen == screen && last.Ids.SequenceEqual(ids, StringComparer.Ordinal))
        {
            return;
        }

        _last = (screen, ids);
        hub.PublishSelection(screen, ids);
    }

    /// <summary>
    /// 選んだ商品の ID。表示中の並びを先に、絞り込みで見えなくなった物を後に（帯の操作と同じ並び）。
    /// 帯の操作の <c>SelectedCards</c> は「もう入っているか」をリストで探すので、2万件では約4億回になる。ここは集合で見分ける
    /// </summary>
    public static IReadOnlyList<string> SelectedIds(IEnumerable<ItemCardViewModel> shown, IEnumerable<ItemCardViewModel> all)
    {
        var seen = new HashSet<ItemCardViewModel>(ReferenceEqualityComparer.Instance);
        var ids = new List<string>();
        foreach (var card in shown.Concat(all))
        {
            if (card.IsSelected && seen.Add(card))
            {
                ids.Add(card.Item.Id);
            }
        }

        return ids;
    }
}
