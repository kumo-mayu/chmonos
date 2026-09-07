using BoothZipInspector;
using BoothZipInspector.Models;

namespace BoothIdResolver;

public sealed class ResolvedId
{
    public required string ItemId { get; init; }
    public required string Source { get; init; }
}

/// <summary>
/// Zone.IdentifierとZIP内テキストの手掛かりから、BOOTH商品IDを1件に絞り込む純粋ロジック。
/// ネットワーク通信は行わない（cookie不要で確実に分かる範囲のみ）。
/// </summary>
public static class IdResolver
{
    /// <summary>
    /// 既知の依存ツール・共通シェーダーの商品ID。ZIP内の.urlが本体ではなくこれらを指していることがある
    /// (実例: Milfy/WendyのZIP内.urlはlilToon(3087170)を指しており、本体はそれぞれ別ID)。
    /// 本体と誤認しないよう、ZIP内テキスト由来の候補からは除外する。
    /// </summary>
    private static readonly HashSet<string> KnownDependencyItemIds = new() { "3087170" };

    /// <summary>
    /// 優先順位: Zone.Identifier(実際のダウンロード元)＞ZIP内テキストのURL(依存ツールを除く)。
    /// 複数の異なるIDが残った場合は自動選択せず、候補すべてを返す。
    /// </summary>
    public static IReadOnlyList<ResolvedId> Resolve(ZoneIdentifierInfo zone, IReadOnlyList<BoothClue> zipClues)
    {
        if (zone.Found && zone.BoothItemId is not null)
        {
            return new[] { new ResolvedId { ItemId = zone.BoothItemId, Source = "Zone.Identifier" } };
        }

        return zipClues
            .Where(c => c.Kind == BoothClueKind.ItemUrl && c.ItemId is not null)
            .Select(c => c.ItemId!)
            .Where(id => !KnownDependencyItemIds.Contains(id))
            .Distinct()
            .Select(id => new ResolvedId { ItemId = id, Source = "ZIP内テキスト" })
            .ToList();
    }

    public static string ToItemUrl(string itemId) => $"https://booth.pm/ja/items/{itemId}";
}
