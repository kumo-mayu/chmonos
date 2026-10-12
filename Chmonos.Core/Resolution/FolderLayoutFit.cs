using Chmonos.Core.Models;

namespace Chmonos.Core.Resolution;

/// <summary>
/// 置き方の指定が、既に商品に結び付いたファイルでどれだけ当てはまるか（<c>docs/spec/folder-layout.md</c>「合っているかを見せる」）。
/// 合わない置き方をオンにしたまま気付かない事故を防ぐため、設定の行に出す。読むだけの計算。
/// </summary>
public sealed record FolderLayoutFit(int Files, int ItemNameMatches, int ShopNameMatches)
{
    /// <summary>
    /// 指定の下にある、商品に結び付いたファイル（外した物を除く）を数え、商品名の段とショップ名の段が合っている数を返す。
    /// オフの指定でも数える（オンにする前に確かめられるように）。重なる深い指定の下のファイルも数える（行ごとの目安なので）
    /// </summary>
    public static FolderLayoutFit Measure(IEnumerable<ItemRecord> items, FolderLayoutRule rule)
    {
        var enabled = rule with { Enabled = true };
        var files = 0;
        var itemMatches = 0;
        var shopMatches = 0;
        foreach (var item in items)
        {
            if (item.IsLocalOnly)
            {
                continue;
            }

            foreach (var path in item.Local.OwnedFiles.SelectMany(file => file.Paths).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!FolderLayoutHint.IsUnder(path, rule.Root))
                {
                    continue;
                }

                // 段の足りない浅い所のファイルも数える（数えないと、置き方に合わないファイルが隠れて、実際より合って見えた。点検33）
                files++;
                if (FolderLayoutHint.For(path, [enabled]) is not { } hint)
                {
                    continue;
                }

                itemMatches += SaleDecorations.SameName(item.Booth.Name, hint.ItemFolder, item.Booth.Shop?.Name) ? 1 : 0;
                shopMatches += SaleDecorations.SameShop(item.Booth.Shop?.Name, item.Booth.Shop?.Subdomain, hint.ShopFolder) ? 1 : 0;
            }
        }

        return new FolderLayoutFit(files, itemMatches, shopMatches);
    }
}
