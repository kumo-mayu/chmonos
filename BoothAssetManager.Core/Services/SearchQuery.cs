using System.Globalization;
using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 文字列で探す対象（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
/// 前置き（<c>name:</c> など）の名前は <see cref="SearchQuery.FieldNames"/>。
/// </summary>
public enum SearchField
{
    /// <summary>商品名（自分で付けた名前と BOOTH の名前）。</summary>
    Name,

    /// <summary>ショップ名（自分で入れた名前と BOOTH の名前）。</summary>
    Shop,

    Subdomain,

    Memo,

    /// <summary>本文（説明文と h2 の見出し・本文）。</summary>
    Main,

    /// <summary>手元のファイル・フォルダの保存場所の全体。</summary>
    Path,

    /// <summary>商品ID（仮ID <c>local-</c> を含む）。</summary>
    Id,

    /// <summary>種類（バリエーション）の名前。BOOTH の今の名前と、購入記録に写した名前。</summary>
    Variation,

    /// <summary>手元のファイル自体の名前（フォルダで持つ物はフォルダの名前）。</summary>
    File,

    /// <summary>zip の中のファイル名。</summary>
    Content,

    /// <summary>BOOTHタグ。</summary>
    Tag,
}

/// <summary>
/// 文字列で探すときの切り替え（ユーザ案 2026-09-15）。既定は「商品名・ショップ名・メモ」を、
/// 大文字と小文字・全角と半角を区別せず、ひらがなとカタカナは区別して探す。
/// </summary>
public sealed record SearchOptions
{
    public static IReadOnlySet<SearchField> DefaultTargets { get; } =
        new HashSet<SearchField> { SearchField.Name, SearchField.Shop, SearchField.Memo };

    public static SearchOptions Default { get; } = new();

    /// <summary>前置きの無い語を探す対象。前置きのある語は、ここに無い対象でも探す（ユーザ判断 2026-09-16）。</summary>
    public IReadOnlySet<SearchField> Targets { get; init; } = DefaultTargets;

    public bool CaseSensitive { get; init; }

    public bool WidthSensitive { get; init; }

    public bool KanaSensitive { get; init; } = true;

    /// <summary>
    /// 商品名の読み（漢字1字ごとの音訓から組んだ「あり得る読み」）も見る。**造語変換を入れたときだけ。**
    /// 常に見ないのは、外れの読みも混じっていて「なぜこれが出たのか」を説明できないため。
    /// </summary>
    public bool IncludeReadings { get; init; }

    /// <summary>大文字小文字と全角半角を区別しない（＝前もって畳んだ文字列で速く比べられる）か。</summary>
    internal bool UsesFold => !CaseSensitive && !WidthSensitive;
}

/// <summary>
/// 1商品の、文字列で探す材料。
///
/// **畳んだ文字列は、その対象を初めて探すときに作って持つ。**入力1文字ごとに全商品の説明文を畳むと重いので
/// 1度だけ作るが、本文や zip の中身は既定で探さないので、使われるまで作らない（メモリを食わない）。
/// 大文字と小文字・全角と半角を区別するときは、畳まずに元の文字列（商品の記録そのもの）と比べる。
/// </summary>
public sealed class SearchHaystack
{
    private static readonly int FieldCount = Enum.GetValues<SearchField>().Length;

    private readonly Func<SearchField, IReadOnlyList<string>> _raw;
    private readonly string?[] _folded = new string?[FieldCount];
    private readonly string?[] _foldedKana = new string?[FieldCount];

    /// <param name="raw">対象ごとの元の文字列。何度呼ばれてもよい（元の記録から作り直す）。</param>
    /// <param name="readings">商品名の読み（畳み済み）。</param>
    public SearchHaystack(Func<SearchField, IReadOnlyList<string>> raw, string readings = "")
    {
        _raw = raw;
        Readings = readings;
    }

    /// <summary>商品名の読み（畳み済み・ひらがな）。造語変換のときだけ見る。</summary>
    public string Readings { get; }

