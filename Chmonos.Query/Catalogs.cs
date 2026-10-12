using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query;

// 商品以外の答えの形（docs/spec/local-api-shapes.md の URL の表）。商品の答えと同じく、名前と意味は外の道具との約束。

/// <summary>アバターの登録簿の1体（<c>/v1/avatars</c>）。</summary>
/// <param name="IsAvatar">アバターとして扱うか（登録簿には、対応アバターとして名指しされたアバターでない商品も載る）。</param>
/// <param name="BaseName">属する共通素体（手で決めた物、無ければ推定）。</param>
/// <param name="DirectCount">名指しで対応している商品の数（画面の検索と同じ数え方）。</param>
/// <param name="ViaBaseCount">素体を通して対応している商品の数。</param>
public sealed record AvatarEntryRef(
    string Id,
    string Name,
    string? BoothName,
    string? ShopName,
    string? Category,
    bool IsAvatar,
    bool IsOwned,
    bool IsOwnedManually,
    string? BaseName,
    IReadOnlyList<string> Aliases,
    string? Memo,
    string? ImagePath,
    int DirectCount,
    int ViaBaseCount);

/// <summary>共通素体（<c>/v1/avatar-bases</c>）。</summary>
/// <param name="ItemId">素体そのものの商品（配布されていなければ null）。</param>
/// <param name="Members">属するアバターの ID。</param>
/// <param name="IsRejected">人が「素体ではない」とした物。</param>
public sealed record AvatarBaseRef(string Name, string? ItemId, IReadOnlyList<string> Members, IReadOnlyList<string> Aliases, string? Memo, bool IsManual, bool IsRejected);

/// <summary>ショップ（<c>/v1/shops</c>）。星とメモは shops.json（ショップの画面で付けた物）。</summary>
public sealed record ShopEntryRef(string Key, string? Name, string? Subdomain, bool IsLocal, string? Url, bool IsFavorite, string? Memo, int ItemCount, int OwnedCount);

public sealed record UserTagSubRef(string Name, string? Memo, int Count);

/// <summary>ユーザータグの大分類（<c>/v1/user-tags</c>）。<paramref name="Count"/> はその大分類を付けた商品の数。</summary>
public sealed record UserTagTopRef(string Name, string? Memo, int Count, IReadOnlyList<UserTagSubRef> Subs);

/// <summary>属性（<c>/v1/attributes</c>）。<paramref name="RatedCount"/> は評価した商品の数。</summary>
public sealed record AttributeEntryRef(string Name, string? Memo, bool IsDefault, int RatedCount);

/// <summary>カテゴリ（<c>/v1/categories</c>）。画面と同じ実質の1つで数える（自分で入れたカテゴリがあればそれ）。</summary>
public sealed record CategoryEntryRef(string Name, string? Parent, int Count);

public sealed record CountRef(string Value, int Count);

/// <summary>ファイル・フォルダから引いた商品（<c>/v1/files/lookup</c>）。</summary>
/// <param name="Kind"><c>file</c> か <c>folder</c>。</param>
/// <param name="State">ファイルなら <see cref="FileRef.State"/> と同じ。フォルダは <c>present</c> か <c>missing</c>。</param>
/// <param name="Path">記録の場所（フォルダの中のファイルを引いたときは、登録したフォルダの場所）。</param>
public sealed record FileOwnerRef(string ItemId, string ItemName, string Kind, string State, string Path, string? Hash);

/// <summary>手元の <c>.unitypackage</c> 1つ（<c>/v1/unitypackages</c>）。</summary>
/// <param name="Container">入っている zip の場所か、登録したフォルダの場所。</param>
/// <param name="Entry">zip・フォルダの中の場所（区切りは <c>/</c>）。</param>
/// <param name="Roots">パッケージが入れる先（zip の中の物だけ。フォルダの物は調べていないので空）。</param>
public sealed record UnityPackageEntryRef(string ItemId, string ItemName, string Source, string Container, string Entry, IReadOnlyList<string> Roots, IReadOnlyList<string> AvatarIds);

public sealed record ModificationMemberRef(string ItemId, long? VariationId, string? FileHash, string? Package, DateTimeOffset AddedAt, bool Detached);

public sealed record ModificationImageRef(string Name, string Path, string? Caption, DateTimeOffset AddedAt);

