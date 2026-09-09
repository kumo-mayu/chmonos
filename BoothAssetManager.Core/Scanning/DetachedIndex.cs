using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 「このファイルはこの商品のものではない」の一覧を引く。
///
/// 除外（<see cref="ExclusionFilter"/>）と違い、ファイルを管理から外すわけではない。
/// **その商品への紐付けだけ**を止める。他の商品には自由に紐付いてよい。
/// </summary>
public sealed class DetachedIndex
{
    private readonly HashSet<(string Hash, string ItemId)> _pairs;

    private DetachedIndex(HashSet<(string, string)> pairs) => _pairs = pairs;

    public static DetachedIndex From(IEnumerable<DetachedFile>? entries)
    {
        var pairs = new HashSet<(string, string)>();
        if (entries is null)
        {
            return new DetachedIndex(pairs);
        }

        foreach (var entry in entries)
        {
            pairs.Add((entry.Hash.ToUpperInvariant(), entry.ItemId));
        }

        return new DetachedIndex(pairs);
    }

    public bool IsDetached(string hash, string itemId) => _pairs.Contains((hash.ToUpperInvariant(), itemId));

    public bool IsEmpty => _pairs.Count == 0;
}
