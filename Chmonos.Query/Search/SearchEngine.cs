using System.Globalization;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query.Search;

/// <summary>検索（<c>POST /v1/search</c>）。</summary>
public sealed class SearchRequest
{
    public SearchExpression? Where { get; init; }

    /// <summary>非表示にした商品も返すか（既定 true。返した物には <c>isHidden</c> の印が付く。ユーザ判断 2026-10-12）。</summary>
    public bool IncludeHidden { get; init; } = true;

    /// <summary>R-18 の商品も返すか（既定 true。同上）。</summary>
    public bool IncludeAdult { get; init; } = true;

    public TextOptionsRequest? TextOptions { get; init; }

    /// <summary>並べ方。同じ値どうしは ID の順（並びは必ず一意）。</summary>
    public IReadOnlyList<SortRequest>? Sort { get; init; }

    /// <summary>前の答えの <c>nextCursor</c>。</summary>
    public string? Cursor { get; init; }

    /// <summary>1回に返す数（既定 100・最大 200）。</summary>
    public int? Limit { get; init; }
}

/// <param name="Field"><c>id</c>・<c>name</c>・<c>publishedAt</c>・<c>acquiredAt</c>・<c>wishCount</c>・<c>paid</c>・<c>fetchedAt</c>。</param>
/// <param name="Order"><c>asc</c>（既定）か <c>desc</c>。値の無い商品はどちらでも後ろ。</param>
public sealed record SortRequest(string Field, string? Order = null);

/// <param name="Total">式に当たった数の全部（この答えに入れた数ではない）。</param>
/// <param name="NextCursor">続きがあれば、次に送る印。無ければ null。</param>
public sealed record SearchPage(IReadOnlyList<ItemSummary> Items, int Total, string? NextCursor);

/// <summary>選択肢の件数（<c>POST /v1/search/facets</c>）。</summary>
public sealed class FacetsRequest
{
    public SearchExpression? Where { get; init; }

    public bool IncludeHidden { get; init; } = true;

    public bool IncludeAdult { get; init; } = true;

    public TextOptionsRequest? TextOptions { get; init; }

    /// <summary>数える欄（<c>category</c>・<c>boothTag</c>・<c>shop</c>・<c>avatar</c>・<c>userTag</c>・<c>attribute</c>）。</summary>
    public IReadOnlyList<string>? Fields { get; init; }
}

public sealed record FacetValue(string Value, string? Name, int Count);

/// <param name="Matched">対応が書いてある商品の数（対応アバターの「対応している」）。</param>
/// <param name="Unspecified">対応の指定が無い商品の数。</param>
public sealed record AvatarGroupCounts(int Matched, int Unspecified);

/// <param name="Total">式に当たった数（件数を数えた相手の数）。</param>
/// <param name="AvatarGroups">欄に <c>avatar</c> を入れたときだけ。</param>
public sealed record FacetsAnswer(int Total, IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Fields, AvatarGroupCounts? AvatarGroups);

