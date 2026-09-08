using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 検索対象の文字列。3つに分けているのは、探す範囲をトグルで広げられるようにするため。
///
/// 1本に繋いでから渡す形にしないのは、繋ぐ処理が入力1文字ごとに全商品ぶん走るため。
/// 分けたまま持って、項ごとに「どれかに含まれるか」を見る。
/// </summary>
public sealed record SearchHaystack
{
    /// <summary>既定で探す範囲。商品名／ショップ名／サブドメイン／メモ／BOOTHタグ。</summary>
    public required string Primary { get; init; }

    /// <summary>「本文も探す」で加わる範囲。説明文とh2セクション。</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>「ファイルのパスも探す」で加わる範囲。</summary>
    public string Paths { get; init; } = string.Empty;
}

/// <summary>
/// 検索式の1ノード。
///
/// スペース＝AND、<c>-語</c>＝除外、<c>"..."</c>＝フレーズ、<c>OR</c>、<c>( )</c> で優先順位。
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

    /// <summary>1つの語、またはフレーズ。<see cref="Text"/> は正規化済み。</summary>
    public sealed record Term(string Text) : SearchNode;

    public sealed record Not(SearchNode Inner) : SearchNode;

    public sealed record And(IReadOnlyList<SearchNode> Parts) : SearchNode;

    public sealed record Or(IReadOnlyList<SearchNode> Parts) : SearchNode;
}

/// <summary>
/// 検索文字列の解釈。
///
/// 全角で入力されがちなので、先にNFKCで畳んでから読む。
/// これで全角の括弧・引用符・ハイフン・空白がすべて半角と同じ扱いになる
/// （日本語入力では全角のまま打たれる方が普通なので、ここを外すと構文が動かない）。
/// </summary>
public static class SearchQuery
{
    /// <summary>
    /// 比較用に文字列を畳む。NFKC＋小文字化。
    /// 探す側と探される側の両方に同じものを掛けることで、
    /// 全角と半角、大文字と小文字の違いを気にせず打てるようにする。
    /// </summary>
    public static string Normalize(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    public static SearchNode Parse(string? query)
    {
        var normalized = Normalize(query);
        if (normalized.Trim().Length == 0)
        {
            return new SearchNode.All();
        }

        var tokens = Tokenize(normalized);
        var index = 0;
        var node = ParseOr(tokens, ref index);

        // 閉じ括弧が余っていても止めない。打ちかけの入力でも結果が出続ける方がよい
        return node ?? new SearchNode.All();
    }

    public static bool Matches(SearchNode node, SearchHaystack haystack, bool includeBody, bool includePaths)
        => node switch
        {
            SearchNode.All => true,
            SearchNode.Term term => Contains(term.Text, haystack, includeBody, includePaths),
            SearchNode.Not not => !Matches(not.Inner, haystack, includeBody, includePaths),
            SearchNode.And and => and.Parts.All(part => Matches(part, haystack, includeBody, includePaths)),
            SearchNode.Or or => or.Parts.Any(part => Matches(part, haystack, includeBody, includePaths)),
            _ => true,
        };

    private static bool Contains(string term, SearchHaystack haystack, bool includeBody, bool includePaths)
        => haystack.Primary.Contains(term, StringComparison.Ordinal)
            || (includeBody && haystack.Body.Contains(term, StringComparison.Ordinal))
            || (includePaths && haystack.Paths.Contains(term, StringComparison.Ordinal));

    // --- 字句 ---

    private enum TokenKind
    {
        Word,
        Or,
        Minus,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    /// <summary>
    /// 引用符は <c>"</c> だけを見る。日本語の「」は商品名にそのまま出てくるので
    /// （『「タマクラゲ」』のように）、フレーズの印にすると打った通りに探せなくなる。
    /// </summary>
    private static bool IsQuote(char c) => c is '"' or '“' or '”';

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
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
                var hasTarget = i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && text[i + 1] != ')';

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
                while (i < text.Length && !IsQuote(text[i]))
                {
                    i++;
                }

                var phrase = text[start..i];
                if (i < text.Length)
                {
                    i++;
                }

                // 閉じ忘れでも、そこまでをフレーズとして扱う
                if (phrase.Length > 0)
                {
                    tokens.Add(new Token(TokenKind.Word, phrase));
                }

                continue;
            }

            var wordStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && !IsQuote(text[i]))
            {
                i++;
            }

            var word = text[wordStart..i];
            tokens.Add(word == "or" ? new Token(TokenKind.Or, word) : new Token(TokenKind.Word, word));
        }

        return tokens;
    }

    // --- 構文 ---
    //
    // or   := and ( "OR" and )*
    // and  := unary+          （並べただけでAND）
    // unary:= "-" unary | primary
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
            var part = ParseUnary(tokens, ref index);
            if (part is null)
            {
                break;
            }

            parts.Add(part);
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

        if (tokens[index].Kind == TokenKind.Minus)
        {
            index++;
            var inner = ParseUnary(tokens, ref index);
            return inner is null ? null : new SearchNode.Not(inner);
        }

        var token = tokens[index];

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
            return new SearchNode.Term(token.Text);
        }

        return null;
    }
}
