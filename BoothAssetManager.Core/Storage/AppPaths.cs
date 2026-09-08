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

    /// <summary>保存先を差し替える環境変数。</summary>
    public const string RootVariable = "BOOTH_ASSET_MANAGER_HOME";

    /// <summary>
    /// 既定の保存先（<c>%LOCALAPPDATA%\BoothAssetManager</c>）。
    ///
    /// <see cref="RootVariable"/> が設定されていればそちらを使う。
    /// 本物のライブラリに触らずに動作を確かめたいときと、
    /// データを別のドライブに置きたいときのための逃げ道。
    /// </summary>
    public static AppPaths Default { get; } = new(ResolveDefaultRoot());

    private static string ResolveDefaultRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootVariable);

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BoothAssetManager")
            : Path.GetFullPath(configured.Trim());
    }

    public string Root { get; }

    public string ItemsDir => Path.Combine(Root, "items");

    public string ImagesDir => Path.Combine(Root, "images");

    public string AppTagsFile => Path.Combine(Root, "appTags.json");

    public string AttributesFile => Path.Combine(Root, "attributes.json");

    public string AvatarRegistryFile => Path.Combine(Root, "avatar-registry.json");

    public string UnresolvedFile => Path.Combine(Root, "unresolved.json");

    public string ExcludedFile => Path.Combine(Root, "excluded.json");

    public string NotificationsFile => Path.Combine(Root, "notifications.json");

    public string ShopBannersFile => Path.Combine(Root, "shop-banners.json");

    public string EditSessionFile => Path.Combine(Root, "edit-session.json");

    public string ScanCacheFile => Path.Combine(Root, "scan-cache.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LockFile => Path.Combine(Root, "app.lock");

    public string ItemFile(string itemId) => Path.Combine(ItemsDir, $"{itemId}.json");

    /// <summary>表示用の説明HTML。検索には使わないので、開いた時だけ読む。</summary>
    public string ItemHtmlFile(string itemId) => Path.Combine(ItemsDir, $"{itemId}.h2.html");

    public string ItemImagesDir(string itemId) => Path.Combine(ImagesDir, itemId);

    /// <summary>
    /// ショップのアイコン置き場。商品IDと衝突しないよう、専用の名前にしてある
    /// （<c>images/</c> 直下は商品IDのフォルダが並ぶ場所なので）。
    /// </summary>
    public string ShopIconsDir => Path.Combine(ImagesDir, "_shops");

    /// <summary>
    /// ショップのアイコン。名前に元URLのハッシュを含める。
    ///
    /// ショップがアイコンを差し替えると商品JSONのURLが変わるので、
    /// 名前も変わって別ファイルになり、次のitem取得で自動的に落とし直される。
    /// 商品画像と同じ考え方（あちらは差し込みで番号がずれるのを避けるため）。
    /// </summary>
    public string ShopIconFile(string subdomain, string? thumbnailUrl)
        => Path.Combine(
            ShopIconsDir,
            thumbnailUrl is null
                ? $"{Sanitize(subdomain)}.webp"
                : $"{Sanitize(subdomain)}_{ShortHash(thumbnailUrl)}.webp");

    /// <summary>元URLから短いハッシュを作る。同じURLなら常に同じ名前になる。</summary>
    private static string ShortHash(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public string ShopBannerFile(string subdomain)
        => Path.Combine(ShopIconsDir, $"{Sanitize(subdomain)}_banner.webp");

    /// <summary>サブドメインはURLの一部なので概ね安全だが、念のためファイル名に使えない字を落とす。</summary>
    private static string Sanitize(string name)
        => string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ItemsDir);
        Directory.CreateDirectory(ImagesDir);
    }
}
