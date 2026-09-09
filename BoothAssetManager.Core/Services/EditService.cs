using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

public interface IEditService
{
    Task<bool> SaveLocalAsync(
        string itemId,
        LocalBlock local,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default);

    Task<EditSession> StartSessionAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default);

    Task<EditSession> AdvanceSessionAsync(int index, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);

    Task<UserTagMaster> AddUserTagAsync(string top, string? sub, CancellationToken cancellationToken = default);

    Task<AttributeMaster> AddAttributeAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// 編集画面のための操作。ユーザ入力の保存と、マスタへの追加を扱う。
///
/// マスタへの追加をここに置いているのは、編集の最中に
/// 「この分類が無いから作りたい」が必ず起きるため。
/// 一度画面を離れさせると、入力途中の内容を抱えたまま往復させることになる。
/// </summary>
public sealed class EditService : IEditService
{
    private readonly DataStore _store;

    public EditService(DataStore store)
    {
        _store = store;
    }

    /// <summary>
    /// <c>local</c> のうち、この画面が決めた項目だけを書く。<c>booth</c> は触らない。
    ///
    /// 丸ごと差し替えないのは、画面が開いた時点の写しを抱えているため。
    /// 開いている間に取り込みや検出が書いた項目まで、古い写しで潰してしまう。
    /// itemが消えていれば false（キューを積んだ後に消えることがある）。
    /// </summary>
    public Task<bool> SaveLocalAsync(
        string itemId,
        LocalBlock local,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default)
        => _store.Items.SaveLocalAsync(itemId, local, owns, cancellationToken: cancellationToken);

    public async Task<EditSession> StartSessionAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken = default)
    {
        var session = new EditSession
        {
            ItemIds = itemIds,
            Index = 0,
            StartedAt = DateTimeOffset.Now,
        };

        await _store.EditSession.SaveAsync(session, cancellationToken);
        return session;
    }

    /// <summary>位置だけを進める。1件ごとに書くので、落ちても直前まで戻る。</summary>
    public async Task<EditSession> AdvanceSessionAsync(int index, CancellationToken cancellationToken = default)
    {
        var session = _store.EditSession.Load() with { Index = index };
        await _store.EditSession.SaveAsync(session, cancellationToken);
        return session;
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
        => _store.EditSession.SaveAsync(new EditSession(), cancellationToken);

    /// <summary>
    /// userTagをマスタへ足す。既にあれば足さない（同じ名前が2つ並ぶとitem側の参照が曖昧になる）。
    /// <paramref name="sub"/> を指定するとトップ配下のサブとして足す。
    /// </summary>
    public async Task<UserTagMaster> AddUserTagAsync(
        string top,
        string? sub,
        CancellationToken cancellationToken = default)
    {
        var topName = top.Trim();
        if (topName.Length == 0)
        {
            return _store.UserTags.Load();
        }

        var master = _store.UserTags.Load();
        var tops = master.Tops.ToList();

        var index = tops.FindIndex(entry => string.Equals(entry.Name, topName, StringComparison.CurrentCultureIgnoreCase));
        if (index < 0)
        {
            tops.Add(new UserTagTop { Name = topName });
            index = tops.Count - 1;
        }

        var subName = sub?.Trim();
        if (!string.IsNullOrEmpty(subName))
        {
            var subs = tops[index].Subs.ToList();
            if (!subs.Any(entry => string.Equals(entry.Name, subName, StringComparison.CurrentCultureIgnoreCase)))
            {
                subs.Add(new UserTagSub { Name = subName });
                tops[index] = new UserTagTop
                {
                    Name = tops[index].Name,
                    Memo = tops[index].Memo,
                    Subs = subs,
                };
            }
        }

        var updated = new UserTagMaster { Tops = tops };
        await _store.UserTags.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<AttributeMaster> AddAttributeAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        var master = _store.Attributes.Load();

        if (trimmed.Length == 0
            || master.Attributes.Any(entry => string.Equals(entry.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            return master;
        }

        var updated = new AttributeMaster
        {
            Attributes = [.. master.Attributes, new AttributeDefinition { Name = trimmed }],
        };

        await _store.Attributes.SaveAsync(updated, cancellationToken);
        return updated;
    }
}
