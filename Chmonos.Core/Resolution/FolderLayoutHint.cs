using Chmonos.Core.Models;

namespace Chmonos.Core.Resolution;

/// <summary>
/// 置き方の指定（<see cref="FolderLayoutRule"/>）から読んだ、1つのファイルのフォルダ名（<c>docs/spec/folder-layout.md</c>）。
/// <see cref="ShopSubdomain"/> は手元の商品から引けたときだけ入る（ショップの商品一覧を取るのに要る）。
/// </summary>
public sealed record FolderLayoutHint(string? ItemFolder, string? ShopFolder, string? ShopSubdomain = null)
{
    /// <summary>
    /// ファイルに効く指定を探し、その段のフォルダ名を返す。効く指定が無い・段のフォルダが無いなら null。
    /// 指定が重なるときは、いちばん深いフォルダの指定を使う（全体に1つ指定して、一部だけ別の並べ方にできるように）。
    /// オフの指定もいちばん深ければ選ばれ、そのファイルには何も効かない（上の指定に落ちない。止めたつもりの所で働かないように）
    /// </summary>
    public static FolderLayoutHint? For(string filePath, IReadOnlyList<FolderLayoutRule> rules)
    {
        var rule = rules
            .Where(rule => rule.IsUsable && IsUnder(filePath, rule.Root))
            .OrderByDescending(rule => rule.Root.TrimEnd('\\', '/').Length)
            .FirstOrDefault();
        if (rule is null || !rule.Enabled)
        {
            return null;
        }

        var root = rule.Root.TrimEnd('\\', '/');
        var item = FolderAt(filePath, rule.ItemNameLevel, root);
        var shop = FolderAt(filePath, rule.ShopNameLevel, root);
        return item is null && shop is null ? null : new FolderLayoutHint(item, shop);
    }

    /// <summary>
    /// ファイルの手掛かりを、手元の商品でショップのサブドメインまで埋めて返す（ショップの商品一覧を取るのに要る）。
    /// ショップ名のフォルダと同じ名前のショップの商品を手元で探す。無ければサブドメインは空のまま（一覧の段は飛ぶ）
    /// </summary>
    public static FolderLayoutHint? For(string filePath, IReadOnlyList<FolderLayoutRule> rules, IEnumerable<ItemRecord> items)
    {
        var hint = For(filePath, rules);
        if (hint?.ShopFolder is null)
        {
            return hint;
        }

        var subdomain = items
            .Select(item => item.Booth.Shop)
            .FirstOrDefault(shop => shop?.Subdomain is { Length: > 0 } && SaleDecorations.SameShop(shop.Name, shop.Subdomain, hint.ShopFolder))
            ?.Subdomain;
        return hint with { ShopSubdomain = subdomain };
    }

    /// <summary>ファイルから見て <paramref name="level"/> 段上のフォルダの名前。指定のフォルダより上に出るなら null（その段は置き方の外）。</summary>
    private static string? FolderAt(string filePath, int? level, string root)
    {
        if (level is not (>= 1 and <= FolderLayoutRule.MaxLevel))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(filePath);
        for (var up = 1; up < level && directory is not null; up++)
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory is null || directory.TrimEnd('\\', '/').Length <= root.Length)
        {
            return null;
        }

        var name = Path.GetFileName(directory.TrimEnd('\\', '/'));
        return name.Length == 0 ? null : name;
    }

    /// <summary>フォルダの下にあるか（大文字小文字を区別しない。Windows のパスなので）。</summary>
    public static bool IsUnder(string filePath, string root)
    {
        var trimmed = root.TrimEnd('\\', '/');
        return trimmed.Length > 0
            && filePath.Length > trimmed.Length + 1
            && filePath.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            && filePath[trimmed.Length] is '\\' or '/';
    }
}