    /// <summary>試験用：対象ごとの文字列から作る。</summary>
    public static SearchHaystack FromValues(IReadOnlyDictionary<SearchField, string[]> values, string readings = "")
        => new(field => values.TryGetValue(field, out var list) ? list : [], SearchQuery.Normalize(readings));

    public IReadOnlyList<string> Raw(SearchField field) => _raw(field);

    /// <summary>
    /// 畳んだ文字列（NFKC＋小文字）。値ごとに改行で区切って繋ぐ——区切らずに繋ぐと、隣り合った値の末尾と先頭が
    /// 1つの語のように見えて、打っていない組み合わせに当たってしまう。
    /// </summary>
    public string Folded(SearchField field)
        => _folded[(int)field] ??= SearchQuery.Normalize(string.Join('\n', _raw(field).Where(value => value.Length > 0)));

    /// <summary>畳んだうえで、カタカナをひらがなに寄せた文字列（「ひらがなとカタカナを区別しない」とき）。</summary>
    public string FoldedIgnoringKana(SearchField field)
        => _foldedKana[(int)field] ??= SearchQuery.ToHiragana(Folded(field));
}

/// <summary>
/// 検索式の1ノード。
///
/// スペース＝AND、<c>-語</c>＝除外、<c>"..."</c>＝フレーズ、<c>OR</c>、<c>( )</c> で優先順位、<c>name:語</c>＝対象を絞る。
/// 記号を基本にしたのは、日常の入力がスペース区切りだから。毎回 AND と打つのは負担で、
/// NOT より <c>-</c> の方が短い。ORだけは記号に定訳がないので語句にする。
/// </summary>
public abstract record SearchNode
{
    private protected SearchNode()
    {
    }

    /// <summary>条件なし。空の入力はすべてに当たる。</summary>
    public sealed record All : SearchNode;

    /// <summary>1つの語、またはフレーズ。</summary>
    /// <param name="Text">畳んだ語（NFKC＋小文字）。既定の比べ方と、別表記を作るときに使う。</param>
    /// <param name="Raw">打ったままの語。大文字と小文字・全角と半角を区別するときに使う。</param>
    /// <param name="Field">前置きで絞った対象。無ければ「対象」の切り替えに従う。</param>
    /// <param name="WholeWord">
    /// 英単語の区切りで当てる。日英変換で作った英語にだけ付ける——部分一致だと短い英語が別の単語の途中に当たり
    /// （top が stop に当たる）、増えた当たりのうち関係のある目安は 32%。区切りで当てると 64% に上がった（2026-09-16・試験データで測定・
    /// `experiments/KatakanaEnglishProbe`）。
    /// </param>
    public sealed record Term(string Text, string Raw, SearchField? Field = null, bool WholeWord = false) : SearchNode;

    public sealed record Not(SearchNode Inner) : SearchNode;

    public sealed record And(IReadOnlyList<SearchNode> Parts) : SearchNode;

    public sealed record Or(IReadOnlyList<SearchNode> Parts) : SearchNode;
}

/// <summary>
/// 検索文字列の解釈。
///
/// **構文の記号は1字ずつ NFKC で畳んでから読む**（ユーザ判断 2026-09-16「記号も畳みましょう」）。
/// 日本語入力では全角の括弧・引用符・ハイフン・空白・コロンのまま打たれるのが普通なので、畳まないと構文が動かない。
/// 語の中身は打ったままも持ち、区別する切り替えのときはそちらで比べる。
/// </summary>
public static class SearchQuery
{
    /// <summary>
    /// 前置きの名前（ユーザ案 2026-09-15）。**ここに無い語は前置きとして読まない**（ユーザ判断 2026-09-16）——
    /// 「Re:Zero」や URL の「https:」を対象の指定と取り違えない。
    /// </summary>
    public static IReadOnlyDictionary<string, SearchField> FieldNames { get; } = new Dictionary<string, SearchField>(StringComparer.Ordinal)
    {
        ["name"] = SearchField.Name,
        ["shop"] = SearchField.Shop,
        ["subdomain"] = SearchField.Subdomain,
        ["memo"] = SearchField.Memo,
        ["main"] = SearchField.Main,
        ["path"] = SearchField.Path,
        ["id"] = SearchField.Id,
        ["variation"] = SearchField.Variation,
        ["file"] = SearchField.File,
        ["content"] = SearchField.Content,
        ["tag"] = SearchField.Tag,
    };

