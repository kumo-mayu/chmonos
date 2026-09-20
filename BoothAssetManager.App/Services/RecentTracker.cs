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
        => RecentActivity.Times(_store.Recent.Load().Entries, kind);
}
