using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>変わった行1つの見せ方（要確認の札・商品ページの「消えた行」で共通）。札の文字も種類で分ける（色だけに頼らない）。</summary>
public sealed record ChangedLineRow(string Text, bool IsAdded)
{
    public string Label => IsAdded ? "追加" : "削除";
}

/// <summary>
/// 商品ページの本文1つ（見出しの本文か、見出しの無い商品の説明文）に付ける、変わった行の印。
/// 本文の行の番号で足した行を指し、消えた行は本文の外に並べる
/// </summary>
public sealed class ChangedLineMarks
{
    public static ChangedLineMarks None { get; } = new(new HashSet<int>(), [], string.Empty);

    private ChangedLineMarks(IReadOnlySet<int> addedLines, IReadOnlyList<ChangedLineRow> removed, string removedMoreText)
    {
        AddedLines = addedLines;
        Removed = removed;
        RemovedMoreText = removedMoreText;
    }

    /// <summary>本文を改行で分けたときの、足された行の番号。</summary>
    public IReadOnlySet<int> AddedLines { get; }

    /// <summary>消えた行（先頭から <see cref="ChangedLines.RemovedPreviewLines"/> 行）。</summary>
    public IReadOnlyList<ChangedLineRow> Removed { get; }

    public string RemovedMoreText { get; }

    public bool HasRemoved => Removed.Count > 0;

    public static ChangedLineMarks For(ChangedLines lines, string? text)
    {
        if (!lines.HasAny)
        {
            return None;
        }

        var (removed, more) = lines.RemovedForPage();
        return new ChangedLineMarks(lines.AddedLineIndexes(text), removed, more);
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

    /// <summary>商品ページの見出しの下に並べる消えた行の数（本文の前に置くので、本文より目立たせない）。</summary>
    public const int RemovedPreviewLines = 3;

    public static ChangedLines None { get; } = new([], 0, 0, false);

    private ChangedLines(IReadOnlyList<NotificationLine> lines, int moreAdded, int moreRemoved, bool wholeAdded)
    {
        Lines = lines;
        MoreAdded = moreAdded;
        MoreRemoved = moreRemoved;
        WholeAdded = wholeAdded;
    }

    public IReadOnlyList<NotificationLine> Lines { get; }

    public int MoreAdded { get; }

    public int MoreRemoved { get; }

    /// <summary>見出しごと足された（前が無い）。本文の行は全部が新しい。</summary>
    public bool WholeAdded { get; }

    public bool HasAny => Lines.Count > 0 || MoreAdded > 0 || MoreRemoved > 0;

    public static ChangedLines From(NotificationDiff diff)
        => diff.Lines is not { Count: > 0 } lines && diff.MoreAdded is null && diff.MoreRemoved is null
            ? None
            : new(diff.Lines ?? [], diff.MoreAdded ?? 0, diff.MoreRemoved ?? 0, diff.Before is null && diff.After is not null);

    /// <summary>札に並べる行（先頭から <see cref="CardLines"/> 行）と、残りの「ほか n 行」。</summary>
    public (IReadOnlyList<ChangedLineRow> Rows, string MoreText) ForCard()
    {
        var rows = Lines.Take(CardLines).Select(Row).ToList();
        return (rows, More(Lines.Count - rows.Count + MoreAdded + MoreRemoved));
    }

    /// <summary>消えた行（本文には無いので、見出しのすぐ下に出す）と、残りの「ほか n 行」。</summary>
    public (IReadOnlyList<ChangedLineRow> Rows, string MoreText) RemovedForPage()
    {
        var removed = Lines.Where(line => line.Kind == NotificationLineKind.Removed).ToList();
        var rows = removed.Take(RemovedPreviewLines).Select(Row).ToList();
        return (rows, More(removed.Count - rows.Count + MoreRemoved));
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
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
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

    /// <summary>長い行は差の側で切ってある（<see cref="LineDiff.MaxLineLength"/>）ので、切った所までで比べる。</summary>
    private static bool Matches(string line, string stored)
        => stored.Length == LineDiff.MaxLineLength + 1 && stored.EndsWith('…')
            ? line.StartsWith(stored[..^1], StringComparison.Ordinal)
            : string.Equals(line, stored, StringComparison.Ordinal);

    private static ChangedLineRow Row(NotificationLine line) => new(line.Text, line.Kind == NotificationLineKind.Added);

    private static string More(int count) => count > 0 ? $"ほか {count} 行" : string.Empty;
}
