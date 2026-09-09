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
    /// 今回の保存先。優先順位は 環境変数 &gt; 設定した場所 &gt; 既定
    /// （<see cref="StoreLocation.Resolve"/>）。
    ///
    /// 起動中は変わらない。全サービスが起動時にこれを受け取るので、
    /// 途中で差し替えるには全部を作り直す必要がある。設定からの変更は再起動で効かせる。
    /// </summary>
    public static AppPaths Default { get; } = new(StoreLocation.Resolve().Path);

    public string Root { get; }

    public string ItemsDir => Path.Combine(Root, "items");

    public string ImagesDir => Path.Combine(Root, "images");

    public string UserTagsFile => Path.Combine(Root, "userTags.json");

    public string AttributesFile => Path.Combine(Root, "attributes.json");

    public string AvatarRegistryFile => Path.Combine(Root, "avatar-registry.json");

    public string UnresolvedFile => Path.Combine(Root, "unresolved.json");

    public string ExcludedFile => Path.Combine(Root, "excluded.json");

    /// <summary>「この商品のものではない」と外した記録。取り込みで戻らないようにするために要る。</summary>
    public string DetachedFile => Path.Combine(Root, "detached.json");

    /// <summary>
    /// 検索の橋渡しに使う索引。同梱の辞書から手元で組んだもので、消しても作り直せる。
    /// 保存先に置くのは、辞書を差し替えたときに一緒に作り直せる場所だから。
    /// </summary>
    public string SearchBridgeCacheFile => Path.Combine(Root, "search-bridge.cache");

    public string NotificationsFile => Path.Combine(Root, "notifications.json");

    public string ShopBannersFile => Path.Combine(Root, "shop-banners.json");

    public string EditSessionFile => Path.Combine(Root, "edit-session.json");

    public string ScanCacheFile => Path.Combine(Root, "scan-cache.json");

    /// <summary>中断した取り込みの記録。最後まで終われば消える。</summary>
    public string ImportStateFile => Path.Combine(Root, "import-state.json");

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

    /// <summary>
    /// 手元にあるショップのアイコンを探す。
    ///
    /// 名前にURLのハッシュが入るので、差し替えがあると複数残る。
    /// 出すのは一番新しく取ったもの（古いものは商品画像と同じく消さずに残す）。
    /// 読む側がURLを知らなくて済むので、取得元が増えても表示は変わらない。
    /// </summary>
    public string? FindShopIcon(string subdomain)
    {
        if (!Directory.Exists(ShopIconsDir))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(ShopIconsDir, $"{Sanitize(subdomain)}_*.webp")
            .Where(path => !path.EndsWith("_banner.webp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

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
