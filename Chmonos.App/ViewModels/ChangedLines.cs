using Chmonos.App.Controls;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>変わった行1つの見せ方（要確認の札）。札の文字も種類で分ける（色だけに頼らない）。</summary>
public sealed record ChangedLineRow(string Text, bool IsAdded)
{
    public string Label => IsAdded ? "追加" : "削除";
}

/// <summary>
/// 商品ページの本文1つ（見出しの本文か、見出しの無い商品の説明文）に付ける、変わった行の印。
///
/// 足した行も消えた行も、本文の上の帯（地と左の線）で示す（メモ17・ユーザ指示 2026-10-03
/// 「追加が緑色の背景帯なのに削除が先頭チップなのは違和感。背景帯に統一」）。前は消えた行を見出しの下に「削除」の札付きで並べていた。
/// 消えた行は本文に無いので、元の位置（今の本文のどの行の前か）へ差し込む
/// </summary>
public sealed class ChangedLineMarks : ILineMarks
{
    public static ChangedLineMarks None { get; } = new(new HashSet<int>(), [], string.Empty);

    private ChangedLineMarks(IReadOnlySet<int> addedLines, IReadOnlyList<InsertedLine> removed, string removedMoreText)
    {
        AddedLines = addedLines;
        RemovedLines = removed;
        RemovedMoreText = removedMoreText;
    }

    /// <summary>本文を改行で分けたときの、足された行の番号。</summary>
    public IReadOnlySet<int> AddedLines { get; }

    /// <summary>元の位置に差し込む消えた行（知らせに残した分は全部）。</summary>
    public IReadOnlyList<InsertedLine> RemovedLines { get; }

    /// <summary>知らせに残さなかった消えた行の数の1行（上限を超えた分。文が無いので並べられない）。無ければ空。</summary>
    public string RemovedMoreText { get; }

    public bool HasRemovedMore => RemovedMoreText.Length > 0;

    public static ChangedLineMarks For(ChangedLines lines, string? text)
    {
        if (!lines.HasAny)
        {
            return None;
        }

        return new ChangedLineMarks(
            lines.AddedLineIndexes(text),
            lines.RemovedSpots(text),
            lines.MoreRemoved > 0 ? $"ほかに消えた行が {lines.MoreRemoved} 行あります。" : string.Empty);
    }
}

/// <summary>
/// 説明文の見出し1つの、変わった行（メモ13-②・ユーザ指示 2026-10-02「変更差分がある行だけでもわかるように」）。
///
/// 差は知らせを作るときに Core が作る（<see cref="NotificationDiff.Lines"/>）。この形より前に作った知らせは行を持たないので、
/// そのときは何も出さず、今までどおり頭の抜き出し（前 → 後）だけを出す——読み替えではなく、無い物を出さないだけ
/// </summary>
public sealed class ChangedLines
{
    /// <summary>要確認の札に並べる行の数。見出しを丸ごと足した・消したときに札が縦に伸びすぎないように。</summary>
    public const int CardLines = 4;

    public static ChangedLines None { get; } = new([], 0, 0, false, false);

    private ChangedLines(IReadOnlyList<NotificationLine> lines, int moreAdded, int moreRemoved, bool wholeAdded, bool wholeRemoved)
    {
        Lines = lines;
        MoreAdded = moreAdded;
        MoreRemoved = moreRemoved;
        WholeAdded = wholeAdded;
        WholeRemoved = wholeRemoved;
    }

    public IReadOnlyList<NotificationLine> Lines { get; }

    public int MoreAdded { get; }

    public int MoreRemoved { get; }

    /// <summary>見出しごと足された（前が無い）。本文の行は全部が新しい。</summary>
    public bool WholeAdded { get; }

    /// <summary>見出しごと消えた（後が無い）。</summary>
    public bool WholeRemoved { get; }

    public bool HasAny => Lines.Count > 0 || MoreAdded > 0 || MoreRemoved > 0;

