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

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 最新を読み、変えて書く。**書き手が複数いるファイル**（登録簿など）はこちらを通す。
    ///
    /// 読んでから書くまでの間に別の書き手が入ると、後から書いた方が先の変更を消す。
    /// 対応アバターの検出は始めに読んで数十分後に書くので、その間にアバター画面で保存した名前が
    /// 検出の終わりに消えていた（U15、友人データの初回で約37分）。
    /// 錠はこの窓口1つにつき1本。アプリは <see cref="DataStore"/> を1つだけ持つので、書き手どうしは重ならない。
    /// </summary>
    /// <returns>書いた値。</returns>
    public async Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updated = change(Load());
            await SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }
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
        SearchHistory = new JsonFileStore<Services.SearchHistoryList>(paths.SearchHistoryFile);
        Recent = new JsonFileStore<Services.RecentLog>(paths.RecentFile);
        Modifications = new ModificationRepository(paths);
        ShopBanners = new JsonFileStore<List<ShopBannerRecord>>(paths.ShopBannersFile);
        ScanCache = new JsonFileStore<List<ScanCacheEntry>>(paths.ScanCacheFile);
        ImportState = new JsonFileStore<Scanning.ImportState>(paths.ImportStateFile);
        EditSession = new JsonFileStore<EditSession>(paths.EditSessionFile);
        Volumes = new JsonFileStore<List<VolumeRecord>>(paths.VolumesFile);
        UiState = new JsonFileStore<UiState>(paths.UiStateFile);
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

    /// <summary>検索の履歴。商品を開いたときに1件積む</summary>
    public JsonFileStore<Services.SearchHistoryList> SearchHistory { get; }

    /// <summary>「最近」の足跡（追加・使った・閲覧）</summary>
    public JsonFileStore<Services.RecentLog> Recent { get; }

    /// <summary>改変の記録。1改変1ファイル</summary>
    public ModificationRepository Modifications { get; }

    /// <summary>ショップのバナーを調べた記録。無いショップを何度も探しに行かないため。</summary>
    public JsonFileStore<List<ShopBannerRecord>> ShopBanners { get; }

    public JsonFileStore<List<ScanCacheEntry>> ScanCache { get; }

    /// <summary>中断した取り込みの記録。最後まで終われば消える。</summary>
    public JsonFileStore<Scanning.ImportState> ImportState { get; }

    /// <summary>編集キューの位置。中断して次回続きから再開するために持つ。</summary>
    public JsonFileStore<EditSession> EditSession { get; }

    /// <summary>ドライブ文字と通し番号の組（<see cref="Services.VolumeTable"/>）。</summary>
    public JsonFileStore<List<VolumeRecord>> Volumes { get; }

    /// <summary>画面が覚えている状態。設定とは別のファイル（技術的負債 3-2）。</summary>
    public JsonFileStore<UiState> UiState { get; }
}