/// <summary>改変（<c>/v1/modifications</c>）。</summary>
public sealed record ModificationRef(
    string Id,
    string Name,
    string AvatarId,
    string AvatarName,
    string? UnityProject,
    string? Memo,
    string? BlueprintId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ModificationMemberRef> Members,
    IReadOnlyList<ModificationImageRef> Images);

public sealed record UnityProjectRef(string Path, IReadOnlyList<string> Modifications);

/// <summary>通知（<c>/v1/notifications</c>）。</summary>
public sealed record NotificationRef(string Id, string Kind, string? ItemId, string Title, string Detail, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsRead, bool IsResolved);

/// <summary>使う人の表示の設定（<c>/v1/display-settings</c>）。外の道具が画面と同じに隠せるように。</summary>
public sealed record DisplaySettingsRef(bool ShowAdult, bool ShowHiddenCountInSearch, bool ShowRemovedBoothImages, string ThumbnailRole);

/// <summary>検索の条件の種類（<c>/v1/search/conditions</c>）。外の道具が条件の画面を組むため。</summary>
/// <param name="Shape">値の形：<c>choice</c>・<c>list</c>・<c>avatar</c>・<c>range</c>・<c>date</c>・<c>attribute</c>・<c>userTag</c>・<c>modification</c>・<c>editStatus</c>・<c>updateNotice</c>・<c>recent</c>。</param>
/// <param name="Choices">選ぶ条件の選択肢（先頭が既定）。</param>
/// <param name="Options">選ぶ条件の補助の切り替え（名前と既定）。</param>
/// <param name="Fields">編集状況の項目・更新通知ありの種類の名前。</param>
public sealed record ConditionKindRef(string Kind, string Shape, IReadOnlyList<string>? Choices, IReadOnlyDictionary<string, bool>? Options, bool AllowsAll, IReadOnlyList<string>? Fields);

/// <summary>商品以外の答えを作る。</summary>
public static class Catalogs
{
    public static IReadOnlyList<AvatarEntryRef> Avatars(QuerySnapshot snapshot)
    {
        var direct = new Dictionary<string, int>(StringComparer.Ordinal);
        var viaBase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items)
        {
            foreach (var (avatarId, match) in snapshot.Compatibility.Resolve(item.Local))
            {
                var counts = match == AvatarMatch.Direct ? direct : viaBase;
                counts[avatarId] = counts.GetValueOrDefault(avatarId) + 1;
            }
        }

