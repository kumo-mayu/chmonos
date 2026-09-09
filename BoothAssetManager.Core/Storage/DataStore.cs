using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Storage;

/// <summary>1つのJSONファイル全体を1つの値として読み書きする小さな入れ物。</summary>
public sealed class JsonFileStore<T> where T : class, new()
{
    private readonly string _path;

    public JsonFileStore(string path)
    {
        _path = path;
    }

    public string Path => _path;

    /// <summary>ファイルが無ければ既定値を返す（初回起動をそのまま通す）。</summary>
    public T Load() => JsonStore.Read<T>(_path) ?? new T();

    public Task SaveAsync(T value, CancellationToken cancellationToken = default)
        => JsonStore.WriteAsync(_path, value, cancellationToken);
}

/// <summary>
/// 保存されるもの一式への入り口。どのファイルが何を持つかをここだけ見れば分かるようにする。
/// </summary>
public sealed class DataStore
{
    public DataStore(AppPaths paths)
    {
        Paths = paths;
        Items = new ItemRepository(paths);
        UserTags = new JsonFileStore<UserTagMaster>(paths.UserTagsFile);
        Attributes = new JsonFileStore<AttributeMaster>(paths.AttributesFile);
        Avatars = new JsonFileStore<AvatarRegistry>(paths.AvatarRegistryFile);
        Settings = new JsonFileStore<AppSettings>(paths.SettingsFile);
        Unresolved = new JsonFileStore<List<UnresolvedFile>>(paths.UnresolvedFile);
        Excluded = new JsonFileStore<List<ExcludedEntry>>(paths.ExcludedFile);
        Notifications = new JsonFileStore<List<NotificationRecord>>(paths.NotificationsFile);
        ShopBanners = new JsonFileStore<List<ShopBannerRecord>>(paths.ShopBannersFile);
        ScanCache = new JsonFileStore<List<ScanCacheEntry>>(paths.ScanCacheFile);
        EditSession = new JsonFileStore<EditSession>(paths.EditSessionFile);
    }

    public AppPaths Paths { get; }

    public ItemRepository Items { get; }

    public JsonFileStore<UserTagMaster> UserTags { get; }

    public JsonFileStore<AttributeMaster> Attributes { get; }

    public JsonFileStore<AvatarRegistry> Avatars { get; }

    public JsonFileStore<AppSettings> Settings { get; }

    public JsonFileStore<List<UnresolvedFile>> Unresolved { get; }

    public JsonFileStore<List<ExcludedEntry>> Excluded { get; }

    public JsonFileStore<List<NotificationRecord>> Notifications { get; }

    /// <summary>ショップのバナーを調べた記録。無いショップを何度も探しに行かないため。</summary>
    public JsonFileStore<List<ShopBannerRecord>> ShopBanners { get; }

    public JsonFileStore<List<ScanCacheEntry>> ScanCache { get; }

    /// <summary>編集キューの位置。中断して次回続きから再開するために持つ。</summary>
    public JsonFileStore<EditSession> EditSession { get; }
}
