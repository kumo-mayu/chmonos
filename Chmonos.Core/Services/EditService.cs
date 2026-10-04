using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

public interface IEditService
{
    Task<bool> SaveLocalAsync(
        string itemId,
        LocalBlock local,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default);

    Task<EditSession> StartSessionAsync(IReadOnlyList<string> itemIds, int index = 0, CancellationToken cancellationToken = default);

    Task<EditSession> AdvanceSessionAsync(int index, CancellationToken cancellationToken = default);

    Task<EditSession> NoteSavedAsync(string itemId, CancellationToken cancellationToken = default);

    Task<EditSession> ReplaceItemIdAsync(string fromId, string toId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 順番を積み直す。今の記録は使わない（保存した印も空に戻す）が、**書くのは錠の中**にする。
    ///
    /// 丸ごと書くので錠は要らないように見えるが、錠の外で書くと、位置・保存した印・IDの付け替えが
    /// 古い記録を読んだ後に割り込み、その書き手が後から古い順番を書き戻して、積み直した順番が消える（2026-10-04 に試験で再現）。
    /// 入り直して順番を詰めたときは <paramref name="index"/> も一緒に渡す。順番と位置を2回に分けて書くと、間で落ちたときに位置が先頭に戻る。
    /// </summary>
    public Task<EditSession> StartSessionAsync(
        IReadOnlyList<string> itemIds,
        int index = 0,
        CancellationToken cancellationToken = default)
        => _store.EditSession.UpdateAsync(
            _ => new EditSession
            {
                ItemIds = itemIds,
                Index = index,
                StartedAt = DateTimeOffset.Now,
            },
            cancellationToken);

    /// <summary>
    /// 位置だけを進める。1件ごとに書くので、落ちても直前まで戻る。
    ///
    /// 位置と「保存した印」は別々に書かれるので、**錠の中で今の記録に当てる**。
    /// 錠の外で読んで書いていたため、保存して次へ・戻るが重なると、
    /// 後から書いた方が相手の欄を消していた。
    /// </summary>
    public Task<EditSession> AdvanceSessionAsync(int index, CancellationToken cancellationToken = default)
        => _store.EditSession.UpdateAsync(session => session with { Index = index }, cancellationToken);

    /// <summary>
    /// この回で保存した商品を控える。編集画面の上の帯で、保存した物と飛ばした物を見分けるため。
    /// </summary>
    public async Task<EditSession> NoteSavedAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var written = new EditSession();
        await _store.EditSession.TryUpdateAsync(
            current =>
            {
                written = current;
                if (current.SavedItemIds.Contains(itemId, StringComparer.Ordinal))
                {
                    return null;
                }

                written = current with { SavedItemIds = [.. current.SavedItemIds, itemId] };
                return written;
            },
            cancellationToken);

        return written;
    }

    /// <summary>
    /// 編集の最中に商品のIDを変えたとき、順番と保存した印の中のIDを付け替える。
    /// 付け替えないと、続きから開いたときに元のIDは無いので飛ばされ、移した先の商品が順番から消える。
    ///
    /// 位置・保存した印と同じく**錠の中で今の記録に当てる**。ここだけ錠の外で読んで書いていたので、
    /// IDの付け替えと「保存して次へ」が重なると、進めた位置か保存した印のどちらかが古い値に戻っていた。
    /// </summary>
    public async Task<EditSession> ReplaceItemIdAsync(string fromId, string toId, CancellationToken cancellationToken = default)
    {
        string Swap(string id) => string.Equals(id, fromId, StringComparison.Ordinal) ? toId : id;

        var written = new EditSession();
        await _store.EditSession.TryUpdateAsync(
            current =>
            {
                written = current;
                if (!current.ItemIds.Contains(fromId, StringComparer.Ordinal))
                {
                    return null;
                }

                written = current with
                {
                    ItemIds = [.. current.ItemIds.Select(Swap)],
                    SavedItemIds = [.. current.SavedItemIds.Select(Swap)],
                };
                return written;
            },
            cancellationToken);

        return written;
    }

    /// <summary>
    /// 終えた順番を捨てる。積み直しと同じ理由で錠の中で書く——錠の外で消すと、古い記録を読んだ書き手が後から書き戻し、
    /// 終えたはずの順番が次に入ったときに続きとして出る。
    /// </summary>
    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
        => _store.EditSession.UpdateAsync(_ => new EditSession(), cancellationToken);

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

        // マスタは管理画面も書く。読み直してから足さないと、
        // 管理画面が並べ替え・改名している最中に足した分が消える（`docs/spec/data-model.md`）
        var subName = sub?.Trim();
        var written = new UserTagMaster();
        await _store.UserTags.TryUpdateAsync(
            master =>
            {
                written = master;
                var tops = master.Tops.ToList();

                var index = tops.FindIndex(entry => string.Equals(entry.Name, topName, StringComparison.CurrentCultureIgnoreCase));
                if (index < 0)
                {
                    tops.Add(new UserTagTop { Name = topName });
                    index = tops.Count - 1;
                }

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

                written = new UserTagMaster { Tops = tops };
                return written;
            },
            cancellationToken);

        return written;
    }

    public async Task<AttributeMaster> AddAttributeAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return _store.Attributes.Load();
        }

        var written = new AttributeMaster();
        await _store.Attributes.TryUpdateAsync(
            master =>
            {
                written = master;
                if (master.Attributes.Any(entry => string.Equals(entry.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return null;
                }

                written = new AttributeMaster
                {
                    Attributes = [.. master.Attributes, new AttributeDefinition { Name = trimmed }],
                };

                return written;
            },
            cancellationToken);

        return written;
    }
}
