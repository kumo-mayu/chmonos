using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// NFKC で畳む道を1本にまとめる。**壊れた UTF-16（片割れのサロゲート）でも例外にしない。**
///
/// 絵文字は2つの UTF-16 単位でできていて、文字列を途中で切ったり、1字ずつ畳んだり、
/// 記号を1つずつ並べた正規表現で落としたりすると片方だけが残る。<see cref="string.Normalize()"/> は
/// それを受けると <see cref="ArgumentException"/> を投げるので、検索欄に絵文字を打っただけで検索が落ち、
/// 検出は1件の商品で丸ごと止まった（評価台で実際に止まった・点検 2026-09-23）。
/// 片割れは文字として意味を持たないので落として続ける。
/// </summary>
public static class Nfkc
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        try
        {
            return text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return DropLoneSurrogates(text).Normalize(NormalizationForm.FormKC);
        }
    }

    /// <summary>
    /// 1字を畳む。畳んで1字にならない物（「㍿」→「株式会社」）と、
    /// サロゲートの片方（それだけでは文字にならない）はそのまま返す。
    /// 構文の記号（全角の括弧・引用符）を見分ける所で使う。
    /// </summary>
    public static char FoldChar(char c)
    {
        if (c < 0x80 || char.IsSurrogate(c))
        {
            return c;
        }

        var folded = c.ToString().Normalize(NormalizationForm.FormKC);
        return folded.Length == 1 ? folded[0] : c;
    }

    private static string DropLoneSurrogates(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(ch).Append(text[++i]);
            }
            else if (!char.IsSurrogate(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
