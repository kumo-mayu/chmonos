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

    Task<ModificationRecord?> LoadAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> RenameAsync(string id, string name, CancellationToken cancellationToken = default);

    Task<bool> SetMemoAsync(string id, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Unityプロジェクトを紐付ける。null で外す。</summary>
    Task<bool> SetProjectAsync(string id, string? path, CancellationToken cancellationToken = default);

    // ---- 構成物 ----

    /// <summary>
    /// 使ったものを足す。**末尾に付く**（並びが導入の順）。
    /// 同じ商品を2回足せる——別のバージョンを重ねることがある。
    /// </summary>
    Task<bool> AddMemberAsync(
        string id,
        ModificationMember member,
        CancellationToken cancellationToken = default);

    /// <summary>位置で外す。並びが意味を持つので、商品IDではなく位置で指す。</summary>
    Task<bool> RemoveMemberAsync(string id, int index, CancellationToken cancellationToken = default);

    /// <summary>位置を動かす。依存物を後から思い出したときに直せるようにする。</summary>
    Task<bool> MoveMemberAsync(
        string id,
        int index,
        int delta,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 手で足した行（どのファイルか分からない行）を、Unityへ送るときに選んだファイルの行に置き換える。
    /// 2つ選べば、その位置に2行並ぶ（同じ商品を別のファイルの行で持つ形）。
    /// **その行がまだファイルの記録の無い同じ商品のときだけ書く**——選んでいる間に並びが変わっていたら、別の行を書き換えてしまう。
    /// </summary>
    Task<bool> ReplaceMemberAsync(
        string id,
        int index,
        IReadOnlyList<ModificationMember> members,
        CancellationToken cancellationToken = default);

    // ---- 画像 ----

    /// <summary>画像を足す。商品と同じ圧縮を通す。読めなければ null。</summary>
    Task<string?> AddImageAsync(string id, byte[] bytes, CancellationToken cancellationToken = default);

    Task<bool> RemoveImageAsync(string id, string fileName, CancellationToken cancellationToken = default);

    Task<bool> MoveImageAsync(
        string id,
        string fileName,
        int delta,
        CancellationToken cancellationToken = default);

    /// <summary>その商品を使っている改変。商品ページに出すために引く。</summary>
    Task<IReadOnlyList<ModificationRecord>> LoadUsingItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>そのプロジェクトに紐づく改変。Unityへ送るときに引く。</summary>
    Task<IReadOnlyList<ModificationRecord>> LoadForProjectAsync(
        string projectPath,
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
    private readonly Images.ImagePipeline? _images;

    public ModificationService(DataStore store, Images.ImagePipeline? images = null)
    {
        _store = store;
        _images = images;
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

    public Task<ModificationRecord?> LoadAsync(string id, CancellationToken cancellationToken = default)
        => _store.Modifications.LoadAsync(id, cancellationToken);

    public Task<bool> RenameAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length == 0
            ? Task.FromResult(false)
            : UpdateAsync(id, record => record with { Name = trimmed }, cancellationToken);
    }

    public Task<bool> SetMemoAsync(string id, string? memo, CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            record => record with { Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim() },
            cancellationToken);

    public Task<bool> SetProjectAsync(string id, string? path, CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            record => record with { UnityProject = string.IsNullOrWhiteSpace(path) ? null : path.Trim() },
            cancellationToken);

    // ---- 構成物 ----

    public Task<bool> AddMemberAsync(
        string id,
        ModificationMember member,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            // **末尾に付ける。**並びが導入の順なので、後から来たものは後ろ
            record => record with { Members = [.. record.Members, member with { AddedAt = DateTimeOffset.Now }] },
            cancellationToken);

    public Task<bool> RemoveMemberAsync(string id, int index, CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            record =>
            {
                if (index < 0 || index >= record.Members.Count)
                {
                    return record;
                }

                var members = record.Members.ToList();
                members.RemoveAt(index);
                return record with { Members = members };
            },
            cancellationToken);

    public Task<bool> MoveMemberAsync(
        string id,
        int index,
        int delta,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            record =>
            {
                var members = record.Members.ToList();
                var to = index + delta;
                if (index < 0 || index >= members.Count || to < 0 || to >= members.Count)
                {
                    return record;
                }

                var moved = members[index];
                members.RemoveAt(index);
                members.Insert(to, moved);
                return record with { Members = members };
            },
            cancellationToken);

    public async Task<bool> ReplaceMemberAsync(
        string id,
        int index,
        IReadOnlyList<ModificationMember> members,
        CancellationToken cancellationToken = default)
    {
        if (members.Count == 0)
        {
            return false;
        }

        var replaced = false;
        var saved = await UpdateAsync(
            id,
            record =>
            {
                if (index < 0 || index >= record.Members.Count)
                {
                    return record;
                }

                var current = record.Members[index];
                if (current.FileHash is not null
                    || members.Any(member => !string.Equals(member.ItemId, current.ItemId, StringComparison.Ordinal)))
                {
                    return record;
                }

                // 足した日時は元の行のまま（いつから使っていたかは変わらない）。並びが導入の順なので、同じ位置に入れる
                var list = record.Members.ToList();
                list.RemoveAt(index);
                list.InsertRange(index, members.Select(member => member with { AddedAt = current.AddedAt }));
                replaced = true;
                return record with { Members = list };
            },
            cancellationToken);

        return saved && replaced;
    }

    // ---- 画像 ----

    public async Task<string?> AddImageAsync(
        string id,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        if (_images is null || !_store.Modifications.Exists(id))
        {
            return null;
        }

        var fileName = await _images.SaveModificationImageAsync(id, bytes, cancellationToken);
        if (fileName is null)
        {
            return null;
        }

        await UpdateAsync(
            id,
            record => record.Images.Any(image =>
                    string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase))

                // 同じ絵を2回落としても1枚にまとまる（名前が中身のハッシュ）。記録も増やさない
                ? record
                : record with
                {
                    Images = [.. record.Images, new ModificationImage
                    {
                        FileName = fileName,
                        AddedAt = DateTimeOffset.Now,
                    }],
                },
            cancellationToken);

        return fileName;
    }

    public async Task<bool> RemoveImageAsync(
        string id,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(fileName);
        var removed = await UpdateAsync(
            id,
            record => record with
            {
                Images = record.Images
                    .Where(image => !string.Equals(image.FileName, name, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            },
            cancellationToken);

        if (removed)
        {
            _images?.DeleteModificationImage(id, name);
        }

        return removed;
    }

    public Task<bool> MoveImageAsync(
        string id,
        string fileName,
        int delta,
        CancellationToken cancellationToken = default)
        => UpdateAsync(
            id,
            record =>
            {
                var images = record.Images.ToList();
                var index = images.FindIndex(image =>
                    string.Equals(image.FileName, Path.GetFileName(fileName), StringComparison.OrdinalIgnoreCase));

                var to = index + delta;
                if (index < 0 || to < 0 || to >= images.Count)
                {
                    return record;
                }

                var moved = images[index];
                images.RemoveAt(index);
                images.Insert(to, moved);
                return record with { Images = images };
            },
            cancellationToken);

    // ---- 逆引き ----

    public async Task<IReadOnlyList<ModificationRecord>> LoadUsingItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var all = await _store.Modifications.LoadAllAsync(cancellationToken);
        return all.Modifications
            .Where(record => record.Members.Any(member =>
                string.Equals(member.ItemId, itemId, StringComparison.Ordinal)))
            .ToList();
    }

    public async Task<IReadOnlyList<ModificationRecord>> LoadForProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return [];
        }

        var all = await _store.Modifications.LoadAllAsync(cancellationToken);
        return all.Modifications
            .Where(record => record.UnityProject is { } path && SamePath(path, projectPath))
            .ToList();
    }

    /// <summary>
    /// 同じプロジェクトを指しているか。
    ///
    /// Windowsのパスは大文字小文字を区別せず、末尾の区切りも揺れる。
    /// 素の文字列比較だと、同じプロジェクトを別物と見て取り逃す。
    /// </summary>
    public static bool SamePath(string? a, string? b)
        => a is not null && b is not null
        && string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 1件を読んで、書き換えて、書き戻す。
    ///
    /// **<c>UpdatedAt</c> は必ず動かす。**触った跡が残らないと、
    /// あとで「最近いじった改変」が分からない。
    ///
    /// 書き換えが何もしなかった場合（範囲外の位置を指した等）も true を返す。
    /// 「対象が無い」と「動かせなかった」を呼ぶ側で区別する必要が無い。
    /// </summary>
    private async Task<bool> UpdateAsync(
        string id,
        Func<ModificationRecord, ModificationRecord> update,
        CancellationToken cancellationToken)
    {
        if (await _store.Modifications.LoadAsync(id, cancellationToken) is not { } record)
        {
            return false;
        }

        await _store.Modifications.SaveAsync(
            update(record) with { UpdatedAt = DateTimeOffset.Now },
            cancellationToken);

        return true;
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
        if (_store.Avatars.Load().Entries.Any(entry =>
                string.Equals(entry.ItemId, avatarItemId, StringComparison.Ordinal)))
        {
            return;
        }

        var item = await _store.Items.LoadAsync(avatarItemId, cancellationToken);

        // 検出や名前の保存と同じファイルを書くので、錠を掛けて最新に足す（U15）
        await _store.Avatars.UpdateAsync(
            registry => registry.Entries.Any(entry =>
                    string.Equals(entry.ItemId, avatarItemId, StringComparison.Ordinal))
                ? registry
                : new AvatarRegistry
                {
                    Entries = registry.Entries
                        .Append(new AvatarRegistryEntry
                        {
                            ItemId = avatarItemId,
                            BoothName = item?.Booth.Name,
                            Category = item?.Booth.Category?.Name,
                        })
                        .OrderBy(entry => entry.ItemId, StringComparer.Ordinal)
                        .ToList(),
                    BaseGroups = registry.BaseGroups,
                },
            cancellationToken);
    }
}
