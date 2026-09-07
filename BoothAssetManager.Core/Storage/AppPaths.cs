namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 保存先の解決。データは全てただのJSONファイルとして置き、ユーザが直接開けるようにする。
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    /// <summary>既定の保存先（<c>%LOCALAPPDATA%\BoothAssetManager</c>）。</summary>
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BoothAssetManager"));

    public string Root { get; }

    public string ItemsDir => Path.Combine(Root, "items");

    public string ImagesDir => Path.Combine(Root, "images");

    public string AppTagsFile => Path.Combine(Root, "appTags.json");

    public string AttributesFile => Path.Combine(Root, "attributes.json");

    public string AvatarRegistryFile => Path.Combine(Root, "avatar-registry.json");

    public string UnresolvedFile => Path.Combine(Root, "unresolved.json");

    public string ExcludedFile => Path.Combine(Root, "excluded.json");

    public string NotificationsFile => Path.Combine(Root, "notifications.json");

    public string EditSessionFile => Path.Combine(Root, "edit-session.json");

    public string ScanCacheFile => Path.Combine(Root, "scan-cache.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LockFile => Path.Combine(Root, "app.lock");

    public string ItemFile(string itemId) => Path.Combine(ItemsDir, $"{itemId}.json");

    /// <summary>表示用の説明HTML。検索には使わないので、開いた時だけ読む。</summary>
    public string ItemHtmlFile(string itemId) => Path.Combine(ItemsDir, $"{itemId}.h2.html");

    public string ItemImagesDir(string itemId) => Path.Combine(ImagesDir, itemId);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ItemsDir);
        Directory.CreateDirectory(ImagesDir);
    }
}
