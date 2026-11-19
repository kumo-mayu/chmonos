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

    /// <summary>その商品の手元の物が全部つながっていないドライブの上か（最後に確かめた答え）。ショップなど、検索のカードを作らない画面のカードも同じ答えを使う。</summary>
    internal bool IsDetached(string itemId) => _detachedIds.Contains(itemId);

    /// <summary>
    /// 全商品を確かめ直し、変わったカードへ知らせる。読み直しの後と、ドライブをつなぎ直した後に呼ぶ。
    /// 続けて呼ばれても同時には1本だけ走らせる（下の remarks）
    /// </summary>
    /// <remarks>
    /// 確かめは同時に1本だけ。走っている間に呼ばれたら、終わった後にもう1回だけ回す
    /// （ドライブの抜き差しの知らせが続けて来ても、根を見る確かめを積み上げない）。
    /// </remarks>
    /// <param name="refreshRemap">ドライブ文字・通し番号の読み替えも読み直すか（つなぎ直した時。文字が変わっていることがある）。</param>
    internal void RefreshDetached(bool refreshRemap = false)
    {
        // 画面のスレッドで呼ぶ（フラグはそこでしか触らない）
        _detachedRemapWanted |= refreshRemap;
        if (_detachedRunning)
        {
            _detachedAgain = true;
            return;
        }

        _detachedRunning = true;
        var items = _allItems;
        var remap = _detachedRemapWanted;
        _detachedRemapWanted = false;
        var volumes = _services.Volumes;
        DetachedRuns++;
        Task.Run(() =>
        {
            if (remap)
            {
                volumes.RefreshRemap();
            }

            var probe = new FilePresenceProbe(volumes: volumes.Snapshot());
            return DetachedItems.Find(items, probe, volumes.CurrentOf, volumes.CurrentOf);
        }).ContinueWith(
            task => RunOnUiThread(() =>
            {
                _detachedRunning = false;
                if (!task.IsFaulted)
                {
                    ApplyDetached(task.Result);
                }

                if (_detachedAgain)
                {
                    _detachedAgain = false;
                    RefreshDetached();
                }
            }),
            TaskScheduler.Default);
    }

    private bool _detachedRunning;
    private bool _detachedAgain;
    private bool _detachedRemapWanted;

    /// <summary>確かめを始めた回数（試験用。続けて呼んでも積み上がらないことを見る）。</summary>
    internal int DetachedRuns { get; private set; }

    /// <summary>確かめ中でなく、待たせた分も無いか（試験用）。</summary>
    internal bool DetachedIdle => !_detachedRunning && !_detachedAgain;

    /// <summary>確かめた答えを入れ終えた（ショップなど、検索の外のカードの帯を付け直す所が聞く）。</summary>
    internal event EventHandler? DetachedApplied;

    /// <summary>確かめた答えをカードへ入れる。変わったカードだけ知らせる。</summary>
    internal void ApplyDetached(HashSet<string> ids)
    {
        _detachedIds = ids;
        foreach (var (id, card) in _cards)
        {
            card.IsDetached = ids.Contains(id);
        }

        DetachedApplied?.Invoke(this, EventArgs.Empty);
    }
}
