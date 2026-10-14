using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>写しの部分（変わったときに読み直す単位）。</summary>
public enum StorePart
{
    Items,
    Images,
    Registry,
    UserTags,
    Attributes,
    ShopNotes,
    Notifications,
    Modifications,
    Recent,
    Settings,
    VideoTitles,

    /// <summary>保存先の形の印（format.json）。写しではなく、答えるかどうかに効く。</summary>
    Format,

    /// <summary>API の設定（api-settings.json）。写しではなく、許可するオリジンに効く。</summary>
    ApiSettings,
}

/// <summary>変わった部分の集まり。アプリからの知らせとファイルの変化の知らせを、読み直すまで貯める。</summary>
public sealed class StoreChangeSet
{
    private readonly HashSet<StorePart> _parts = [];
    private readonly HashSet<string> _itemIds = new(StringComparer.Ordinal);

    public IReadOnlySet<StorePart> Parts => _parts;

    /// <summary>変わった商品（知らせに載っていた物。読み直しは一覧を見て変わったファイルを全部拾うので、ここは外への知らせに使う）。</summary>
    public IReadOnlySet<string> ItemIds => _itemIds;

    public bool IsEmpty => _parts.Count == 0;

    public static StoreChangeSet Everything()
    {
        var all = new StoreChangeSet();
        foreach (var part in Enum.GetValues<StorePart>())
        {
            all._parts.Add(part);
        }

        all.Overflowed = true;
        return all;
    }

    /// <summary>知らせを取りこぼした（ファイルの変化の知らせがあふれた・通番が飛んだ）。全部を読み直す。</summary>
    public bool Overflowed { get; private set; }

    public void Add(StorePart part, string? itemId = null)
    {
        _parts.Add(part);
        if (itemId is not null)
        {
            _itemIds.Add(itemId);
        }
    }

    public void Add(StoreChangeSet other)
    {
        _parts.UnionWith(other._parts);
        _itemIds.UnionWith(other._itemIds);
        Overflowed |= other.Overflowed;
    }
}

/// <summary>保存先のファイルの場所から、写しのどの部分が変わったかを見分ける（docs/spec/data-model.md の保存するファイルの一覧）。</summary>
public static class StoreChanges
{
    /// <summary>写しに関係の無いファイル（ログ・控え・書きかけ・索引）は null。</summary>
    public static (StorePart Part, string? ItemId)? Classify(AppPaths paths, string fullPath)
    {
        var relative = Path.GetRelativePath(paths.Root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = parts[^1];
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parts.Length == 2 && Is(parts[0], "items") && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(name);
            return StoreIds.IsItemId(id) ? (StorePart.Items, id) : null;
        }

        if (parts.Length == 3 && Is(parts[0], "images"))
        {
            return parts[1] switch
            {
                "_avatars" => (StorePart.Registry, null),
                "_mods" => (StorePart.Modifications, null),
                var id when StoreIds.IsItemId(id) => (StorePart.Images, id),
                _ => null,
            };
        }

        // 商品の画像のフォルダごと消された・作られた
        if (parts.Length == 2 && Is(parts[0], "images") && StoreIds.IsItemId(name))
        {
            return (StorePart.Images, name);
        }

        if (parts.Length == 2 && Is(parts[0], "modifications"))
        {
            return (StorePart.Modifications, null);
        }

        if (parts.Length != 1)
        {
            return null;
        }

        StorePart? part = name.ToLowerInvariant() switch
        {
            "avatar-registry.json" => StorePart.Registry,
            "usertags.json" => StorePart.UserTags,
            "attributes.json" => StorePart.Attributes,
            "shops.json" => StorePart.ShopNotes,
            "notifications.json" => StorePart.Notifications,
            "recent.json" => StorePart.Recent,
            "settings.json" => StorePart.Settings,
            "video-titles.json" => StorePart.VideoTitles,
            "format.json" => StorePart.Format,
            "api-settings.json" => StorePart.ApiSettings,
            _ => null,
        };
        return part is { } found ? (found, null) : null;
    }

    private static bool Is(string segment, string name) => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 部分のファイルの印（大きさ・日時。改変はフォルダの中のファイルの数・合計・いちばん新しい日時）。
    /// 読み直しの前にこれを比べ、変わっていない部分は読み直さず、外にも知らせない（時々の見比べのたびに知らせが出ないように）。
    /// 商品と画像は商品ごとに見るので、ここでは扱わない（null）。
    /// </summary>
    public static string? StampOf(AppPaths paths, StorePart part)
    {
        if (part == StorePart.Modifications)
        {
            var directory = new DirectoryInfo(paths.ModificationsDir);
            if (!directory.Exists)
            {
                return "none";
            }

            var files = directory.GetFiles("*.json");
            return $"{files.Length}:{files.Sum(file => file.Length)}:{(files.Length == 0 ? 0 : files.Max(file => file.LastWriteTimeUtc.Ticks))}";
        }

        var path = part switch
        {
            StorePart.Registry => paths.AvatarRegistryFile,
            StorePart.UserTags => paths.UserTagsFile,
            StorePart.Attributes => paths.AttributesFile,
            StorePart.ShopNotes => paths.ShopNotesFile,
            StorePart.Notifications => paths.NotificationsFile,
            StorePart.Recent => paths.RecentFile,
            StorePart.Settings => paths.SettingsFile,
            StorePart.VideoTitles => paths.VideoTitlesFile,
            StorePart.Format => Path.Combine(paths.Root, StoreFormat.MarkerFileName),
            StorePart.ApiSettings => paths.ApiSettingsFile,
            _ => null,
        };

        if (path is null)
        {
            return null;
        }

        var info = new FileInfo(path);
        return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "none";
    }
}
