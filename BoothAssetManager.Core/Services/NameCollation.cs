using System.Collections.Concurrent;
using System.Text;
using BoothAssetManager.Core.Search;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 名前の読みの順（検索の「名前」「ショップ」の並べ替え。ユーザ判断 2026-09-24）。
///
/// 文字の符号の順（前の並び）だと、先頭に【】の付いた名前が固まり、カタカナとひらがな・漢字が別々の塊になって、
/// 「あ→わ」と言いながら五十音に並ばなかった。比べる前に名前を**並べ替えの鍵**に直す：
/// <list type="bullet">
/// <item>先頭の括弧の塊（【…】［…］[…]（…）〔…〕など）と記号・空白を飛ばす。括弧しか無い名前は括弧の中身で比べる</item>
/// <item>漢字は同梱の KANJIDIC2 の読みでひらがなにする（<see cref="KanjiReadings.PrimaryReading"/>・推定）。カタカナもひらがなに寄せる</item>
/// <item>英字は大文字小文字を区別しない。全角の英数字は半角に寄せる。数字は数として比べる（2 が 10 より前）</item>
/// </list>
///
/// **鍵は名前ごとに1度だけ作って控える。**並べ替えのたびに2000件の鍵を作り直さない
/// （控えは名前の文字列で引くので、名前が変われば新しい鍵ができ、同じ名前は使い回す）。
/// </summary>
public sealed class NameCollation : IComparer<string>
{
    /// <summary>漢字の読みを使わない並び（試験・読みの道具を持たない呼び手）。漢字はその字のまま比べる。</summary>
    public static NameCollation Plain { get; } = new(null);

    private static readonly (char Open, char Close)[] Brackets =
    [
        ('【', '】'), ('［', '］'), ('[', ']'), ('（', '）'), ('(', ')'), ('〔', '〕'),
        ('「', '」'), ('『', '』'), ('〈', '〉'), ('《', '》'), ('〖', '〗'), ('<', '>'), ('＜', '＞'), ('{', '}'), ('｛', '｝'),
    ];

    private readonly KanjiReadings? _readings;
    private readonly ConcurrentDictionary<string, string> _keys = new(StringComparer.Ordinal);
    private readonly Func<string, string> _build;

    public NameCollation(KanjiReadings? readings)
    {
        _readings = readings;
        _build = name => BuildKey(name, _readings);
    }

    /// <summary>名前の並べ替えの鍵（控えから引く。無ければ作って控える）。</summary>
    public string KeyOf(string name) => _keys.GetOrAdd(name, _build);

    /// <summary>
    /// 並べ替えに渡す鍵（<c>OrderBy(item => names.SortKeyOf(item.DisplayName))</c>）。
    /// LINQ の並べ替えは鍵を要素ごとに1度だけ引くので、比べるたびに控えを引かずに済む（2000件で約2万回比べる）。
    /// </summary>
    public NameSortKey SortKeyOf(string name) => new(KeyOf(name), name);

    /// <summary>
    /// 鍵で比べ、鍵が同じなら元の名前の符号の順（「きゃ」と「きや」、「A」と「a」を毎回同じ順に置くため）。
    /// </summary>
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        var byKey = CompareKeys(KeyOf(x), KeyOf(y));
        return byKey != 0 ? byKey : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// 鍵どうしを比べる。数字の続きは数として比べ、それ以外は符号の順
    /// （鍵の中では 数字 → 英字 → ひらがな → 読めなかった漢字 の順になる）。
    /// </summary>
    public static int CompareKeys(string x, string y)
    {
        // 同じ頭はまとめて飛ばす（シリーズ物は頭が長く揃っていて、1字ずつ比べると2000件の並べ替えが 4ms かかった）。
        // 数字の途中で止まったら、数として比べられるよう数字の頭まで戻す
        var common = x.AsSpan().CommonPrefixLength(y);
        while (common > 0 && char.IsAsciiDigit(x[common - 1]))
        {
            common--;
        }

        int i = common, j = common;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var start1 = i;
                var start2 = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var left = x.AsSpan(start1, i - start1).TrimStart('0');
                var right = y.AsSpan(start2, j - start2).TrimStart('0');

                if (left.Length != right.Length)
                {
                    return left.Length - right.Length;
                }

                var digits = left.SequenceCompareTo(right);
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            if (x[i] != y[j])
            {
                return x[i] - y[j];
            }

            i++;
            j++;
        }

