using System.Globalization;
using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 金額の欄の読み取り（ユーザ判断 2026-09-20・I3：「考えられる基本的な記法の種類に対応し、
/// それでも漏れた例は止めずに注意を出しましょう」）。
///
/// **前は `int.TryParse` だけだった**ので、「¥1,200」「1,200円」「１２００」は黙って空になり、
/// 支出の統計から静かに落ちていた。打った本人は保存できたと思っている。
///
/// 受けるのは、人が普通に打つ形：通貨の記号（¥ ￥ 円）・桁区切りのコンマ・全角・前後の空白。
/// **読めなかったら null を返す**。呼ぶ側は捨てたことを言う（止めはしない）。
/// </summary>
public static class MoneyText
{
    /// <summary>
    /// 払った額の欄を読む。**空欄は 0円**（ユーザ判断 2026-09-29：未入力を無くす）。読めない文字だけ null（呼ぶ側が捨てたことを言う）。
    ///
    /// 前は空欄を「未入力」（null）として持ち、0 を無料と分けていた。未入力は検索の「払った額」「有料・無料」で値の無い物として
    /// 絞り込み・並べ替えから抜け落ち、探しにくかった。印を付けると今の値段が入るので、空欄になるのは BOOTH の値段が無い行
    /// （種類を指さない購入・消えた版）か、わざわざ消したときだけ。支出の合計はもともと未入力を 0 で足していたので変わらない
    /// </summary>
    public static int? ParsePaid(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Parse(text);

    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // 全角の数字・記号を半角に寄せてから、金額に使われる飾りを落とす
        var folded = new StringBuilder(Nfkc.Fold(text.Trim()))
            .Replace("¥", string.Empty)
            .Replace("￥", string.Empty)
            .Replace("円", string.Empty)
            .Replace("JPY", string.Empty)
            .Replace(",", string.Empty)
            .Replace("、", string.Empty)
            .Replace(" ", string.Empty)
            .ToString()
            .Trim();

        if (folded.Length == 0)
        {
            return null;
        }

        // 「1200.0」のような書き方も受ける（小数を持つ金額はこのアプリでは扱わないので、端数があれば読めないとする）
        if (folded.Contains('.'))
        {
            return decimal.TryParse(folded, NumberStyles.Number, CultureInfo.InvariantCulture, out var exact)
                && exact == decimal.Truncate(exact)
                && exact is >= 0 and <= int.MaxValue
                ? (int)exact
                : null;
        }

        return int.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }

    /// <summary>打ってあるのに読めなかったか。**空欄は「読めなかった」ではない**（入れていないだけ）。</summary>
    public static bool IsUnreadable(string? text)
        => !string.IsNullOrWhiteSpace(text) && Parse(text) is null;
}
