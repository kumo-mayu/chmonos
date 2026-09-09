using System.Text;

namespace BoothAssetManager.Core.Search;

/// <summary>
/// ローマ字を読み（ひらがな）に直す。**通信も辞書も要らない**、表の引き当てだけ。
///
/// 候補を複数返すのは、ローマ字が一意に決まらないため。
/// <c>shinon</c> は「し・の・ん」とも「し・ん・お・ん」とも読める。
/// 前者を選べば「心音（しんおん）」に届かない。どちらが正しいかは
/// ここでは決められないので、**両方返して後段の照合に落とさせる。**
///
/// 日本語でない語（<c>kipfel</c> など）は崩れた読みになるが、
/// それが辞書にもライブラリにも当たらないだけで害は無い。
/// </summary>
public static class RomajiReading
{
    /// <summary>返す候補の上限。増やしても当たりは増えず、照合だけ重くなる。</summary>
    private const int MaxResults = 6;

    private static readonly (string Romaji, string Kana)[] Table = BuildTable();

    /// <summary>この語がローマ字として読めるか（ASCIIの英字だけでできているか）。</summary>
    public static bool LooksRomaji(string word)
        => word.Length > 0 && word.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '\'' or '-');

    /// <summary>
    /// 読みの候補。読めなければ空。
    /// </summary>
    public static IReadOnlyList<string> Readings(string word)
    {
        if (!LooksRomaji(word))
        {
            return [];
        }

        var source = word.ToLowerInvariant().Replace("-", string.Empty).Replace("'", string.Empty);
        if (source.Length == 0)
        {
            return [];
        }

        var results = new List<string>();
        Walk(source, 0, new StringBuilder(), results);
        return results;
    }

    /// <summary>
    /// 先頭から順に、当てはまる綴りを長い方から試す。
    /// 行き止まりになったらそこで捨てる（部分的に読めた分は返さない——
    /// 「途中まで読めた」ものは日本語として意味を成さないため）。
    /// </summary>
    private static void Walk(string source, int index, StringBuilder kana, List<string> results)
    {
        if (results.Count >= MaxResults)
        {
            return;
        }

        if (index >= source.Length)
        {
            var text = kana.ToString();
            if (text.Length > 0 && !results.Contains(text))
            {
                results.Add(text);
            }

            return;
        }

        var current = source[index];

        // 促音：子音が2つ続いたら「っ」。ただし n は撥音なので除く
        if (index + 1 < source.Length
            && current == source[index + 1]
            && current is not ('n' or 'a' or 'i' or 'u' or 'e' or 'o'))
        {
            var length = kana.Length;
            kana.Append('っ');
            Walk(source, index + 1, kana, results);
            kana.Length = length;
            return;
        }

        // 撥音：n のあとが母音でも y でもなければ「ん」で確定
        if (current == 'n')
        {
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (next is not ('a' or 'i' or 'u' or 'e' or 'o' or 'y'))
            {
                var length = kana.Length;
                kana.Append('ん');
                Walk(source, index + 1, kana, results);
                kana.Length = length;
                return;
            }

            // 母音が続く場合は「な行」と「ん＋母音」の両方があり得る。
            // shinon → しのん / しんおん。後者でしか引けない語があるので両方進める
            var branch = kana.Length;
            kana.Append('ん');
            Walk(source, index + 1, kana, results);
            kana.Length = branch;
        }

        foreach (var (romaji, kanaText) in Table)
        {
            if (romaji.Length > source.Length - index)
            {
                continue;
            }

            if (string.CompareOrdinal(source, index, romaji, 0, romaji.Length) != 0)
            {
                continue;
            }

            var length = kana.Length;
            kana.Append(kanaText);
            Walk(source, index + romaji.Length, kana, results);
            kana.Length = length;

            if (results.Count >= MaxResults)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 綴りと読みの対応。ヘボン式と訓令式の両方を入れてある（<c>shi</c> と <c>si</c> の両方）。
    /// 長い綴りから試すので、ここでは長さ順に並べ替えて持つ。
    /// </summary>
    private static (string, string)[] BuildTable()
    {
        var pairs = new List<(string, string)>
        {
            ("a", "あ"), ("i", "い"), ("u", "う"), ("e", "え"), ("o", "お"),
            ("ka", "か"), ("ki", "き"), ("ku", "く"), ("ke", "け"), ("ko", "こ"),
            ("ga", "が"), ("gi", "ぎ"), ("gu", "ぐ"), ("ge", "げ"), ("go", "ご"),
            ("sa", "さ"), ("shi", "し"), ("si", "し"), ("su", "す"), ("se", "せ"), ("so", "そ"),
            ("za", "ざ"), ("ji", "じ"), ("zi", "じ"), ("zu", "ず"), ("ze", "ぜ"), ("zo", "ぞ"),
            ("ta", "た"), ("chi", "ち"), ("ti", "ち"), ("tsu", "つ"), ("tu", "つ"), ("te", "て"), ("to", "と"),
            ("da", "だ"), ("di", "ぢ"), ("du", "づ"), ("de", "で"), ("do", "ど"),
            ("na", "な"), ("ni", "に"), ("nu", "ぬ"), ("ne", "ね"), ("no", "の"),
            ("ha", "は"), ("hi", "ひ"), ("fu", "ふ"), ("hu", "ふ"), ("he", "へ"), ("ho", "ほ"),
            ("ba", "ば"), ("bi", "び"), ("bu", "ぶ"), ("be", "べ"), ("bo", "ぼ"),
            ("pa", "ぱ"), ("pi", "ぴ"), ("pu", "ぷ"), ("pe", "ぺ"), ("po", "ぽ"),
            ("ma", "ま"), ("mi", "み"), ("mu", "む"), ("me", "め"), ("mo", "も"),
            ("ya", "や"), ("yu", "ゆ"), ("yo", "よ"),
            ("ra", "ら"), ("ri", "り"), ("ru", "る"), ("re", "れ"), ("ro", "ろ"),
            ("wa", "わ"), ("wo", "を"), ("nn", "ん"),
            ("va", "ゔぁ"), ("vi", "ゔぃ"), ("vu", "ゔ"), ("ve", "ゔぇ"), ("vo", "ゔぉ"),

            ("kya", "きゃ"), ("kyu", "きゅ"), ("kyo", "きょ"),
            ("gya", "ぎゃ"), ("gyu", "ぎゅ"), ("gyo", "ぎょ"),
            ("sha", "しゃ"), ("shu", "しゅ"), ("sho", "しょ"),
            ("sya", "しゃ"), ("syu", "しゅ"), ("syo", "しょ"),
            ("ja", "じゃ"), ("ju", "じゅ"), ("jo", "じょ"),
            ("jya", "じゃ"), ("jyu", "じゅ"), ("jyo", "じょ"),
            ("zya", "じゃ"), ("zyu", "じゅ"), ("zyo", "じょ"),
            ("cha", "ちゃ"), ("chu", "ちゅ"), ("cho", "ちょ"),
            ("tya", "ちゃ"), ("tyu", "ちゅ"), ("tyo", "ちょ"),
            ("nya", "にゃ"), ("nyu", "にゅ"), ("nyo", "にょ"),
            ("hya", "ひゃ"), ("hyu", "ひゅ"), ("hyo", "ひょ"),
            ("bya", "びゃ"), ("byu", "びゅ"), ("byo", "びょ"),
            ("pya", "ぴゃ"), ("pyu", "ぴゅ"), ("pyo", "ぴょ"),
            ("mya", "みゃ"), ("myu", "みゅ"), ("myo", "みょ"),
            ("rya", "りゃ"), ("ryu", "りゅ"), ("ryo", "りょ"),
            ("fa", "ふぁ"), ("fi", "ふぃ"), ("fe", "ふぇ"), ("fo", "ふぉ"),
            ("she", "しぇ"), ("che", "ちぇ"), ("je", "じぇ"),
            ("ti", "てぃ"), ("di", "でぃ"), ("dyu", "でゅ"),
        };

        // 長い綴りを先に試す。ka より kya を先に見ないと「きゃ」が作れない
        return pairs
            .OrderByDescending(pair => pair.Item1.Length)
            .ThenBy(pair => pair.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>ひらがなをカタカナに寄せる。文字コードを足すだけで、辞書は要らない。</summary>
    public static string ToKatakana(string hiragana)
    {
        var builder = new StringBuilder(hiragana.Length);
        foreach (var c in hiragana)
        {
            builder.Append(c is >= 'ぁ' and <= 'ゖ' ? (char)(c + 0x60) : c);
        }

        return builder.ToString();
    }
}