        return (x.Length - i) - (y.Length - j);
    }

    /// <summary>名前から並べ替えの鍵を作る（控えを通さない。試験と測定用に出してある）。</summary>
    public static string BuildKey(string name, KanjiReadings? readings)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // 全角の英数字・半角のカタカナを寄せる（ＡＢＣ→ABC・ｱ→ア）
        var text = name.Normalize(NormalizationForm.FormKC);

        var body = SkipLeadingBrackets(text, out var bracketContents);
        var key = Fold(body, readings);

        // 括弧を飛ばすと何も残らない名前（【セット】だけ）は、括弧の中身で比べる
        if (key.Length == 0)
        {
            key = Fold(bracketContents, readings);
        }

        // 記号だけの名前は鍵が空になる。空どうしで全部同じにせず、元の名前で比べる
        return key.Length == 0 ? text : key;
    }

    /// <summary>先頭の括弧の塊と記号・空白を飛ばした残り。飛ばした括弧の中身は <paramref name="contents"/> に繋ぐ。</summary>
    private static string SkipLeadingBrackets(string text, out string contents)
    {
        var skipped = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsLetterOrDigit(c))
            {
                break;
            }

            var close = CloserOf(c);
            if (close is { } closer && text.IndexOf(closer, index + 1) is var end and > 0)
            {
                skipped.Append(text, index + 1, end - index - 1).Append(' ');
                index = end + 1;
                continue;
            }

            // 閉じの無い括弧・記号・空白は1字ずつ飛ばす
            index++;
        }

        contents = skipped.ToString();
        return text[index..];
    }

    private static char? CloserOf(char open)
    {
        foreach (var (o, c) in Brackets)
        {
            if (o == open)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>記号・空白を落とし、カタカナと漢字をひらがなに、英字を小文字にする。</summary>
    private static string Fold(string text, KanjiReadings? readings)
    {
        var builder = new StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length;)
        {
            var c = text[index];

            if (IsKanji(c))
            {
                // 漢字の続き（熟語）は音、1字だけなら訓で読む（KanjiReadings.PrimaryReading）
                var end = index;
                while (end < text.Length && IsKanji(text[end]))
                {
                    end++;
                }

                var inCompound = end - index > 1;
                string? previous = null;
                for (var k = index; k < end; k++)
                {
                    // 々は前の字をもう一度読む（濁りは推さない）
                    var reading = text[k] == '々' ? previous : readings?.PrimaryReading(text[k], inCompound);
                    if (reading is null)
                    {
                        // 読めない字はそのまま置く（ひらがなの後ろに並ぶ）
                        builder.Append(text[k]);
                        previous = null;
                        continue;
                    }

                    builder.Append(reading);
                    previous = reading;
                }

                index = end;
                continue;
            }

            index++;

            if (c == 'ー')
            {
                // 長音は前のかなの母音に読み替える（「けーき」を「けえき」として「けいと」より後ろに置く）
                if (builder.Length > 0 && VowelOf(builder[^1]) is { } vowel)
                {
                    builder.Append(vowel);
                }

                continue;
            }

            if (c is >= 'ァ' and <= 'ヶ')
            {
                c = (char)(c - 0x60);
            }

            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            builder.Append(Large(char.ToLowerInvariant(c)));
        }

        return builder.ToString();
    }

    /// <summary>小さいかなは大きいかなとして比べる（「きゃ」と「きや」を隣に置く）。</summary>
    private static char Large(char c) => c switch
    {
        'ぁ' => 'あ',
        'ぃ' => 'い',
        'ぅ' => 'う',
        'ぇ' => 'え',
        'ぉ' => 'お',
        'っ' => 'つ',
        'ゃ' => 'や',
        'ゅ' => 'ゆ',
        'ょ' => 'よ',
        'ゎ' => 'わ',
        'ゕ' => 'か',
        'ゖ' => 'け',
        _ => c,
    };

    private const string VowelA = "あかがさざただなはばぱまやらわ";
    private const string VowelI = "いきぎしじちぢにひびぴみり";
    private const string VowelU = "うくぐすずつづぬふぶぷむゆるゔ";
    private const string VowelE = "えけげせぜてでねへべぺめれ";
    private const string VowelO = "おこごそぞとどのほぼぽもよろを";

    private static char? VowelOf(char kana)
        => VowelA.Contains(kana) ? 'あ'
            : VowelI.Contains(kana) ? 'い'
            : VowelU.Contains(kana) ? 'う'
            : VowelE.Contains(kana) ? 'え'
            : VowelO.Contains(kana) ? 'お'
            : null;

    private static bool IsKanji(char c) => c is >= '一' and <= '鿿' or '々';
}

/// <summary>名前の読みの順の鍵1つ（<see cref="NameCollation.SortKeyOf"/>）。</summary>
public readonly record struct NameSortKey(string Key, string Name) : IComparable<NameSortKey>
{
    /// <summary>鍵で比べ、鍵が同じなら元の名前の符号の順（<see cref="NameCollation.Compare"/> と同じ）。</summary>
    public int CompareTo(NameSortKey other)
    {
        var byKey = NameCollation.CompareKeys(Key, other.Key);
        return byKey != 0 ? byKey : string.CompareOrdinal(Name, other.Name);
    }
}
