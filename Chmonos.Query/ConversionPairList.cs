using Chmonos.Core.Models;

namespace Chmonos.Query;

/// <summary>組の端1つ。アバター（商品 ID）か共通素体（名前）のどちらか（商品の <c>conversion.targets</c> と同じ持ち方・同じ名前）。</summary>
/// <param name="AvatarId">アバターの商品 ID。素体なら null。</param>
/// <param name="BaseName">共通素体の名前（その仲間全員を指す）。アバターなら null。</param>
public sealed record ConversionEndRef(string? AvatarId, string? BaseName);

/// <summary>人が足した変換の組1つ（<c>/v1/conversion-pairs</c>）。向きは元→先の1方向（元に対応する衣装を、先に着せられる）。</summary>
/// <param name="From">元（衣装の対応アバターの側）。</param>
/// <param name="To">先（変換して着せられるアバターの側）。</param>
/// <param name="Tool">使うツールの名前（無ければ null）。</param>
/// <param name="Memo">人が書いたメモ（無ければ null）。</param>
public sealed record ConversionPairRef(ConversionEndRef From, ConversionEndRef To, string? Tool, string? Memo);

/// <param name="Items">人が足した順（画面の並び「追加が古い順」と同じ、記録の位置の順）。</param>
/// <param name="Total">全部の数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record ConversionPairPage(IReadOnlyList<ConversionPairRef> Items, int Total, string? NextCursor);

/// <summary>
/// 人が足した変換の組（<c>conversion-pairs.json</c>）を読む道（2026-11-27 に出すと決めた物の④。docs/spec/local-api-internal.md）。
/// 読むだけで、足す・外すのは画面に残す。組は商品ではなくアバター・共通素体どうしの事実なので、商品の答えには入れず1本の一覧にする。
/// 記録は写しの <see cref="QuerySnapshot.ConversionPairs"/>（見張りの部分 <see cref="StorePart.ConversionPairs"/>）から取る。
/// 記録のまま全部の行を返す（端が空の行・元と先が同じ行も。計算に使わない行かは、外の道具が端を見て分かる）。
/// </summary>
public static class ConversionPairList
{
    private const string ListName = "conversionPairs";

    public static ConversionPairPage Page(QuerySnapshot snapshot, string? cursor, int? limit)
    {
        var rows = snapshot.ConversionPairs.Select((pair, index) => (Pair: pair, Index: index)).ToList();

        static IReadOnlyList<string> Key((ConversionPair Pair, int Index) row) => [ListPages.Text(row.Index)];
        static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
            => ListPages.Number(left[0]).CompareTo(ListPages.Number(right[0]));

        var (page, next) = ListPages.Take(rows, Key, Compare, ListName, keyLength: 1, cursor, limit);
        return new ConversionPairPage(
            page.Select(row => new ConversionPairRef(End(row.Pair.From), End(row.Pair.To), row.Pair.Tool, row.Pair.Memo)).ToList(),
            rows.Count,
            next);
    }

    private static ConversionEndRef End(ConversionEnd end) => new(end.AvatarItemId, end.BaseName);
}