    public static ChangedLines From(NotificationDiff diff)
        => diff.Lines is not { Count: > 0 } lines && diff.MoreAdded is null && diff.MoreRemoved is null
            ? None
            : new(
                diff.Lines ?? [],
                diff.MoreAdded ?? 0,
                diff.MoreRemoved ?? 0,
                diff.Before is null && diff.After is not null,
                diff.Before is not null && diff.After is null);

    /// <summary>札に並べる行（先頭から <see cref="CardLines"/> 行）と、残りの「ほか n 行」。</summary>
    public (IReadOnlyList<ChangedLineRow> Rows, string MoreText) ForCard()
    {
        var rows = Lines.Take(CardLines).Select(Row).ToList();
        return (rows, More(Lines.Count - rows.Count + MoreAdded + MoreRemoved));
    }

    /// <summary>
    /// 今の本文のうち、足された行の番号（本文を改行で分けたときの番号）。
    ///
    /// 差は行の中身しか持たないので、本文を上から辿り、足された行を差の並びの順に1つずつ当てる
    /// （同じ文の行が2つあっても、足した方より前のもう1つに印が付きにくい）。
    /// 見出しごと足された物は、空でない行を全部印にする（上限で残さなかった行にも付くように）
    /// </summary>
    public IReadOnlySet<int> AddedLineIndexes(string? text)
    {
        var result = new HashSet<int>();
        var lines = Split(text);
        if (WholeAdded)
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (LineDiff.NormalizeLine(lines[index]).Length > 0)
                {
                    result.Add(index);
                }
            }

