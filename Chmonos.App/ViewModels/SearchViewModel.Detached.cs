using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索画面：カードの帯「取り外し中」（手元の物が全部、今つながっていないドライブの上の商品。ユーザ判断 2026-11-20）。
/// ドライブの根を見るので、カードを作る流れの中ではやらず、作った後に裏で確かめて入れる
/// （落ちた共有は根を見るだけで数秒待つことがあり、検索の読み込みを止めたくない）。
/// </summary>
public sealed partial class SearchViewModel
{
    private HashSet<string> _detachedIds = new(StringComparer.Ordinal);
    private int _detachedRun;

    /// <summary>その商品の手元の物が全部つながっていないドライブの上か（最後に確かめた答え）。ショップなど、検索のカードを作らない画面のカードも同じ答えを使う。</summary>
    internal bool IsDetached(string itemId) => _detachedIds.Contains(itemId);

    /// <summary>
    /// 全商品を確かめ直し、変わったカードへ知らせる。読み直しの後と、ドライブをつなぎ直した後に呼ぶ。
    /// 続けて呼ばれたら、古い方の答えは捨てる（新しい呼びだけが書く）
    /// </summary>
    internal void RefreshDetached()
    {
        var items = _allItems;
        var run = Interlocked.Increment(ref _detachedRun);
        var volumes = _services.Volumes;
        Task.Run(() =>
        {
            var probe = new FilePresenceProbe(volumes: volumes.Snapshot());
            return DetachedItems.Find(items, probe, volumes.CurrentOf, volumes.CurrentOf);
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted || run != Volatile.Read(ref _detachedRun))
                {
                    return;
                }

                RunOnUiThread(() => ApplyDetached(task.Result));
            },
            TaskScheduler.Default);
    }

    /// <summary>確かめた答えをカードへ入れる。変わったカードだけ知らせる。</summary>
    internal void ApplyDetached(HashSet<string> ids)
    {
        _detachedIds = ids;
        foreach (var (id, card) in _cards)
        {
            card.IsDetached = ids.Contains(id);
        }
    }
}
