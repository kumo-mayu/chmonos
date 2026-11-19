using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 人が足した変換の組（<c>conversion-pairs.json</c>）の変え方。どれも錠の中で今の値に当てる関数として使う
/// （<c>UiCommand.ChangeConversionPairs</c>・ID の変更・素体の改名と削除）。
/// </summary>
public static class ConversionPairs
{
    /// <summary>組を足す。同じ元と先の組が既にあれば足さない（ツールとメモが違っても重ねない）。</summary>
    public static ConversionPairFile Add(ConversionPairFile file, ConversionPair pair)
        => pair.IsEmpty || pair.PointsToItself || file.Pairs.Any(known => known.SameEnds(pair))
            ? file
            : file with { Pairs = [.. file.Pairs, pair] };

    /// <summary>同じ元と先の組を外す。</summary>
    public static ConversionPairFile Remove(ConversionPairFile file, ConversionPair pair)
        => file with { Pairs = [.. file.Pairs.Where(known => !known.SameEnds(pair))] };

    /// <summary>
    /// 同じ元と先の組のメモを書き替える（ツールは残す）。空白だけのメモは持たない（null。ファイルに空の欄を書かない）。
    /// 組が既に外されていたら何もしない（メモの待ちの間に別の所で外した組を、メモだけで生き返らせない）
    /// </summary>
    public static ConversionPairFile SetMemo(ConversionPairFile file, ConversionPair pair, string? memo)
    {
        var text = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        return file.Pairs.Any(known => known.SameEnds(pair) && known.Memo != text)
            ? file with { Pairs = [.. file.Pairs.Select(known => known.SameEnds(pair) ? known with { Memo = text } : known)] }
            : file;
    }

    /// <summary>
    /// 商品 ID を変えた・まとめたときの読み替え。変わらなければ null（書かない）。
    /// 読み替えで元と先が同じになった組と、前の組と重なった組は落とす（統合で同じ組が2つになる）
    /// </summary>
    public static ConversionPairFile? RenameItem(ConversionPairFile file, string fromId, string toId)
        => Rewrite(file, end => string.Equals(end.AvatarItemId, fromId, StringComparison.Ordinal) ? ConversionEnd.Avatar(toId) : end);

    /// <summary>
    /// 素体の改名（<paramref name="newName"/> あり）と削除（null）の読み替え。変わらなければ null。
    /// 消した素体を指す組は落とす（つなぎ先の無い組は持たない。変換扱いの行と同じ）
    /// </summary>
    public static ConversionPairFile? RenameBase(ConversionPairFile file, string oldName, string? newName)
        => Rewrite(file, end => end.BaseName is { } name && AvatarBaseNames.Comparer.Equals(name, oldName)
            ? newName is null ? null : ConversionEnd.Base(newName)
            : end);

    private static ConversionPairFile? Rewrite(ConversionPairFile file, Func<ConversionEnd, ConversionEnd?> map)
    {
        var changed = false;
        var kept = new List<ConversionPair>();
        foreach (var pair in file.Pairs)
        {
            var from = map(pair.From);
            var to = map(pair.To);
            if (!ReferenceEquals(from, pair.From) || !ReferenceEquals(to, pair.To))
            {
                changed = true;
            }

            if (from is null || to is null)
            {
                continue;
            }

            var next = ReferenceEquals(from, pair.From) && ReferenceEquals(to, pair.To) ? pair : pair with { From = from, To = to };
            if (next.PointsToItself || kept.Any(known => known.SameEnds(next)))
            {
                changed = true;
                continue;
            }

            kept.Add(next);
        }

        return changed ? file with { Pairs = kept } : null;
    }
}
