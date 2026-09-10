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

    /// <summary>
    /// 書き込みを1本に直列化する。閲覧と使ったが同時に走ると、
    /// 読んでから書くまでの間に相手の足跡を消してしまう。
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

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

        await _gate.WaitAsync();
        try
        {
            var log = _store.Recent.Load();
            var updated = RecentActivity.Touch(log.Entries, itemId, kind, at);
            await _store.Recent.SaveAsync(new RecentLog { Entries = updated });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 足跡が1つ欠けても商品の記録は無事。ここで止める理由が無い
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>その種類の時刻を商品IDから引ける形で返す。並べ替えのために読む。</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> Times(RecentKind kind)
        => RecentActivity.Times(_store.Recent.Load().Entries, kind);
}
