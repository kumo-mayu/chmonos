using System.Globalization;
using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

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

    // ここから下は検索欄の記法で指す物（ユーザ判断 2026-10-08）。「対象」の切り替えには出さない

    /// <summary>ユーザータグ（完全一致・<c>*</c>・<c>大分類/小分類</c>。<see cref="SearchConditions"/>）。</summary>
    UserTag,

    /// <summary>対応アバター（名前・呼び方・素体の名前）。登録簿から引く（<see cref="SearchFacts"/>）。</summary>
    Avatar,

    /// <summary>カテゴリ（条件「カテゴリ」と同じく、自分で入れたカテゴリか BOOTH のカテゴリと、その親）。</summary>
    Category,

    /// <summary>商品の状態（<c>is:favorite</c> など・<see cref="SearchConditions"/>）。</summary>
    Is,

    /// <summary>商品が持つ物（<c>has:update</c> など）。</summary>
    Has,

    /// <summary>払った額の範囲。</summary>
    Paid,

    /// <summary>BOOTH の価格の範囲（種類のどれかが入れば当たる）。</summary>
    Price,

    /// <summary>スキ数の範囲。</summary>
    Wish,
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

    private readonly EqualityNeutral<IReadOnlySet<SearchField>, SearchField[]> _targetList = new();

    /// <summary>
    /// <see cref="Targets"/> を配列にした物。<c>IReadOnlySet</c> を foreach で回すと、商品×語ごとに列挙子の箱が1つできていた。
    /// <c>with</c> で対象を差し替えた写しでも、元の集合が同じ時だけ使い回す。
    /// </summary>
    internal SearchField[] TargetList => _targetList.Of(Targets, static set => set.ToArray());
}

