namespace BoothAssetManager.Core.Storage;

/// <summary>
/// ショップのアイコン置き場を1回だけ列挙して引く表。
///
/// ショップ一覧の集計は店ごとに <see cref="AppPaths.FindShopIcon"/> を呼んでいて、
/// 呼ぶたびに置き場を列挙し、見つかったファイルの日時も1枚ずつ問い合わせていた。
/// 「店の数×置き場の中の数」で2乗に伸び、500店・1000枚で集計が 372ms かかった（表にして 0.84ms。
/// <c>docs/research/memory-budget.md</c> 2026-09-29）。1店だけを見る所は <see cref="AppPaths.FindShopIcon"/> のままでよい。
///
/// 名前の最後の <c>_</c> より前をサブドメインとみなす。<c>{sub}_*.webp</c> の型は <c>sub_x</c> という
/// 別の店のアイコンも拾う作りだったが、サブドメインに <c>_</c> は入らないので答えは変わらず、表の方が正しい。
/// </summary>
public sealed class ShopIconIndex
{
    private readonly string _directory;

    /// <summary>サブドメイン → 一番新しいアイコンのファイル名と日時。</summary>
    private readonly Dictionary<string, (string Name, DateTime WrittenUtc)> _icons = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _banners = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="directory">置き場。返すパスはここにファイル名をつないだ物（<see cref="AppPaths.FindShopIcon"/> と同じ形）。</param>
    /// <param name="files">置き場の中のファイル名と更新日時。日時は列挙で得た物を渡す（1枚ずつ問い合わせると表にした意味が薄れる）。</param>
    public ShopIconIndex(string directory, IEnumerable<(string Name, DateTime WrittenUtc)> files)
    {
        _directory = directory;

        foreach (var (name, writtenUtc) in files)
        {
            if (!name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stem = name[..^".webp".Length];
            var cut = stem.LastIndexOf('_');

            // 区切りの無い名前（URL が分からなかったときの {sub}.webp）は、今の探し方でも拾わない
            if (cut <= 0)
            {
                continue;
            }

            var subdomain = stem[..cut];

            if (string.Equals(stem[(cut + 1)..], "banner", StringComparison.OrdinalIgnoreCase))
            {
                _banners.Add(subdomain);
                continue;
            }

            // 日時が同じなら先に見えた方を残す。今の探し方（列挙の順に並べ替えて先頭）と同じ答えにするため
            if (!_icons.TryGetValue(subdomain, out var current) || writtenUtc > current.WrittenUtc)
            {
                _icons[subdomain] = (name, writtenUtc);
            }
        }
    }

    /// <summary>置き場を1回だけ列挙して表を作る。置き場が無ければ空の表。</summary>
    public static ShopIconIndex Read(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            return new ShopIconIndex(directory, []);
        }

        return new ShopIconIndex(
            directory,
            info.EnumerateFiles("*.webp").Select(file => (file.Name, file.LastWriteTimeUtc)));
    }

    /// <summary>手元にある、そのショップの一番新しいアイコン。無ければ null。</summary>
    public string? FindIcon(string subdomain)
        => _icons.TryGetValue(AppPaths.Sanitize(subdomain), out var icon)
            ? Path.Combine(_directory, icon.Name)
            : null;

    /// <summary>そのショップのバナーが手元にあるか。</summary>
    public bool HasBanner(string subdomain) => _banners.Contains(AppPaths.Sanitize(subdomain));
}