        return snapshot.Registry.Entries
            .Select(entry => new AvatarEntryRef(
                entry.ItemId,
                snapshot.AvatarNames.GetValueOrDefault(entry.ItemId) ?? entry.ItemId,
                entry.BoothName,
                entry.ShopName,
                entry.Category,
                AvatarService.IsAvatar(entry),
                snapshot.OwnedAvatarIds.Contains(entry.ItemId),
                entry.IsOwnedManually,
                string.IsNullOrWhiteSpace(entry.BaseName) ? snapshot.Compatibility.BaseNameOf(entry.ItemId) : entry.BaseName,
                entry.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text).ToList(),
                entry.Memo,
                AvatarImageSync.IconPath(snapshot.Paths, entry.ItemId, snapshot.ItemsById.GetValueOrDefault(entry.ItemId), snapshot.Settings.ShowRemovedBoothImages),
                direct.GetValueOrDefault(entry.ItemId),
                viaBase.GetValueOrDefault(entry.ItemId)))
            .ToList();
    }

    public static IReadOnlyList<AvatarBaseRef> AvatarBases(QuerySnapshot snapshot)
        => snapshot.Registry.BaseGroups
            .Select(group => new AvatarBaseRef(
                group.Name,
                group.ItemId,
                snapshot.Compatibility.MembersOf(group.Name),
                group.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text).ToList(),
                group.Memo,
                group.IsManual,
                group.Rejected))
            .ToList();

    public static IReadOnlyList<ShopEntryRef> Shops(QuerySnapshot snapshot)
    {
        var notes = snapshot.ShopNotes
            .GroupBy(note => note.Subdomain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        return snapshot.Items
            .Where(item => item.ShopSubdomain is not null)
            .GroupBy(item => item.ShopSubdomain!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var key = group.Key;
                var local = LocalShopKey.IsLocal(key);
                var note = notes.GetValueOrDefault(key);
                return new ShopEntryRef(
                    key,
                    first.ShopName,
                    local ? null : key,
                    local,
                    group.Select(item => item.Local.Shop?.Url ?? item.Booth.Shop?.Url).FirstOrDefault(url => url is not null),
                    snapshot.FavoriteShops.Contains(key),
                    note?.Memo,
                    group.Count(),
                    group.Count(item => item.IsOwned));
            })
            .ToList();
    }

    public static ShopEntryRef Shop(QuerySnapshot snapshot, string key)
        => Shops(snapshot).FirstOrDefault(shop => string.Equals(shop.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw QueryException.NotFound("shopNotFound", $"ショップが見つかりません: {key}");

    /// <summary>マスタの並び（タグの管理の並び）。マスタに無いのに商品に付いている大分類・小分類は、後ろに足す（黙って消さない）。</summary>
    public static IReadOnlyList<UserTagTopRef> UserTags(QuerySnapshot snapshot)
    {
        var topCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var subCounts = new Dictionary<(string, string), int>();
        foreach (var item in snapshot.Items)
        {
            foreach (var tag in item.Local.UserTags)
            {
                topCounts[tag.Top] = topCounts.GetValueOrDefault(tag.Top) + 1;
                foreach (var sub in tag.Subs.Distinct(StringComparer.Ordinal))
                {
                    subCounts[(tag.Top, sub)] = subCounts.GetValueOrDefault((tag.Top, sub)) + 1;
                }
            }
        }

        var result = snapshot.UserTagMaster.Tops
            .Select(top => new UserTagTopRef(
                top.Name,
                top.Memo,
                topCounts.GetValueOrDefault(top.Name),
                top.Subs.Select(sub => new UserTagSubRef(sub.Name, sub.Memo, subCounts.GetValueOrDefault((top.Name, sub.Name))))
                    .Concat(subCounts.Keys
                        .Where(pair => pair.Item1 == top.Name && top.Subs.All(sub => sub.Name != pair.Item2))
                        .Select(pair => new UserTagSubRef(pair.Item2, null, subCounts[pair])))
                    .ToList()))
            .ToList();

        foreach (var orphan in topCounts.Keys.Where(name => snapshot.UserTagMaster.Tops.All(top => top.Name != name)).Order(StringComparer.Ordinal))
        {
            result.Add(new UserTagTopRef(
                orphan,
                null,
                topCounts[orphan],
                subCounts.Keys.Where(pair => pair.Item1 == orphan).Select(pair => new UserTagSubRef(pair.Item2, null, subCounts[pair])).ToList()));
        }

        return result;
    }

    public static IReadOnlyList<AttributeEntryRef> Attributes(QuerySnapshot snapshot)
    {
        var rated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in snapshot.Items.SelectMany(item => item.Local.Attributes.Keys))
        {
            rated[name] = rated.GetValueOrDefault(name) + 1;
        }

        var result = snapshot.AttributeMaster.Attributes
            .Select(attribute => new AttributeEntryRef(attribute.Name, attribute.Memo, attribute.IsDefault, rated.GetValueOrDefault(attribute.Name)))
            .ToList();
        result.AddRange(rated.Keys
            .Where(name => snapshot.AttributeMaster.Attributes.All(attribute => attribute.Name != name))
            .Order(StringComparer.Ordinal)
            .Select(name => new AttributeEntryRef(name, null, false, rated[name])));
        return result;
    }

    public static IReadOnlyList<CategoryEntryRef> Categories(QuerySnapshot snapshot)
    {
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parent in CategoryTable.Bundled().Parents)
        {
            foreach (var child in parent.Children)
            {
                parentOf.TryAdd(child, parent.Name);
            }
        }

        return snapshot.Items
            .Where(item => item.CategoryName is not null)
            .GroupBy(item => item.CategoryName!, StringComparer.Ordinal)
            .Select(group => new CategoryEntryRef(
                group.Key,
                group.Select(item => item.HasUserCategory ? null : item.Booth.Category?.ParentName).FirstOrDefault(name => name is not null)
                    ?? parentOf.GetValueOrDefault(group.Key),
                group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<CountRef> BoothTags(QuerySnapshot snapshot)
        => snapshot.Items
            .SelectMany(item => item.Booth.Tags.Distinct(StringComparer.Ordinal))
            .GroupBy(tag => tag, StringComparer.Ordinal)
            .Select(group => new CountRef(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Value, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// ファイル・フォルダの場所か、ファイルのハッシュから、どの商品の物かを引く（Unity や外の道具から逆に引く）。
    /// 場所は大文字・小文字と区切り（<c>/</c> と <c>\</c>）を問わない。登録したフォルダの中の場所なら、そのフォルダの商品を返す。
    /// 外したファイル・古い版も返す（<see cref="FileOwnerRef.State"/> で見分ける）。
    /// </summary>
    public static IReadOnlyList<FileOwnerRef> LookupFiles(QuerySnapshot snapshot, string? path, string? hash)
    {
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(hash))
        {
            throw QueryException.Invalid("invalidLookup", "path か hash のどちらか1つを送ります。");
        }

        var result = new List<FileOwnerRef>();
        var wanted = path is null ? null : FolderTree.Normalize(path);
        foreach (var item in snapshot.Items)
        {
            foreach (var file in item.Local.LocalFiles)
            {
                var state = ItemMapper.FileState(file);
                if (hash is not null)
                {
                    if (string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new FileOwnerRef(item.Id, item.DisplayName, "file", state, file.Paths.FirstOrDefault() ?? string.Empty, file.Hash));
                    }

                    continue;
                }

                foreach (var place in file.Paths)
                {
                    if (string.Equals(FolderTree.Normalize(place), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new FileOwnerRef(item.Id, item.DisplayName, "file", state, place, file.Hash));
                    }
                }
            }

            if (wanted is null)
            {
                continue;
            }

            foreach (var folder in item.Local.LocalFolders)
            {
                var root = FolderTree.Normalize(folder.Path);
                if (string.Equals(root, wanted, StringComparison.OrdinalIgnoreCase)
                    || wanted.StartsWith(FolderTree.UnderPrefix(folder.Path), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new FileOwnerRef(item.Id, item.DisplayName, "folder", folder.MissingSince is null ? "present" : "missing", folder.Path, null));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 手元の <c>.unitypackage</c>（置き場のある zip の中と、登録したフォルダの中）。<paramref name="avatarId"/> を渡すと、
    /// そのアバターに対応する商品（画面の検索と同じ数え方。素体経由を含む）の物だけ。
    /// </summary>
    public static IReadOnlyList<UnityPackageEntryRef> UnityPackages(QuerySnapshot snapshot, string? avatarId)
    {
        var result = new List<UnityPackageEntryRef>();
        foreach (var item in snapshot.Items)
        {
            var avatars = snapshot.Compatibility.Resolve(item.Local).Keys.Order(StringComparer.Ordinal).ToList();
            if (avatarId is not null && !avatars.Contains(avatarId))
            {
                continue;
            }

            foreach (var file in item.Local.LocalFiles.Where(file => ItemMapper.FileState(file) == "present"))
            {
                foreach (var package in file.UnityPackages ?? [])
                {
                    result.Add(new UnityPackageEntryRef(item.Id, item.DisplayName, "file", file.Paths[0], package.Entry, package.Roots, avatars));
                }
            }

            foreach (var folder in item.Local.LocalFolders.Where(folder => folder.MissingSince is null))
            {
                foreach (var entry in folder.UnityPackages)
                {
                    result.Add(new UnityPackageEntryRef(item.Id, item.DisplayName, "folder", folder.Path, entry, [], avatars));
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<ModificationRef> Modifications(QuerySnapshot snapshot)
        => snapshot.Modifications.Records
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .Select(record => Modification(snapshot, record))
            .ToList();

    public static ModificationRef Modification(QuerySnapshot snapshot, string id)
        => snapshot.Modifications.Records.FirstOrDefault(record => record.Id == id) is { } record
            ? Modification(snapshot, record)
            : throw QueryException.NotFound("modificationNotFound", $"改変が見つかりません: {id}");

    /// <summary>その商品を使っている改変（外した物も含め、<see cref="ModificationMemberRef.Detached"/> で見分ける）。</summary>
    public static IReadOnlyList<ModificationRef> ModificationsUsing(QuerySnapshot snapshot, string itemId)
        => snapshot.Modifications.Records
            .Where(record => record.Members.Any(member => member.ItemId == itemId))
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .Select(record => Modification(snapshot, record))
            .ToList();

    private static ModificationRef Modification(QuerySnapshot snapshot, ModificationRecord record)
    {
        var directory = snapshot.Paths.ModificationImagesDir(record.Id);
        return new ModificationRef(
            record.Id,
            record.Name,
            record.AvatarItemId,
            snapshot.AvatarNames.GetValueOrDefault(record.AvatarItemId) ?? record.AvatarItemId,
            record.UnityProject,
            record.Memo,
            record.BlueprintId,
            record.CreatedAt,
            record.UpdatedAt,
            record.Members.Select(member => new ModificationMemberRef(member.ItemId, member.VariationId, member.FileHash, member.Package, member.AddedAt, member.Detached)).ToList(),
            record.Images.Select(image => new ModificationImageRef(image.FileName, Path.Combine(directory, image.FileName), image.Caption, image.AddedAt)).ToList());
    }

    public static IReadOnlyList<UnityProjectRef> UnityProjects(QuerySnapshot snapshot)
        => snapshot.Modifications.Records
            .Where(record => !string.IsNullOrWhiteSpace(record.UnityProject))
            .GroupBy(record => record.UnityProject!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UnityProjectRef(group.First().UnityProject!, group.Select(record => record.Id).Order(StringComparer.Ordinal).ToList()))
            .ToList();

    public static IReadOnlyList<NotificationRef> Notifications(QuerySnapshot snapshot, string? itemId)
        => snapshot.Notifications
            .Where(record => itemId is null || record.ItemId == itemId)
            .OrderByDescending(record => record.CreatedAt)
            .ThenBy(record => record.Id, StringComparer.Ordinal)
            .Select(record => new NotificationRef(
                record.Id,
                JsonNamingPolicy.CamelCase.ConvertName(record.Kind.ToString()),
                record.ItemId,
                record.Title,
                record.Detail,
                record.CreatedAt,
                record.UpdatedAt,
                record.IsRead,
                record.IsResolved))
            .ToList();

    public static DisplaySettingsRef DisplaySettings(QuerySnapshot snapshot)
        => new(
            snapshot.Settings.ShowAdult,
            snapshot.Settings.ShowHiddenCountInSearch,
            snapshot.Settings.ShowRemovedBoothImages,
            JsonNamingPolicy.CamelCase.ConvertName(snapshot.Settings.ThumbnailRole.ToString()));

    public static IReadOnlyList<ConditionKindRef> ConditionKinds()
        => Search.ConditionKinds.All
            .Select(pair =>
            {
                var (name, kind) = pair;
                if (SearchModuleConditions.ChoiceOf(kind) is { } choice)
                {
                    return new ConditionKindRef(
                        name,
                        "choice",
                        choice.Keys,
                        choice.Flag is { } flag ? new Dictionary<string, bool> { [flag.StateKey] = flag.Default } : null,
                        false,
                        null);
                }

                if (SearchModuleConditions.ListOf(kind) is { } list)
                {
                    return new ConditionKindRef(name, kind == SearchModuleKind.Avatar ? "avatar" : "list", null, null, list.AllowsAnd, null);
                }

                return kind switch
                {
                    SearchModuleKind.WishList or SearchModuleKind.Price => new ConditionKindRef(name, "range", null, null, kind == SearchModuleKind.Price, null),
                    SearchModuleKind.PublishedAt or SearchModuleKind.AcquiredAt => new ConditionKindRef(name, "date", null, null, false, null),
                    SearchModuleKind.Unedited => new ConditionKindRef(
                        name, "editStatus",
                        [EditStatusCondition.MissingKey, EditStatusCondition.FilledKey, EditStatusCondition.NeutralKey],
                        null, true, EditFieldsMissing.All.Select(EditFieldsMissing.KeyOf).ToList()),
                    SearchModuleKind.Updated => new ConditionKindRef(
                        name, "updateNotice",
                        [UpdateNoticeCondition.UpdatedKey, UpdateNoticeCondition.OtherKey, UpdateNoticeCondition.NeutralKey],
                        null, true, UpdateNoticeCondition.AllKinds.Select(UpdateNoticeCondition.KeyOf).ToList()),
                    SearchModuleKind.Recent => new ConditionKindRef(name, "recent", ["used", "viewed", "added"], null, false, null),
                    SearchModuleKind.Attribute => new ConditionKindRef(name, "attribute", null, null, true, null),
                    SearchModuleKind.UserTag => new ConditionKindRef(name, "userTag", null, null, true, null),
                    _ => new ConditionKindRef(name, "modification", null, null, true, null),
                };
            })
            .ToList();
}
