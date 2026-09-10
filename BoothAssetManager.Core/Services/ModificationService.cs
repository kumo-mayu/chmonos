using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

public interface IModificationService
{
    /// <summary>そのアバターの改変。新しく作った順。</summary>
    Task<IReadOnlyList<ModificationRecord>> LoadForAvatarAsync(
        string avatarItemId,
        CancellationToken cancellationToken = default);

    Task<ModificationLoadResult> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 改変を作る。アバターが登録簿に無ければその場で足す。
    /// 名前が空なら作らない（null を返す）。
    /// </summary>
    Task<ModificationRecord?> CreateAsync(
        string avatarItemId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>消す。**貼った画像も一緒に消える。**聞くのは呼ぶ側。</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>同じアバターに同じ名前の改変が既にあるか。作る前に知らせるために見る。</summary>
    Task<bool> HasSameNameAsync(
        string avatarItemId,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 改変の記録を作る・消す・読む。
///
/// 決めた理由は <c>設計詳細_改変の記録.md</c>。要点だけ：
/// **アバター1体＋名前で1つ**（1体に複数持てる）、**同じ名前も許す**
/// （「普段着」を作り直したいとき、古い方を消す前に新しい方を作れないと困る）。
/// </summary>
public sealed class ModificationService : IModificationService
{
    private readonly DataStore _store;

    public ModificationService(DataStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<ModificationRecord>> LoadForAvatarAsync(
        string avatarItemId,
        CancellationToken cancellationToken = default)
    {
        var all = await _store.Modifications.LoadAllAsync(cancellationToken);
        return all.Modifications
            .Where(record => string.Equals(record.AvatarItemId, avatarItemId, StringComparison.Ordinal))
            .ToList();
    }

    public Task<ModificationLoadResult> LoadAllAsync(CancellationToken cancellationToken = default)
        => _store.Modifications.LoadAllAsync(cancellationToken);

    public async Task<ModificationRecord?> CreateAsync(
        string avatarItemId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || string.IsNullOrWhiteSpace(avatarItemId))
        {
            return null;
        }

        // **登録簿に無ければその場で足す。**
        // 「登録簿に入るまで改変が作れない」を避ける——改変を作る操作が登録簿を育てる
        await EnsureInRegistryAsync(avatarItemId, cancellationToken);

        var now = DateTimeOffset.Now;
        var record = new ModificationRecord
        {
            Id = ModificationId.For(avatarItemId, trimmed, now),
            AvatarItemId = avatarItemId,
            Name = trimmed,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _store.Modifications.SaveAsync(record, cancellationToken);
        return record;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_store.Modifications.Exists(id))
        {
            return false;
        }

        _store.Modifications.Delete(id);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> HasSameNameAsync(
        string avatarItemId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var mine = await LoadForAvatarAsync(avatarItemId, cancellationToken);
        return mine.Any(record =>
            string.Equals(record.Name, trimmed, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// アバターを登録簿に入れる。既にあれば何もしない。
    ///
    /// **判定の結果は書かない。**ここで入れるのは「人が改変を作った」という事実で、
    /// 検出が決める <c>AvatarOverride</c> や <c>Category</c> には触らない。
    /// 名前は商品から引ければ入れる（引けなければ検出か手入力で後から埋まる）。
    /// </summary>
    private async Task EnsureInRegistryAsync(string avatarItemId, CancellationToken cancellationToken)
    {
        var registry = _store.Avatars.Load();
        if (registry.Entries.Any(entry =>
                string.Equals(entry.ItemId, avatarItemId, StringComparison.Ordinal)))
        {
            return;
        }

        var item = await _store.Items.LoadAsync(avatarItemId, cancellationToken);
        var entries = registry.Entries.ToList();
        entries.Add(new AvatarRegistryEntry
        {
            ItemId = avatarItemId,
            BoothName = item?.Booth.Name,
            Category = item?.Booth.Category?.Name,
        });

        await _store.Avatars.SaveAsync(
            new AvatarRegistry
            {
                Entries = entries.OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ToList(),
                BaseGroups = registry.BaseGroups,
            },
            cancellationToken);
    }
}