/// <summary>
/// 検索の本体。式を組み（葉の判定は画面と同じ Core の物）、商品を絞り、並べ、続きの印で分けて返す。
/// 式の大きさに上限を置く（とても深い式・葉の多い式で止まらないように。docs/spec/local-api.md「守り」）。
/// </summary>
public static class SearchEngine
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;
    public const int MaxDepth = 32;
    public const int MaxLeaves = 200;

    /// <summary>
    /// 式の段（枝も葉も）の数の上限。葉だけを数えると、空の and・or を大量に並べて上限を越えられた（点検30）。
    /// 葉の上限 200 に、それを組む枝を足しても十分に収まる数
    /// </summary>
    public const int MaxNodes = 500;

    public static IReadOnlyList<string> FacetFields { get; } = ["category", "boothTag", "shop", "avatar", "userTag", "attribute"];

    private static readonly CompareInfo Japanese = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    public static SearchPage Search(QuerySnapshot snapshot, SearchRequest request, DateTimeOffset now)
    {
        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
        {
            throw QueryException.Invalid("invalidLimit", $"limit は 1〜{MaxLimit} です: {limit}");
        }

        var sort = SortOf(request.Sort);
        var matched = Filter(snapshot, request.Where, request.IncludeHidden, request.IncludeAdult, request.TextOptions, now);
        var ordered = matched
            .Select(item => (Item: item, Key: sort.Select(key => key.Value(item, snapshot)).ToArray()))
            .ToList();
        ordered.Sort((left, right) => Compare(left.Key, left.Item.Id, right.Key, right.Item.Id, sort));

        var start = 0;
        if (request.Cursor is { } cursor)
        {
            var (afterKey, afterId) = ReadCursor(cursor, sort);
            start = ordered.FindIndex(entry => Compare(entry.Key, entry.Item.Id, afterKey, afterId, sort) > 0);
            if (start < 0)
            {
                start = ordered.Count;
            }
        }

        var page = ordered.Skip(start).Take(limit).ToList();
        var next = start + page.Count < ordered.Count && page.Count > 0
            ? WriteCursor(page[^1].Key, page[^1].Item.Id, sort)
            : null;
        return new SearchPage(page.Select(entry => ItemMapper.Summary(entry.Item, snapshot)).ToList(), ordered.Count, next);
    }

    /// <summary>
    /// 選択肢の件数：式に当たった商品の中で、欄の値ごとの数。
    /// 画面は「自分の条件は自分を除いて数える」が、式は木なので「自分」を決められない。数えたい欄の条件を外した式を送ってもらう。
    /// </summary>
    public static FacetsAnswer Facets(QuerySnapshot snapshot, FacetsRequest request, DateTimeOffset now)
    {
        var fields = request.Fields ?? FacetFields;
        foreach (var field in fields)
        {
            if (!FacetFields.Contains(field))
            {
                throw QueryException.Invalid("unknownFacet", $"数えられない欄です: {field}（{string.Join("・", FacetFields)}）");
            }
        }

        var items = Filter(snapshot, request.Where, request.IncludeHidden, request.IncludeAdult, request.TextOptions, now);
        var context = snapshot.ConditionContext(now);
        var answer = new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            answer[field] = field switch
            {
                "category" => Count(items, item => item.CategoryName is { } name ? [name] : []),
                "boothTag" => Count(items, item => item.Booth.Tags),
                "shop" => Count(items, item => item.ShopSubdomain is { } key ? [key] : [], key => snapshot.Items.FirstOrDefault(item => item.ShopSubdomain == key)?.ShopName),
                "avatar" => Count(items, item => snapshot.Compatibility.Resolve(item.Local).Keys, id => snapshot.AvatarNames.GetValueOrDefault(id)),
                "userTag" => Count(items, item => item.Local.UserTags.SelectMany(tag => tag.Subs.Select(sub => $"{tag.Top}/{sub}").Prepend(tag.Top)).Distinct(StringComparer.Ordinal)),
                _ => Count(items, item => item.Local.Attributes.Keys),
            };
        }

        AvatarGroupCounts? groups = null;
        if (fields.Contains("avatar"))
        {
            var unspecified = items.Count(item => SearchModuleConditions.IsUnspecifiedAvatar(item, context));
            groups = new AvatarGroupCounts(items.Count - unspecified, unspecified);
        }

        return new FacetsAnswer(items.Count, answer, groups);
    }

    /// <summary>式に当たる商品（並びは ID の順）。非表示・R-18 は式より先に外す（画面と同じ順）。</summary>
    public static IReadOnlyList<ItemRecord> Filter(
        QuerySnapshot snapshot,
        SearchExpression? where,
        bool includeHidden,
        bool includeAdult,
        TextOptionsRequest? textOptions,
        DateTimeOffset now)
    {
        var context = snapshot.ConditionContext(now);
        var options = OptionsOf(textOptions);
        var budget = new ExpressionBudget();
        var compiled = where is null ? new CompiledExpression.All() : Compile(where, snapshot, context, options, budget, 1);

        // 検索用の文字列は、その対象を初めて探すときに畳んで覚える（SearchHaystack）。同時に来た問い合わせが同じ物を畳み合わないよう、1本ずつ照らす
        lock (snapshot.SearchGate)
        {
            return snapshot.Items
                .Where(item => (includeHidden || !item.Local.IsHidden) && (includeAdult || !item.Booth.IsAdult))
                .Where(item => compiled.Matches(item, negated: false))
                .ToList();
        }
    }

    private static CompiledExpression Compile(
        SearchExpression expression,
        QuerySnapshot snapshot,
        SearchModuleContext context,
        SearchOptions options,
        ExpressionBudget budget,
        int depth)
    {
        // and・or の中の null は入力の誤り（点検32：読む所で落ち、内部の誤りとして 500 を返していた）
        if (expression is null)
        {
            throw QueryException.Invalid("invalidExpression", "式に null は書けません。");
        }

        if (depth > MaxDepth)
        {
            throw QueryException.Invalid("expressionTooDeep", $"式が深すぎます（{MaxDepth} 段まで）。");
        }

        if (++budget.Nodes > MaxNodes)
        {
            throw QueryException.Invalid("expressionTooLarge", $"式が大きすぎます（and・or・not と葉を合わせて {MaxNodes} まで）。");
        }

        var written = (expression.And is not null ? 1 : 0) + (expression.Or is not null ? 1 : 0) + (expression.Not is not null ? 1 : 0)
            + (expression.Text is not null ? 1 : 0) + (expression.Condition is not null ? 1 : 0);
        if (written != 1)
        {
            throw QueryException.Invalid("invalidExpression", "式の1段には and・or・not・text・condition のどれか1つだけを書きます。");
        }

        if (expression.And is { } and)
        {
            var children = new List<CompiledExpression>(and.Count);
            foreach (var child in and)
            {
                children.Add(Compile(child, snapshot, context, options, budget, depth + 1));
            }

            return new CompiledExpression.And(children);
        }

        if (expression.Or is { } or)
        {
            var children = new List<CompiledExpression>(or.Count);
            foreach (var child in or)
            {
                children.Add(Compile(child, snapshot, context, options, budget, depth + 1));
            }

            return new CompiledExpression.Or(children);
        }

        if (expression.Not is { } not)
        {
            return new CompiledExpression.Not(Compile(not, snapshot, context, options, budget, depth + 1));
        }

        if (++budget.Leaves > MaxLeaves)
        {
            throw QueryException.Invalid("expressionTooLarge", $"式の葉が多すぎます（{MaxLeaves} まで）。");
        }

        if (expression.Text is { } text)
        {
            return new CompiledExpression.Text(SearchQuery.Parse(text), snapshot.HaystackOf, options, snapshot.Facts);
        }

        var state = ConditionKinds.ToState(expression.Condition!);
        var kind = Enum.Parse<SearchModuleKind>(state.Kind);
        return new CompiledExpression.Condition(kind, SearchModuleConditions.Build(state, context)!, context);
    }

    private static SearchOptions OptionsOf(TextOptionsRequest? request)
    {
        if (request is null)
        {
            return SearchOptions.Default;
        }

        var targets = SearchOptions.DefaultTargets;
        if (request.Targets is { } names)
        {
            var fields = new HashSet<SearchField>();
            foreach (var name in names)
            {
                fields.Add(SearchQuery.FieldNames.TryGetValue(name, out var field)
                    ? field
                    : throw QueryException.Invalid("unknownTarget", $"知らない対象です: {name}（{string.Join("・", SearchQuery.FieldNames.Keys)}）"));
            }

            targets = fields;
        }

        return new SearchOptions
        {
            Targets = targets,
            CaseSensitive = request.CaseSensitive,
            WidthSensitive = request.WidthSensitive,
            KanaSensitive = request.KanaSensitive,
        };
    }

    private static List<FacetValue> Count(IReadOnlyList<ItemRecord> items, Func<ItemRecord, IEnumerable<string>> valuesOf, Func<string, string?>? nameOf = null)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var value in valuesOf(item).Distinct(StringComparer.Ordinal))
            {
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new FacetValue(pair.Key, nameOf?.Invoke(pair.Key), pair.Value))
            .ToList();
    }

    /// <summary>並べる値（数か文字か無し）。値の無い商品はどの向きでも後ろ。</summary>
    internal readonly record struct SortValue(long? Number, string? Text)
    {
        public bool IsEmpty => Number is null && Text is null;
    }

    internal sealed record SortKey(string Field, bool Descending, Func<ItemRecord, QuerySnapshot, SortValue> Value);

    private static List<SortKey> SortOf(IReadOnlyList<SortRequest>? requests)
    {
        var keys = new List<SortKey>();
        foreach (var request in requests ?? [])
        {
            if (request is null)
            {
                throw QueryException.Invalid("invalidSort", "sort に null は書けません。");
            }

            var descending = request.Order switch
            {
                null or "asc" => false,
                "desc" => true,
                _ => throw QueryException.Invalid("invalidSort", $"order は asc か desc です: {request.Order}"),
            };

            Func<ItemRecord, QuerySnapshot, SortValue> value = request.Field switch
            {
                "id" => (item, _) => new SortValue(null, item.Id),
                "name" => (item, _) => new SortValue(null, item.DisplayName),
                "publishedAt" => (item, _) => new SortValue(item.Booth.PublishedAt?.UtcTicks, null),
                "acquiredAt" => (item, _) => new SortValue(PurchaseDates.EnteredEarliest(item)?.DayNumber, null),
                "wishCount" => (item, _) => new SortValue(item.Booth.WishListsCount, null),
                "paid" => (item, _) => new SortValue(Purchases.SelfPaidOrNull(item), null),
                "fetchedAt" => (item, _) => new SortValue(item.Booth.FetchedAt?.UtcTicks, null),
                _ => throw QueryException.Invalid("invalidSort", $"並べられない欄です: {request.Field}"),
            };

            keys.Add(new SortKey(request.Field, descending, value));
        }

        return keys;
    }

    private static int Compare(SortValue[] left, string leftId, SortValue[] right, string rightId, IReadOnlyList<SortKey> sort)
    {
        for (var index = 0; index < sort.Count; index++)
        {
            var (a, b) = (left[index], right[index]);
            if (a.IsEmpty || b.IsEmpty)
            {
                // 値の無い商品は向きを問わず後ろ
                var empty = a.IsEmpty.CompareTo(b.IsEmpty);
                if (empty != 0)
                {
                    return empty;
                }

                continue;
            }

            var result = a.Number is { } x && b.Number is { } y
                ? x.CompareTo(y)
                : Japanese.Compare(a.Text, b.Text, CompareOptions.None);
            if (result != 0)
            {
                return sort[index].Descending ? -result : result;
            }
        }

        // 同じ値どうしは ID の順（並びを必ず一意にする。続きの印がずれないように）
        return string.CompareOrdinal(leftId, rightId);
    }

    /// <summary>続きの印：最後に返した商品の並べる値と ID と、並べ方の名前（並べ方を変えて送られたら断る）。中身は約束にしない。</summary>
    private static string WriteCursor(SortValue[] key, string id, IReadOnlyList<SortKey> sort)
    {
        var cursor = new PageCursor(
            string.Join(",", sort.Select(entry => entry.Field + (entry.Descending ? ":desc" : string.Empty))),
            key.Select(value => new PageCursorValue(value.Number, value.Text)).ToList(),
            id);
        var json = JsonSerializer.SerializeToUtf8Bytes(cursor, CursorJsonContext.Default.PageCursor);
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static (SortValue[] Key, string Id) ReadCursor(string text, IReadOnlyList<SortKey> sort)
    {
        try
        {
            var base64 = text.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var cursor = JsonSerializer.Deserialize(Convert.FromBase64String(base64), CursorJsonContext.Default.PageCursor)
                ?? throw new FormatException();
            var signature = string.Join(",", sort.Select(entry => entry.Field + (entry.Descending ? ":desc" : string.Empty)));
            if (cursor.Sort != signature || cursor.Values.Count != sort.Count)
            {
                throw QueryException.Invalid("invalidCursor", "続きの印は、同じ並べ方の問い合わせにだけ使えます。");
            }

            return (cursor.Values.Select(value => new SortValue(value.Number, value.Text)).ToArray(), cursor.Id);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            throw QueryException.Invalid("invalidCursor", "続きの印が読めません。");
        }
    }
}

/// <summary>式を組むときに数える物（葉と、枝も含めた段）。</summary>
internal sealed class ExpressionBudget
{
    public int Leaves { get; set; }

    public int Nodes { get; set; }
}

internal sealed record PageCursor(string Sort, IReadOnlyList<PageCursorValue> Values, string Id);

/// <summary>続きの印の書き方（外に見せない型なので、答えの書き方とは分ける）。</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(PageCursor))]
internal sealed partial class CursorJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

internal sealed record PageCursorValue(long? Number, string? Text);
