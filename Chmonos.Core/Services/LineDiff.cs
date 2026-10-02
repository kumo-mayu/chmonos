using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 説明文の見出し1つぶんの、前と後の本文から**変わった行だけ**を取り出す（メモ13-②・ユーザ指示 2026-10-02）。
///
/// 前は前後の頭から70字だけを持っていて、変わったのが見出しの後ろの方だと抜き出しが同じになり、何が変わったか読めなかった。
/// 行を単位にするのは、BOOTH の説明文が改行で区切った箇条書き・版ごとの1行で書かれることが多く、
/// 「足した版の行」「消した注意書きの行」がそのまま読める単位だから。
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// 知らせに残す行の上限。要確認は200件まで残すので、見出しを丸ごと足した・消したときに本文を全部写すと
    /// <c>notifications.json</c> が膨らむ。画面に出すのは数行なので、見せる分より十分に多ければ足りる。
    /// 超えた分は数だけ残す（<see cref="NotificationDiff.MoreAdded"/>・<see cref="NotificationDiff.MoreRemoved"/>）。上限は足した行・消した行のそれぞれに当てる
    /// </summary>
    public const int MaxLines = 30;

    /// <summary>1行の上限の字数。改行の無い長い段落が1行になる商品があり、そのまま写すと1行で数KBになる。</summary>
    public const int MaxLineLength = 300;

    /// <summary>
    /// 前後の行を突き合わせる表（行の数の積）の上限。これを超える本文は、並びを見ずに「相手に無い行」で分ける。
    /// 千行×千行で表は約4MB。説明文の見出し1つでここまで長い物は無いはずで、来ても固まらないための止め
    /// </summary>
    private const long MaxTableCells = 1_000_000;

    /// <summary>
    /// 変わった行を、後の本文の並びに沿って返す（同じ所で消えた行は足した行の前）。同じ本文なら空。
    /// 行の中の空白の違い（全角・連続・前後）は差と見なさない（知らせを作るかの判断と同じ見方）。空の行は数えない。
    /// </summary>
    public static IReadOnlyList<NotificationLine> Compare(string? before, string? after)
    {
        var old = Lines(before);
        var current = Lines(after);

        // 頭と尻の同じ行を先に外す。多くの更新は1か所に数行足すだけなので、突き合わせる表がほぼ要らなくなる
        var head = 0;
        while (head < old.Count && head < current.Count && old[head] == current[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < old.Count - head && tail < current.Count - head
            && old[old.Count - 1 - tail] == current[current.Count - 1 - tail])
        {
            tail++;
        }

        var oldMiddle = old.Skip(head).Take(old.Count - head - tail).ToList();
        var currentMiddle = current.Skip(head).Take(current.Count - head - tail).ToList();
        if (oldMiddle.Count == 0 && currentMiddle.Count == 0)
        {
            return [];
        }

        return (long)oldMiddle.Count * currentMiddle.Count <= MaxTableCells
            ? Align(oldMiddle, currentMiddle)
            : Unmatched(oldMiddle, currentMiddle);
    }

    /// <summary>本文を行に分ける。行の中の空白は1つに詰める（<see cref="BoothChanges"/> が本文を比べるときと同じ詰め方）。</summary>
    public static List<string> Lines(string? text)
        => (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>1行の空白を詰める。画面が今の本文の行と差の行を突き合わせるときも、同じ詰め方で比べる。</summary>
    public static string NormalizeLine(string line)
        => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>いちばん長く共通する並び（LCS）で突き合わせ、残った行を足した・消したに分ける。</summary>
    private static List<NotificationLine> Align(List<string> old, List<string> current)
    {
        // lengths[i, j]：old[i..] と current[j..] の共通する並びの長さ。後ろから埋めると、前から辿って差を出せる
        var lengths = new int[old.Count + 1, current.Count + 1];
        for (var i = old.Count - 1; i >= 0; i--)
        {
            for (var j = current.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = old[i] == current[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var result = new List<NotificationLine>();
        var (oi, ci) = (0, 0);
        while (oi < old.Count || ci < current.Count)
        {
            if (oi < old.Count && ci < current.Count && old[oi] == current[ci])
            {
                oi++;
                ci++;
            }
            else if (ci >= current.Count || (oi < old.Count && lengths[oi + 1, ci] >= lengths[oi, ci + 1]))
            {
                // 消した行を先に出す。書き換えた行は「消した → 足した」の順に並び、前後を見比べやすい
                result.Add(Removed(old[oi++]));
            }
            else
            {
                result.Add(Added(current[ci++]));
            }
        }

        return result;
    }

    /// <summary>並びを見ずに、相手に無い行を拾う（同じ行が何回あるかは数える）。長すぎる本文のときだけ使う。</summary>
    private static List<NotificationLine> Unmatched(List<string> old, List<string> current)
    {
        var result = new List<NotificationLine>();
        result.AddRange(Missing(old, current).Select(Removed));
        result.AddRange(Missing(current, old).Select(Added));
        return result;
    }

    /// <summary><paramref name="lines"/> のうち、<paramref name="other"/> に（同じ回数ぶん）無い行。</summary>
    private static IEnumerable<string> Missing(List<string> lines, List<string> other)
    {
        var counts = other.GroupBy(line => line, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (counts.TryGetValue(line, out var count) && count > 0)
            {
                counts[line] = count - 1;
            }
            else
            {
                yield return line;
            }
        }
    }

    private static NotificationLine Added(string text) => new() { Kind = NotificationLineKind.Added, Text = Clip(text) };

    private static NotificationLine Removed(string text) => new() { Kind = NotificationLineKind.Removed, Text = Clip(text) };

    private static string Clip(string text) => text.Length <= MaxLineLength ? text : text[..MaxLineLength] + "…";
}
