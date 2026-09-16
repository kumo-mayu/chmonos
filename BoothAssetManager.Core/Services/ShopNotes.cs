using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>ショップの星とメモ（<see cref="ShopNoteRecord"/>）の読み書きの決まり。</summary>
public static class ShopNotes
{
    /// <summary>そのショップの記録。無ければ null。</summary>
    public static ShopNoteRecord? Of(IEnumerable<ShopNoteRecord> records, string subdomain)
        => records.FirstOrDefault(record => string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

    /// <summary>お気に入りのショップの鍵（大文字小文字を区別しない）。</summary>
    public static IReadOnlySet<string> FavoriteKeys(IEnumerable<ShopNoteRecord> records)
        => records.Where(record => record.IsFavorite)
            .Select(record => record.Subdomain)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 1店ぶんを変える。無ければ作り、星もメモも無くなったら落とす。
    /// 名前の控えと ID は、書くたびに今の値で上書きする（名前が変わっていれば新しい名前で見分けられる）。
    /// 並びは鍵の順にそろえる（ファイルを見比べたときに、変えた所だけが差になる）。
    /// </summary>
    public static List<ShopNoteRecord> Apply(
        IEnumerable<ShopNoteRecord> records,
        string subdomain,
        string? nameHint,
        string? uuid,
        Func<ShopNoteRecord, ShopNoteRecord> change,
        DateTimeOffset now)
    {
        var others = records
            .Where(record => !string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var current = Of(records, subdomain) ?? new ShopNoteRecord { Subdomain = subdomain };
        var changed = change(current) with
        {
            Subdomain = current.Subdomain,
            NameHint = string.IsNullOrWhiteSpace(nameHint) ? current.NameHint : nameHint,
            Uuid = string.IsNullOrWhiteSpace(uuid) ? current.Uuid : uuid,
            UpdatedAt = now,
        };

        if (!changed.IsEmpty)
        {
            others.Add(changed);
        }

        return others.OrderBy(record => record.Subdomain, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