    public static string FieldName(SearchField field) => FieldNames.First(pair => pair.Value == field).Key;

    /// <summary>
    /// 区別する切り替えのときの比べ方。日本語の決まり（全角と半角・ひらがなとカタカナの対応）を知っている比べ方を使う。
    /// </summary>
    private static readonly CompareInfo JapaneseCompare = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    /// <summary>
    /// 比較用に文字列を畳む。NFKC＋小文字化。
    /// 探す側と探される側の両方に同じものを掛けることで、
    /// 全角と半角、大文字と小文字の違いを気にせず打てるようにする。
    /// </summary>
    public static string Normalize(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : Nfkc.Fold(text).ToLowerInvariant();

    /// <summary>カタカナをひらがなに寄せる（長音・記号はそのまま）。</summary>
    public static string ToHiragana(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        }

        return builder.ToString();
    }

    public static SearchNode Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchNode.All();
        }

        var tokens = DropUnmatchedClose(Tokenize(query));
        var index = 0;
        var node = ParseOr(tokens, ref index);
        return node ?? new SearchNode.All();
    }

    /// <summary>
    /// 対になる開き括弧の無い閉じ括弧を読み飛ばす。前は余った「)」でそこから後ろを黙って捨てていたので、
    /// 「夏) 冬」が「夏」だけで探され、打った語が効いていないことに気付けなかった（点検 2026-09-23）。
    /// 閉じていない「(」は文の終わりで閉じたとみなす（打ちかけの入力でも結果が出続ける方がよい）。
    /// </summary>
    private static List<Token> DropUnmatchedClose(List<Token> tokens)
    {
        var kept = new List<Token>(tokens.Count);
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Open)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.Close)
            {
                if (depth == 0)
                {
                    continue;
                }

                depth--;
            }

            kept.Add(token);
        }

        return kept;
    }

    public static bool Matches(SearchNode node, SearchHaystack haystack, SearchOptions options)
        => node switch
        {
            SearchNode.All => true,
            SearchNode.Term term => Contains(term, haystack, options),
            SearchNode.Not not => !Matches(not.Inner, haystack, options),
            SearchNode.And and => and.Parts.All(part => Matches(part, haystack, options)),
            SearchNode.Or or => or.Parts.Any(part => Matches(part, haystack, options)),
            _ => true,
        };

    private static bool Contains(SearchNode.Term term, SearchHaystack haystack, SearchOptions options)
    {
        if (term.Field is { } field)
        {
            return InField(term, haystack, field, options)
                || (field == SearchField.Name && InReadings(term, haystack, options));
        }

        foreach (var target in options.Targets)
        {
            if (InField(term, haystack, target, options))
            {
                return true;
            }
        }

        // 読みは商品名の読みなので、商品名を探しているときだけ見る
        return options.Targets.Contains(SearchField.Name) && InReadings(term, haystack, options);
    }

    private static bool InField(SearchNode.Term term, SearchHaystack haystack, SearchField field, SearchOptions options)
    {
        // 日英変換の英語。畳んだ語から作った候補なので、畳んだ文字列で区切りを見る
        if (term.WholeWord)
        {
            return ContainsWholeWord(haystack.Folded(field), term.Text);
        }

        if (options.UsesFold)
        {
            return options.KanaSensitive
                ? haystack.Folded(field).Contains(term.Text, StringComparison.Ordinal)
                : haystack.FoldedIgnoringKana(field).Contains(ToHiragana(term.Text), StringComparison.Ordinal);
        }

        var compare = CompareOptions.None;
        if (!options.CaseSensitive)
        {
            compare |= CompareOptions.IgnoreCase;
        }

        if (!options.WidthSensitive)
        {
            compare |= CompareOptions.IgnoreWidth;
        }

        if (!options.KanaSensitive)
        {
            compare |= CompareOptions.IgnoreKanaType;
        }

        foreach (var value in haystack.Raw(field))
        {
            var found = compare == CompareOptions.None
                ? value.Contains(term.Raw, StringComparison.Ordinal)
                : JapaneseCompare.IndexOf(value, term.Raw, compare) >= 0;
            if (found)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>前後が英字・数字でない所に語があるか（英単語の区切り）。日本語の文字の隣は区切りとみなす。</summary>
    private static bool ContainsWholeWord(string text, string word)
    {
        if (word.Length == 0)
        {
            return false;
        }

        for (var start = text.IndexOf(word, StringComparison.Ordinal); start >= 0;
             start = text.IndexOf(word, start + 1, StringComparison.Ordinal))
        {
            var end = start + word.Length;
            var before = start == 0 || !char.IsAsciiLetterOrDigit(text[start - 1]);
            var after = end >= text.Length || !char.IsAsciiLetterOrDigit(text[end]);
            if (before && after)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>読みはひらがなで組んであるので、畳んだ語で見る（区別の切り替えは効かせない）。</summary>
    private static bool InReadings(SearchNode.Term term, SearchHaystack haystack, SearchOptions options)
        => options.IncludeReadings
            && haystack.Readings.Length > 0
            && (haystack.Readings.Contains(term.Text, StringComparison.Ordinal)
                || haystack.Readings.Contains(ToHiragana(term.Text), StringComparison.Ordinal));

    // --- 字句 ---

    private enum TokenKind
    {
        Word,
        Or,
        Minus,
        Open,
        Close,

        /// <summary>中身の無い前置き（<c>name:</c>）。続くフレーズ・括弧・語に対象を当てる。</summary>
        Prefix,
    }

    private readonly record struct Token(TokenKind Kind, string Raw, SearchField? Field = null);

    /// <summary>1字を NFKC で畳む。構文の記号（全角の括弧・引用符・ハイフン・コロン）を見分けるため。</summary>
    private static char FoldSymbol(char c) => Nfkc.FoldChar(c);

    /// <summary>
    /// 引用符は <c>"</c> だけを見る。日本語の「」は商品名にそのまま出てくるので
    /// （『「タマクラゲ」』のように）、フレーズの印にすると打った通りに探せなくなる。
    /// </summary>
    private static bool IsQuote(char folded) => folded is '"' or '“' or '”';

    private static bool IsSpace(char c) => char.IsWhiteSpace(c) || char.IsWhiteSpace(FoldSymbol(c));

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = FoldSymbol(text[i]);

            if (IsSpace(text[i]))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.Open, "("));
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.Close, ")"));
                i++;
                continue;
            }

            // 語の先頭のハイフンだけが除外の印。Kuuta-3D の中のハイフンは語の一部として、
            // 下の語の読み取りにそのまま入る（ここには来ない）。
            if (c == '-')
            {
                var hasTarget = i + 1 < text.Length && !IsSpace(text[i + 1]) && FoldSymbol(text[i + 1]) != ')';

                // 打ちかけの「-」だけは落とす。語として扱うとどこにも当たらず0件になる
                if (hasTarget)
                {
                    tokens.Add(new Token(TokenKind.Minus, "-"));
                }

                i++;
                continue;
            }

            if (IsQuote(c))
            {
                i++;
                var start = i;
                while (i < text.Length && !IsQuote(FoldSymbol(text[i])))
                {
                    i++;
                }

                var phrase = text[start..i];
                if (i < text.Length)
                {
                    i++;
                }

                // 閉じ忘れでも、そこまでをフレーズとして扱う。引用符の中の「name:」は前置きとして読まない（ユーザ判断）
                if (phrase.Length > 0)
                {
                    tokens.Add(new Token(TokenKind.Word, phrase));
                }

                continue;
            }

            var wordStart = i;
            while (i < text.Length && !IsSpace(text[i]))
            {
                var folded = FoldSymbol(text[i]);
                if (folded is '(' or ')' || IsQuote(folded))
                {
                    break;
                }

                i++;
            }

            tokens.Add(ReadWord(text[wordStart..i]));
        }

        return tokens;
    }

    /// <summary>
    /// 語を読む。「OR」（大文字だけ。全角の「ＯＲ」も）なら演算子、決まった名前＋「:」（全角の「：」も）で始まれば前置き。
    /// 小文字の「or」は語として探す——英語の商品名（「Black or White」）で打った語が演算子に化けないように。
    /// spec の書き方も「OR」だけ（Google などと同じ）。
    /// </summary>
    private static Token ReadWord(string raw)
    {
        if (Nfkc.Fold(raw) == "OR")
        {
            return new Token(TokenKind.Or, raw);
        }

        for (var colon = 0; colon < raw.Length; colon++)
        {
            if (FoldSymbol(raw[colon]) != ':')
            {
                continue;
            }

            if (colon > 0 && FieldNames.TryGetValue(Normalize(raw[..colon]), out var field))
            {
                var rest = raw[(colon + 1)..];
                return rest.Length == 0
                    ? new Token(TokenKind.Prefix, raw, field)
                    : new Token(TokenKind.Word, rest, field);
            }

            break;
        }

        return new Token(TokenKind.Word, raw);
    }

    // --- 構文 ---
    //
    // or   := and ( "OR" and )*
    // and  := unary+          （並べただけでAND）
    // unary:= "-" unary | 前置き unary | primary
    // prim := "(" or ")" | 語

    private static SearchNode? ParseOr(List<Token> tokens, ref int index)
    {
        var parts = new List<SearchNode>();
        var first = ParseAnd(tokens, ref index);
        if (first is not null)
        {
            parts.Add(first);
        }

        while (index < tokens.Count && tokens[index].Kind == TokenKind.Or)
        {
            index++;
            var next = ParseAnd(tokens, ref index);
            if (next is not null)
            {
                parts.Add(next);
            }
        }

        // 片側しかない OR は、書きかけとみなして無視する
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new SearchNode.Or(parts),
        };
    }

    private static SearchNode? ParseAnd(List<Token> tokens, ref int index)
    {
        var parts = new List<SearchNode>();

        while (index < tokens.Count && tokens[index].Kind is not (TokenKind.Or or TokenKind.Close))
        {
            var start = index;
            var part = ParseUnary(tokens, ref index);
            if (part is not null)
            {
                parts.Add(part);
            }
            else if (index == start)
            {
                break;
            }

            // 読み進めたのに何も無かった物（空の「()」・打ちかけの「-」「name:」）は無いものとして次へ進む。
            // 前はここで止めていたので「() 冬」の「冬」が捨てられ、全件に当たっていた（点検 2026-09-23）
        }

        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => new SearchNode.And(parts),
        };
    }

    private static SearchNode? ParseUnary(List<Token> tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            return null;
        }

        var token = tokens[index];

        if (token.Kind == TokenKind.Minus)
        {
            index++;
            var inner = ParseUnary(tokens, ref index);
            return inner is null ? null : new SearchNode.Not(inner);
        }

        if (token.Kind == TokenKind.Prefix)
        {
            index++;
            var inner = ParseUnary(tokens, ref index);

            // 打ちかけの「name:」だけは落とす。語として探すとどこにも当たらず0件になる
            return inner is null ? null : WithField(inner, token.Field!.Value);
        }

        if (token.Kind == TokenKind.Open)
        {
            index++;
            var inner = ParseOr(tokens, ref index);

            if (index < tokens.Count && tokens[index].Kind == TokenKind.Close)
            {
                index++;
            }

            return inner;
        }

        if (token.Kind == TokenKind.Word)
        {
            index++;
            return new SearchNode.Term(Normalize(token.Raw), token.Raw, token.Field);
        }

        return null;
    }

    /// <summary>前置きを括弧やフレーズの中の語に当てる。中で別の前置きを書いた語はそちらを優先する。</summary>
    private static SearchNode WithField(SearchNode node, SearchField field) => node switch
    {
        SearchNode.Term { Field: null } term => term with { Field = field },
        SearchNode.Not not => new SearchNode.Not(WithField(not.Inner, field)),
        SearchNode.And and => new SearchNode.And(and.Parts.Select(part => WithField(part, field)).ToList()),
        SearchNode.Or or => new SearchNode.Or(or.Parts.Select(part => WithField(part, field)).ToList()),
        _ => node,
    };
}
