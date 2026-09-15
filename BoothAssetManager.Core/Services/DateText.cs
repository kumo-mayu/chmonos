using System.Globalization;
using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 日付の欄に打った文字を読む（検索の公開日・入手日・ユーザ案 2026-09-15）。
///
/// - <c>2026/09/01</c>・<c>2026-9-1</c>・<c>2026.9.1</c>・<c>2026年9月1日</c>・<c>20260901</c>：その日
/// - <c>9/1</c>：**今日以前で最も近い**その日（今日が 8月1日で <c>12/01</c> なら去年の 12月1日）
/// - <c>2026/09</c>：開始の欄なら月初め、終わりの欄なら月末
/// - <c>2026</c>：開始の欄なら 1月1日、終わりの欄なら 12月31日
///
/// 全角の数字・記号も読む（NFKC で畳む）。読めなければ null（画面は「日付として読めません」と出し、条件にしない）。
/// </summary>
public static class DateText
{
    /// <param name="isEnd">終わりの欄か（月だけ・年だけのとき、終わりの日に寄せる）。</param>
    public static DateOnly? Parse(string? text, bool isEnd, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = new StringBuilder(text.Trim().Normalize(NormalizationForm.FormKC))
            .Replace('年', '/').Replace('月', '/').Replace("日", string.Empty)
            .Replace('-', '/').Replace('.', '/')
            .ToString()
            .Trim('/', ' ');

        var parts = folded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => !part.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var numbers = parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();

        return parts.Length switch
        {
            3 => Make(numbers[0], numbers[1], numbers[2]),
            2 when parts[0].Length == 4 => Month(numbers[0], numbers[1], isEnd),
            2 => NearestPast(numbers[0], numbers[1], today),
            1 when parts[0].Length == 8 => Make(numbers[0] / 10000, numbers[0] / 100 % 100, numbers[0] % 100),
            1 when parts[0].Length == 4 => Make(numbers[0], isEnd ? 12 : 1, isEnd ? 31 : 1),
            _ => null,
        };
    }

    private static DateOnly? Make(int year, int month, int day)
        => year is >= 1900 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;

    private static DateOnly? Month(int year, int month, bool isEnd)
        => year is >= 1900 and <= 9999 && month is >= 1 and <= 12
            ? new DateOnly(year, month, isEnd ? DateTime.DaysInMonth(year, month) : 1)
            : null;

    /// <summary>年の無い「月/日」は、今日以前で最も近いその日。今年のその日がまだ来ていなければ去年。</summary>
    private static DateOnly? NearestPast(int month, int day, DateOnly today)
    {
        if (Make(today.Year, month, day) is { } thisYear && thisYear <= today)
        {
            return thisYear;
        }

        // 2月29日は去年に無いことがある。そのときは読めないとする
        return Make(today.Year - 1, month, day);
    }
}
