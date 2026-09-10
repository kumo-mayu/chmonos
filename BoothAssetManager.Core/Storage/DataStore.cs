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
        Detached = new JsonFileStore<List<DetachedFile>>(paths.DetachedFile);
        Notifications = new JsonFileStore<List<NotificationRecord>>(paths.NotificationsFile);
        SearchHistory = new JsonFileStore<Services.SearchHistoryList>(paths.SearchHistoryFile);
        ShopBanners = new JsonFileStore<List<ShopBannerRecord>>(paths.ShopBannersFile);
        ScanCache = new JsonFileStore<List<ScanCacheEntry>>(paths.ScanCacheFile);
        ImportState = new JsonFileStore<Scanning.ImportState>(paths.ImportStateFile);
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

    /// <summary>商品ページで外したファイル。取り込みが同じ商品へ戻さないように見る。</summary>
    public JsonFileStore<List<DetachedFile>> Detached { get; }

    public JsonFileStore<List<NotificationRecord>> Notifications { get; }

    /// <summary>検索の履歴。商品を開いたときに1件積む</summary>
    public JsonFileStore<Services.SearchHistoryList> SearchHistory { get; }

    /// <summary>ショップのバナーを調べた記録。無いショップを何度も探しに行かないため。</summary>
    public JsonFileStore<List<ShopBannerRecord>> ShopBanners { get; }

    public JsonFileStore<List<ScanCacheEntry>> ScanCache { get; }

    /// <summary>中断した取り込みの記録。最後まで終われば消える。</summary>
    public JsonFileStore<Scanning.ImportState> ImportState { get; }

    /// <summary>編集キューの位置。中断して次回続きから再開するために持つ。</summary>
    public JsonFileStore<EditSession> EditSession { get; }
}