            return result;
        }

        var added = Lines.Where(line => line.Kind == NotificationLineKind.Added).Select(line => line.Text).ToList();
        var next = 0;
        for (var index = 0; index < lines.Length && next < added.Count; index++)
        {
            if (Matches(LineDiff.NormalizeLine(lines[index]), added[next]))
            {
                result.Add(index);
                next++;
            }
        }

        return result;
    }

    /// <summary>消えた行を、今の本文のどの行の前へ差し込むか（<see cref="Place{T}"/>）。</summary>
    public IReadOnlyList<InsertedLine> RemovedSpots(string? text)
    {
        var current = Split(text).Select(LineDiff.NormalizeLine).ToList();

        // 差は今の本文の順に並んでいるので、手前の足した行を当てた所より前は探さない（同じ文の行が前にもあるとき、そちらに付かないように）。
        // 足した行そのものが直前の行のこともあるので、その行から探す
        var removed = new List<(string, string?, string, int)>();
        var position = 0;
        foreach (var line in Lines)
        {
            if (line.Kind == NotificationLineKind.Removed)
            {
                removed.Add((line.Text, line.Follows, line.Text, Math.Max(position - 1, 0)));
                continue;
            }

            for (var index = position; index < current.Count; index++)
            {
                if (Matches(current[index], line.Text))
                {
                    position = index + 1;
                    break;
                }
            }
        }

        return Place(current, removed).Select(spot => new InsertedLine(spot.Before, spot.Item)).ToList();
    }

    /// <summary>
    /// 消えた物（行・見出し・バリエーション）を、今の並びのどこへ差し込むかを決める。<paramref name="current"/> は今の並びの文（比べる形に詰めた物）。
    ///
    /// 消えた物は「今の並びで直前にあった物の文」（<see cref="NotificationLine.Follows"/>）を持つので、その文を今の並びで探し、すぐ後ろに置く。
    /// 同じ文が2つあれば、前に置いた所から先を先に探す（差は今の並びの順に並んでいる）。
    /// 未読のうちに重ねた知らせでは、直前の物がその後で消えていることがある：そのときは、その消えた物のすぐ後ろに置く。
    /// どちらにも無い（手で直した JSON など）ときは、前に置いた物の所、それも無ければ先頭
    /// </summary>
    /// <returns>差し込む位置（今の並びの何番目の前か。並びの数なら末尾）の小さい順。同じ位置は差の順。</returns>
    internal static IReadOnlyList<(int Before, T Item)> Place<T>(IReadOnlyList<string> current, IReadOnlyList<(string Text, string? Follows, T Item)> removed)
        => Place(current, removed.Select(entry => (entry.Text, entry.Follows, entry.Item, 0)).ToList());

    /// <param name="removed">消えた物と、直前の物を探し始める位置（それより前は後回しに探す）。</param>
    internal static IReadOnlyList<(int Before, T Item)> Place<T>(
        IReadOnlyList<string> current, IReadOnlyList<(string Text, string? Follows, T Item, int From)> removed)
    {
        // 位置ごとの並び（消えた物の番号を並べる順に）。1段目で今の並びに手掛かりのある物を置き、
        // 2段目で「直前の物も消えた物」をその物の後ろへ足す（重ねた知らせでは、後で消えた物が差の後ろに来るので、1段で決めると順が逆になる）
        var slots = new SortedDictionary<int, List<int>>();
        var slotOf = new int?[removed.Count];
        void Put(int index, int before, int? after = null)
        {
            if (!slots.TryGetValue(before, out var list))
            {
                slots[before] = list = [];
            }

            // 直前の物の後ろ。同じ物の後ろに続く物が既にあれば、その後ろ（差の順を保つ）
            var at = list.Count;
            if (after is { } anchor)
            {
                at = list.IndexOf(anchor) + 1;
                while (at < list.Count && string.Equals(removed[list[at]].Follows, removed[anchor].Text, StringComparison.Ordinal))
                {
                    at++;
                }
            }

            list.Insert(at, index);
            slotOf[index] = before;
        }

        var cursor = 0;
        for (var index = 0; index < removed.Count; index++)
        {
            var (_, follows, _, from) = removed[index];
            if (follows is null)
            {
                Put(index, 0);
            }
            else if (Find(current, follows, Math.Max(cursor, from)) is { } found)
            {
                Put(index, found + 1);
                cursor = found;
            }
        }

        for (var progressed = true; progressed;)
        {
            progressed = false;
            for (var index = 0; index < removed.Count; index++)
            {
                if (slotOf[index] is not null || removed[index].Follows is not { } follows)
                {
                    continue;
                }

                var anchor = Enumerable.Range(0, removed.Count)
                    .LastOrDefault(other => other != index && slotOf[other] is not null
                        && string.Equals(removed[other].Text, follows, StringComparison.Ordinal), -1);
                if (anchor >= 0)
                {
                    Put(index, slotOf[anchor]!.Value, anchor);
                    progressed = true;
                }
            }
        }

        // どこにも手掛かりの無い物（手で直した JSON など）は、差の順で1つ前の物の所、それも無ければ先頭
        for (var index = 0; index < removed.Count; index++)
        {
            if (slotOf[index] is null)
            {
                Put(index, index > 0 && slotOf[index - 1] is { } previous ? previous : 0);
            }
        }

        return slots.SelectMany(slot => slot.Value.Select(index => (slot.Key, removed[index].Item))).ToList();
    }

    private static int? Find(IReadOnlyList<string> current, string follows, int from)
    {
        for (var index = from; index < current.Count; index++)
        {
            if (Matches(current[index], follows))
            {
                return index;
            }
        }

        for (var index = 0; index < Math.Min(from, current.Count); index++)
        {
            if (Matches(current[index], follows))
            {
                return index;
            }
        }

        return null;
    }

    private static string[] Split(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

    /// <summary>長い行は差の側で切ってある（<see cref="LineDiff.MaxLineLength"/>）ので、切った所までで比べる。</summary>
    internal static bool Matches(string line, string stored)
        => stored.Length == LineDiff.MaxLineLength + 1 && stored.EndsWith('…')
            ? line.StartsWith(stored[..^1], StringComparison.Ordinal)
            : string.Equals(line, stored, StringComparison.Ordinal);

    private static ChangedLineRow Row(NotificationLine line) => new(line.Text, line.Kind == NotificationLineKind.Added);

    private static string More(int count) => count > 0 ? $"ほか {count} 行" : string.Empty;
}
