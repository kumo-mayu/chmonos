using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 「このファイルはこの商品のものではない」の一覧を引く。
///
/// 外した印（<see cref="LocalFileRecord.Detached"/>）は商品のJSONの中にあるので、全商品から集める
/// （以前は別の detached.json に持っていた）。
///
/// 除外（<see cref="ExclusionFilter"/>）と違い、ファイルを管理から外すわけではない。
/// **その商品への紐付けだけ**を止める。他の商品には自由に紐付いてよい。
/// </summary>
public sealed class DetachedIndex
{
    private readonly HashSet<(string Hash, string ItemId)> _pairs;

    private DetachedIndex(HashSet<(string, string)> pairs) => _pairs = pairs;

    public static DetachedIndex From(IEnumerable<ItemRecord> items)
    {
        var pairs = new HashSet<(string, string)>();

        foreach (var item in items)
        {
            foreach (var file in item.Local.LocalFiles.Where(file => file.Detached))
            {
                pairs.Add((file.Hash.ToUpperInvariant(), item.Id));
            }
        }

        return new DetachedIndex(pairs);
    }

    public bool IsDetached(string hash, string itemId) => _pairs.Contains((hash.ToUpperInvariant(), itemId));

    public bool IsEmpty => _pairs.Count == 0;
}
