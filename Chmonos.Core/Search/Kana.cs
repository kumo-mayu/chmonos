using System.Text;

namespace Chmonos.Core.Search;

/// <summary>
/// ひらがな・カタカナの見分けと、文字コードの差（0x60）での寄せ合い。辞書は要らない。
/// 前は検索・読み・辞書・検出・変換・並べ替えの10か所ほどに同じ範囲と式を写していた。
///
/// 範囲は ひらがな「ぁ〜ゖ」・カタカナ「ァ〜ヶ」（互いに 0x60 ずれて1対1に対応する所だけ）。
/// 長音「ー」と中黒「・」は、どちらのかなでもないので含めない——要る所が足す（読みを数える所は「ー」をかなに数えるなど、所ごとに違う）。
/// カタカナの「ヷ〜ヺ」はひらがなに対応が無いので寄せない。名前の語を「ァ〜ヺ」で見る検出（<c>AvatarDetector</c>）は問いが違うので寄せない。
/// </summary>
public static class Kana
{
    /// <summary>ひらがなとカタカナの文字コードの差。</summary>
    private const int Offset = 'ァ' - 'ぁ';

    public static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ';

    public static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヶ';

    /// <summary>かなか（ひらがな・カタカナ・長音「ー」）。読みや名前の語をかなだけの物と見るときの範囲。</summary>
    public static bool IsKanaOrLongVowel(char c) => IsHiragana(c) || IsKatakana(c) || c == 'ー';

    public static char ToHiragana(char c) => IsKatakana(c) ? (char)(c - Offset) : c;

    public static char ToKatakana(char c) => IsHiragana(c) ? (char)(c + Offset) : c;

    /// <summary>カタカナをひらがなに寄せる（長音・記号はそのまま）。</summary>
    public static string ToHiragana(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(ToHiragana(c));
        }

        return builder.ToString();
    }

    /// <summary>ひらがなをカタカナに寄せる（長音・記号はそのまま）。</summary>
    public static string ToKatakana(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(ToKatakana(c));
        }

        return builder.ToString();
    }
}
