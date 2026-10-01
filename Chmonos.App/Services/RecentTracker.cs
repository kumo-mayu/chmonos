using System.IO;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// 「最近」の足跡を打つ。
///
/// **足跡を打つのに人を待たせない。**閲覧は商品を開くたびに走るので、
/// 書き込みで画面が止まらないようにする（失敗しても黙って諦める——
/// 足跡が1つ欠けても商品の記録は無事）。
///
/// itemのJSONではなく <c>recent.json</c> に集めている理由は
/// <see cref="RecentActivity"/> に書いてある。
/// </summary>
public sealed class RecentTracker
{
    private readonly DataStore _store;
    private readonly BackgroundWriteQueue _writes;

    public RecentTracker(DataStore store, BackgroundWriteQueue writes)
    {
        _store = store;
        _writes = writes;
    }

    public Task TouchAsync(string itemId, RecentKind kind)
        => TouchAsync(itemId, kind, DateTimeOffset.Now);

    /// <summary>
    /// 足跡を1つ打つ。**書くのは画面のスレッドの外**（<see cref="BackgroundWriteQueue"/>）。
    ///
    /// 前はここで直に書いていた。非同期の形でも、錠が空いていれば読む・書く・ディスクへ書き出す・置き換えるが
    /// 呼んだスレッドで走るので、商品ページを開くたびに画面のスレッドが 8〜10ms 止まっていた（2026-09-30 に SSD で測った）。
    /// 時刻は呼ばれた時点で決め、列は頼まれた順に書くので、続けて開いても古い時刻が後から勝つことは無い。
    /// </summary>
    public Task TouchAsync(string itemId, RecentKind kind, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Task.CompletedTask;
        }

        return _writes.RunAsync(() => WriteTouchAsync(itemId, kind, at));
    }

    private async Task WriteTouchAsync(string itemId, RecentKind kind, DateTimeOffset at)
    {
        try
        {
            // 保存先を運んでいる間は待つ。足跡は `UiCommand` を通らないので、門はここで通す
            // （コピー済みへ書いた分は、元を消すときに失われる）
            await Core.Storage.StoreWriteGate.WaitAsync();

            // 錠は窓口（`JsonFileStore.UpdateAsync`）に持たせる。ここだけに錠を置いていたので、
            // 取り込み側（別経路で同じ recent.json を書く）とは重なり、片方の足跡が消えていた
            await _store.Recent.UpdateAsync(
                log => new RecentLog { Entries = RecentActivity.Touch(log.Entries, itemId, kind, at) });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 足跡が1つ欠けても商品の記録は無事。ここで止める理由が無い
        }
    }

    /// <summary>
    /// 手元に無くなった商品の行を落とす。起動時に裏で1回（ユーザ判断 2026-09-29）。
    ///
    /// 落とす関数（<see cref="RecentActivity.KeepOnly"/>）はあったが、どこからも呼ばれておらず、
    /// 消した商品・IDを変えた元の商品の行が残り続けていた（1商品1行なので、消すほど少しずつ溜まる）。
    /// **在るかは錠の中で1行ずつ見る**：先に商品の一覧を読んでから落とすと、その間に取り込みが作った商品の行を落とし得る。
    /// 行は商品を保存した後にしか書かれないので、錠の中で在るかを見れば取り違えない。落とす物が無ければ書かない。
    /// 通信しないので、裏の取得を設定で切っていても行う
    /// </summary>
    /// <returns>落とした行の数。</returns>
    public async Task<int> PruneMissingItemsAsync()
    {
        await Core.Storage.StoreWriteGate.WaitAsync();

        var dropped = 0;
        await _store.Recent.TryUpdateAsync(log =>
        {
            var present = log.Entries
                .Select(entry => entry.ItemId)
                .Distinct(StringComparer.Ordinal)
                .Where(_store.Items.Exists)
                .ToHashSet(StringComparer.Ordinal);
            var kept = RecentActivity.KeepOnly(log.Entries, present);
            dropped = log.Entries.Count - kept.Count;
            return dropped == 0 ? null : new RecentLog { Entries = kept };
        });
        return dropped;
    }

    /// <summary>その種類の時刻を商品IDから引ける形で返す。並べ替えのために読む。</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> Times(RecentKind kind)
    {
        var all = AllTimes();
        return kind switch
        {
            RecentKind.Added => all.Added,
            RecentKind.Used => all.Used,
            _ => all.Viewed,
        };
    }

    /// <summary>
    /// 3種類の時刻を、足跡を1回読んで分ける。
    ///
    /// 「最近」の条件で絞るたびに、種類ごとに <c>recent.json</c> を読み直していた（1回の絞り込みで3〜4回・画面のスレッド）。
    /// 足跡は写しとして持つ（<c>DataStore.Recent</c>。変わっていなければ同じ物が返る）ので、
    /// 返った物が前と同じなら分けた結果も前の物を使う。持つのは商品数ぶんの時刻の表3つ（2000件で数百KB）。
    /// </summary>
    public ViewModels.RecentTimes AllTimes()
    {
        var log = _store.Recent.Load();
        if (_times is { } seen && ReferenceEquals(seen.Source, log))
        {
            return seen.Times;
        }

        var times = new ViewModels.RecentTimes(
            RecentActivity.Times(log.Entries, RecentKind.Added),
            RecentActivity.Times(log.Entries, RecentKind.Used),
            RecentActivity.Times(log.Entries, RecentKind.Viewed));
        _times = new TimesCache(log, times);
        return times;
    }

    private sealed record TimesCache(RecentLog Source, ViewModels.RecentTimes Times);

    private volatile TimesCache? _times;
}