/// <summary>
/// 値から作った物を1つだけ覚える。**record の等しさに加わらない**（いつも等しい）——検索の式や切り替えは
/// record なので、覚えた物で「等しいか」が変わると、同じ式が別物になる。<c>with</c> で写した先とは覚えた物を分け合うが、
/// 元の値が同じ参照の時だけ使うので、写しで値を変えても古い物は返さない。
/// </summary>
internal sealed class EqualityNeutral<TKey, TValue>
    where TKey : class
{
    private Entry? _entry;

    public TValue Of(TKey key, Func<TKey, TValue> make)
    {
        // 覚えた物は1つの参照でまとめて差し替える（別のスレッドから来ても、鍵と値の組が崩れない）
        if (_entry is { } entry && ReferenceEquals(entry.Key, key))
        {
            return entry.Value;
        }

        var value = make(key);
        _entry = new Entry(key, value);
        return value;
    }

    public override bool Equals(object? obj) => obj is EqualityNeutral<TKey, TValue>;

    public override int GetHashCode() => 0;

    private sealed record Entry(TKey Key, TValue Value);
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
        _readings = readings;
    }

    /// <param name="raw">対象ごとの元の文字列。</param>
    /// <param name="makeReadings">商品名の読み（畳む前）を作る。<see cref="Readings"/> を初めて見たときに1度だけ呼ぶ。</param>
    public SearchHaystack(Func<SearchField, IReadOnlyList<string>> raw, Func<string>? makeReadings)
    {
        _raw = raw;
        _makeReadings = makeReadings;
        _readings = makeReadings is null ? string.Empty : null;
    }

    private readonly Func<string>? _makeReadings;
    private string? _readings;

    /// <summary>
    /// 元の商品。状態・数で当てる記法（<c>is:</c>・<c>paid:</c> など）が見る。ショップの一覧などの商品でない材料では null（当たらない）
    /// </summary>
    public ItemRecord? Item { get; init; }

    /// <summary>
    /// 商品名の読み（畳み済み・ひらがな）。造語変換のときだけ見るので、**見たときに作る**
    /// （既定の検索では全商品で作らずに済む）。
    /// </summary>
    public string Readings
    {
        get
        {
            if (_readings is { } ready)
            {
                return ready;
            }

            // 同時に2つのスレッドから来ても、どちらも同じ文字列を作るだけ（作る物は商品の記録から決まる）
            var made = SearchQuery.Normalize(_makeReadings!());
            _readings = made;
            return made;
        }
    }

    /// <summary>試験用：対象ごとの文字列から作る。</summary>
    public static SearchHaystack FromValues(IReadOnlyDictionary<SearchField, string[]> values, string readings = "")
        => new(field => values.TryGetValue(field, out var list) ? list : [], SearchQuery.Normalize(readings));

    private readonly IReadOnlyList<string>?[] _rawValues = new IReadOnlyList<string>?[FieldCount];

    /// <summary>
    /// 元の文字列。区別する切り替えのときに見る。**初めて見たときに控える**——前は照らすたびに商品の記録から
    /// 並びを作り直していて、全角半角を区別してファイル・パスを対象にすると2000件で1回 15ms・割り当て 2.3MB だった（2026-09-24 実測）。
    /// 控えるのは並びだけで、中の文字列は商品の記録と同じ物（ファイル名だけはパスから切り出した物）。
    /// </summary>
    public IReadOnlyList<string> Raw(SearchField field) => _rawValues[(int)field] ??= _raw(field);

    // 最後に照らした式と切り替えの組（SearchQuery.MatchKey）と、その答え。当たりと外れを別の欄に置くのは、
    // 答えと鍵を1つの参照の書き換えで決めるため（別のスレッドから照らしても組が崩れない）
    private object? _matchedFor;
    private object? _unmatchedFor;

    internal bool TryRecall(object key, out bool matched)
    {
        matched = ReferenceEquals(_matchedFor, key);
        return matched || ReferenceEquals(_unmatchedFor, key);
    }

    internal void Remember(object key, bool matched)
    {
        if (matched)
        {
            _matchedFor = key;
        }
        else
        {
            _unmatchedFor = key;
        }
    }

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
    public sealed record Term(string Text, string Raw, SearchField? Field = null, bool WholeWord = false) : SearchNode
    {
        /// <summary>
        /// 引用符で囲んで打った語か。**囲んだ語は別表記で広げない**（ユーザ判断 2026-10-08）——「その並びのまま含むものだけを探す」と説明していて、
        /// 多くの検索でも引用符は「打ったとおりに探す」。別表記を入れたままでも、この語だけは広げないと1語ずつ指せる
        /// </summary>
        public bool Quoted { get; init; }

        private readonly EqualityNeutral<string, string> _hiragana = new();

        /// <summary>
        /// 畳んだ語のカタカナをひらがなに寄せた物。前は商品×対象ごとに作り直していた（ひらがなとカタカナを区別しないとき・読みを見るとき）。
        /// </summary>
        internal string Hiragana => _hiragana.Of(Text, SearchQuery.ToHiragana);
    }

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
        ["usertag"] = SearchField.UserTag,
        ["avatar"] = SearchField.Avatar,
        ["category"] = SearchField.Category,
        ["is"] = SearchField.Is,
        ["has"] = SearchField.Has,
        ["paid"] = SearchField.Paid,
        ["price"] = SearchField.Price,
        ["wish"] = SearchField.Wish,
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

    /// <summary>
    /// 読む語の長さの上限。括弧と「-」は1つごとに1段深く読み進めるので、括弧を数千個貼るとスタックが溢れて落ちた（点検 2026-09-28）。
    /// 人が打つ検索の語は長くても数十字で、500字あれば足りる。深さもこの字数までに収まる
    /// </summary>
    public const int MaxQueryLength = 500;

    public static SearchNode Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchNode.All();
        }

        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
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

    /// <summary>
    /// 商品が式に当たるか。
    ///
    /// **同じ式と切り替えの組の答えは、商品ごとに覚えて使い回す。**検索画面は1回の絞り込みで、結果を出すのに1度、
    /// 選択肢の件数を数えるのに条件ごとに1度ずつ、同じ式で全商品を照らし直す（条件が3つなら4回）。
    /// 大文字小文字・全角半角を区別して本文を対象にすると1回の照合が2000件で約170ms かかり、それが（条件数＋1）倍になっていた（2026-09-24 実測）。
    /// 打ち直すと式が作り直されるので、覚えた答えは使われない（式と切り替えは参照が同じ時だけ同じ組とみなす）。
    /// </summary>
    /// <param name="facts">商品の記録の外の事実（対応アバターの名前・未読の更新）。無ければ <c>avatar:</c>・<c>has:</c> の一部が当たらない。</param>
    public static bool Matches(SearchNode node, SearchHaystack haystack, SearchOptions options, SearchFacts? facts = null)
    {
        if (node is SearchNode.All)
        {
            return true;
        }

        // 記録の外の事実で答えが変わる式は覚えない。材料は記録が同じ商品で使い回すので、既読にしてもアバターを登録し直しても、
        // 覚えた古い答えが返ってしまう
        if (DependsOnFacts(node))
        {
            return Evaluate(node, haystack, options, facts);
        }

        var key = MatchKey.For(node, options);
        if (haystack.TryRecall(key, out var matched))
        {
            return matched;
        }

        matched = Evaluate(node, haystack, options, facts);
        haystack.Remember(key, matched);
        return matched;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SearchNode, object> FactDependence = new();

    /// <summary>式が記録の外の事実（<see cref="SearchConditions.DependsOnFacts"/>）を見るか。式ごとに1回だけ調べる。</summary>
    public static bool DependsOnFacts(SearchNode node)
        => (bool)FactDependence.GetValue(node, static node => Walk(node));

    private static bool Walk(SearchNode node) => node switch
    {
        SearchNode.Term { Field: { } field } => SearchConditions.DependsOnFacts(field),
        SearchNode.Not not => Walk(not.Inner),
        SearchNode.And and => and.Parts.Any(Walk),
        SearchNode.Or or => or.Parts.Any(Walk),
        _ => false,
    };

    /// <summary>式のどこかに <c>is:</c> の語があるか（<c>is:hidden</c> で非表示の商品も照らすため）。否定の中も数える——外す側でも、照らさなければ外せない。</summary>
    public static bool Mentions(SearchNode node, SearchField field, string word) => node switch
    {
        SearchNode.Term term => term.Field == field && term.Text == word,
        SearchNode.Not not => Mentions(not.Inner, field, word),
        SearchNode.And and => and.Parts.Any(part => Mentions(part, field, word)),
        SearchNode.Or or => or.Parts.Any(part => Mentions(part, field, word)),
        _ => false,
    };

    /// <summary>
    /// 式と切り替えの組。直前の組と参照が同じなら同じ物を返す（1回の絞り込みの中では、全商品が同じ鍵を覚える）。
    /// スレッドごとに持つ——別のスレッドの照合と取り合って、毎回作り直しになるのを避ける。
    /// </summary>
    private sealed class MatchKey(SearchNode node, SearchOptions options)
    {
        [ThreadStatic]
        private static MatchKey? _last;

        public static MatchKey For(SearchNode node, SearchOptions options)
        {
            if (_last is { } last && ReferenceEquals(last._node, node) && ReferenceEquals(last._options, options))
            {
                return last;
            }

            return _last = new MatchKey(node, options);
        }

        private readonly SearchNode _node = node;
        private readonly SearchOptions _options = options;
    }

    // ラムダ（All・Any）を使わないのは、商品×ノードごとに閉包が1つできていたため
    private static bool Evaluate(SearchNode node, SearchHaystack haystack, SearchOptions options, SearchFacts? facts)
    {
        switch (node)
        {
            case SearchNode.Term term:
                return Contains(term, haystack, options, facts);

            case SearchNode.Not not:
                return !Evaluate(not.Inner, haystack, options, facts);

            case SearchNode.And and:
                for (var i = 0; i < and.Parts.Count; i++)
                {
                    if (!Evaluate(and.Parts[i], haystack, options, facts))
                    {
                        return false;
                    }
                }

                return true;

            case SearchNode.Or or:
                for (var i = 0; i < or.Parts.Count; i++)
                {
                    if (Evaluate(or.Parts[i], haystack, options, facts))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return true;
        }
    }

    private static bool Contains(SearchNode.Term term, SearchHaystack haystack, SearchOptions options, SearchFacts? facts)
    {
        if (term.Field is { } conditionField && SearchConditions.IsCondition(conditionField))
        {
            return SearchConditions.Matches(term, haystack.Item, facts);
        }

        if (term.Field is { } field)
        {
            return InField(term, haystack, field, options)
                || (field == SearchField.Name && InReadings(term, haystack, options));
        }

        foreach (var target in options.TargetList)
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
                : haystack.FoldedIgnoringKana(field).Contains(term.Hiragana, StringComparison.Ordinal);
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

        var values = haystack.Raw(field);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
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
                || haystack.Readings.Contains(term.Hiragana, StringComparison.Ordinal));

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

    private readonly record struct Token(TokenKind Kind, string Raw, SearchField? Field = null, bool Quoted = false);

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
                    tokens.Add(new Token(TokenKind.Word, phrase, Quoted: true));
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
            return new SearchNode.Term(Normalize(token.Raw), token.Raw, token.Field) { Quoted = token.Quoted };
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

/// <summary>
/// ショップの一覧の検索欄。検索画面と同じ書き方（除く <c>-</c>・ひとまとまり <c>"…"</c>・<c>OR</c>・括弧・前置き）を受け付ける
/// （ユーザ判断 2026-09-28）。前は打った文字をそのまま含むかだけを見ていて、同じアプリの中で検索欄ごとに書き方が違っていた。
/// 読み方と照らし方は検索画面の物をそのまま使い、探す材料だけをショップの物にする。
/// </summary>
public static class ShopSearch
{
    private static readonly SearchOptions NamesOnly = Make(names: true, memos: false);
    private static readonly SearchOptions MemosOnly = Make(names: false, memos: true);
    private static readonly SearchOptions Both = Make(names: true, memos: true);

    /// <summary>
    /// ショップ1件の探す材料。ショップ名は <c>shop:</c> でも <c>name:</c> でも探せる——ショップの一覧で「名前」と言えばショップ名なので。
    /// </summary>
    public static SearchHaystack Haystack(string name, string subdomain, string? memo)
    {
        string[] nameValues = string.IsNullOrEmpty(name) ? [] : [name];

        // 手で名前だけ入れたショップの鍵（local-…）は、人が打つサブドメインではない（商品の検索と同じ扱い）
        string[] subdomainValues = string.IsNullOrEmpty(subdomain) || LocalShopKey.IsLocal(subdomain) ? [] : [subdomain];
        string[] memoValues = string.IsNullOrEmpty(memo) ? [] : [memo];

        return new SearchHaystack(field => field switch
        {
            SearchField.Name or SearchField.Shop => nameValues,
            SearchField.Subdomain => subdomainValues,
            SearchField.Memo => memoValues,
            _ => [],
        });
    }

    /// <summary>
    /// 前置きの無い語を、ショップ名（とサブドメイン）とメモのどちらで探すか。
    /// 同じ組には同じ物を返す——照らした答えは式と切り替えの参照で覚えるので、打つたびに作り直すと覚えた答えが使われない。
    /// </summary>
    public static SearchOptions Options(bool names, bool memos) => (names, memos) switch
    {
        (true, false) => NamesOnly,
        (false, true) => MemosOnly,
        _ => Both,
    };

    /// <summary>ショップが式に当たるか。空の式なら全部当たる。</summary>
    public static bool Matches(SearchNode query, SearchHaystack shop, bool names, bool memos)
        => SearchQuery.Matches(query, shop, Options(names, memos));

    private static SearchOptions Make(bool names, bool memos)
    {
        var targets = new HashSet<SearchField>();
        if (names)
        {
            targets.Add(SearchField.Shop);
            targets.Add(SearchField.Subdomain);
        }

        if (memos)
        {
            targets.Add(SearchField.Memo);
        }

        return new SearchOptions { Targets = targets };
    }
}
