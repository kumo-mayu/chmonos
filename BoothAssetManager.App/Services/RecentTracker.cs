using System.IO;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App.Services;

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

    public RecentTracker(DataStore store)
    {
        _store = store;
    }

    public Task TouchAsync(string itemId, RecentKind kind)
        => TouchAsync(itemId, kind, DateTimeOffset.Now);

    public async Task TouchAsync(string itemId, RecentKind kind, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

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
