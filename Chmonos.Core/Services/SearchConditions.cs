using System.Globalization;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品の記録の外にある、検索欄の記法で見る事実（ユーザ判断 2026-10-08・記法の追加）。
/// 対応アバターの名前は登録簿と素体の索引から、未読の更新は知らせの表から引くので、商品の記録が同じでも答えが変わる。
/// 渡さなければ <c>avatar:</c> と <c>has:update</c> はどの商品にも当たらない（タグの管理などの検索欄）。
/// </summary>
public sealed class SearchFacts
{
    private readonly Func<AvatarCompatibilityIndex> _index;
    private readonly Func<AvatarRegistry> _registry;
    private readonly Func<string, bool> _hasUnreadUpdate;
    private AvatarCompatibilityIndex? _builtIndex;
    private Dictionary<string, string[]>? _avatarNames;
    private Dictionary<string, string[]>? _baseNames;

    /// <param name="index">素体経由の対応の索引。絞り込みの条件と同じ物を渡す（同じ答えにするため）。</param>
    /// <param name="registry">アバターの登録簿。名前と呼び方を引く。<c>avatar:</c> を使ったときだけ読む。</param>
    /// <param name="hasUnreadUpdate">未読の「商品の更新」の知らせがあるか（条件「更新通知あり」とカードの札と同じ表）。</param>
    public SearchFacts(Func<AvatarCompatibilityIndex> index, Func<AvatarRegistry> registry, Func<string, bool> hasUnreadUpdate)
    {
        _index = index;
        _registry = registry;
        _hasUnreadUpdate = hasUnreadUpdate;
    }

    internal bool HasUnreadUpdate(string itemId) => _hasUnreadUpdate(itemId);

    /// <summary>
    /// 対応アバターとして探す名前。絞り込みの「対応アバター」の既定（素体経由も含める）と同じ範囲にする：
    /// 対応しているアバターの名前・正式名・呼び方・ID、宣言した素体と、対応しているアバターが属する素体の名前と呼び方。
    /// 消した対応と、説明文のリンク（要確認）は数えない（<see cref="AvatarCompatibilityIndex.Resolve"/> と同じ）
    /// </summary>
    internal IEnumerable<string> AvatarNames(ItemRecord item)
    {
        var index = _builtIndex ??= _index();
        if (_avatarNames is null || _baseNames is null)
        {
            BuildNames(_registry());
        }

        foreach (var avatarId in index.Resolve(item.Local).Keys)
        {
            yield return avatarId;
            if (_avatarNames!.TryGetValue(avatarId, out var names))
            {
                foreach (var name in names)
                {
                    yield return name;
                }
            }

            if (index.BaseNameOf(avatarId) is { } baseName)
            {
                foreach (var name in BaseNames(baseName))
                {
                    yield return name;
                }
            }
        }

        foreach (var declared in item.Local.AvatarBases)
        {
            if (!declared.Rejected)
            {
                foreach (var name in BaseNames(declared.BaseName))
                {
                    yield return name;
                }
            }
        }
    }

    private IEnumerable<string> BaseNames(string baseName)
        => _baseNames!.TryGetValue(baseName, out var names) ? names : [baseName];

    private void BuildNames(AvatarRegistry registry)
    {
        _avatarNames = registry.Entries.ToDictionary(
            entry => entry.ItemId,
            entry => new[] { Services.AvatarNames.ShownName(entry), entry.BoothName }
                .Concat(entry.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text))
                .OfType<string>()
                .Where(name => name.Length > 0)
                .ToArray(),
            StringComparer.Ordinal);

        _baseNames = new Dictionary<string, string[]>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in registry.BaseGroups.Where(group => !group.Rejected && !string.IsNullOrWhiteSpace(group.Name)))
        {
            _baseNames[group.Name] = group.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text).Prepend(group.Name).ToArray();
        }
    }
}

/// <summary>
/// 検索欄の記法のうち、文字ではなく商品の状態・数で当てる物（ユーザ判断 2026-10-08）。
/// <c>is:</c>・<c>has:</c> は絞り込みの条件と**同じ式**で見る（カードの札・条件の件数と、文字で探した件数をずらさない）。
/// <c>paid:</c>（払った額）・<c>price:</c>（BOOTH の価格）・<c>wish:</c>（スキ数）は数の範囲で見る。
/// </summary>
public static class SearchConditions
{
    /// <summary><c>is:</c> の語（畳んだ形）。ここに無い語はどの商品にも当たらない。</summary>
    public static IReadOnlyList<string> IsWords { get; } = ["favorite", "hidden", "local", "owned", "r18", "delisted"];

    /// <summary><c>has:</c> の語（畳んだ形）。</summary>
    public static IReadOnlyList<string> HasWords { get; } = ["update", "brokenzip", "missing"];

    /// <summary>文字の照らし方を使わず、ここで当てる前置きか。別表記で広げない（数や決まった語なので）。</summary>
    public static bool IsCondition(SearchField field)
        => field is SearchField.Is or SearchField.Has or SearchField.Paid or SearchField.Price or SearchField.Wish or SearchField.Avatar
            or SearchField.UserTag;

    /// <summary>答えが商品の記録の外の事実で変わる前置きか（覚えた答えを使い回せない）。</summary>
    internal static bool DependsOnFacts(SearchField field) => field is SearchField.Avatar or SearchField.Has;

    internal static bool Matches(SearchNode.Term term, ItemRecord? item, SearchFacts? facts)
    {
        if (item is null)
        {
            return false;
        }

        return term.Field switch
        {
            SearchField.Is => MatchesIs(term.Text, item),
            SearchField.Has => MatchesHas(term.Text, item, facts),
            SearchField.Paid => TryParseRange(term.Text, out var paid)
                && Purchases.SelfPaidOrNull(item) is { } amount && paid.Contains(amount),
            SearchField.Price => TryParseRange(term.Text, out var price)
                && item.Booth.Variations.Any(variation => price.Contains(variation.Price)),
            SearchField.Wish => TryParseRange(term.Text, out var wish) && wish.Contains(item.Booth.WishListsCount),
            SearchField.UserTag => MatchesUserTag(term.Text, item),
            SearchField.Avatar => facts is not null && term.Text.Length > 0
                && facts.AvatarNames(item).Any(name => SearchQuery.Normalize(name).Contains(term.Text, StringComparison.Ordinal)),
            _ => false,
        };
    }

    /// <summary>
    /// ユーザータグ（ユーザ判断 2026-10-08）。名前は**完全一致**で、<c>*</c> を書いた所だけ何文字でもよい。
    /// 自分で付けた名前なので、ぴったり指せる方が役に立つ（含むで当てると「衣装」が「衣装小物」にも当たる）。
    /// <c>大分類/小分類</c>・<c>大分類/</c>（小分類は問わない）・<c>/小分類</c>（大分類は問わない）。区切りの無い名前は大分類か小分類のどちらか。
    /// 区切りはタグの管理の見せ方（「大分類 / 小分類」）に合わせて「/」。全角の「／」も畳まれて同じになる
    /// </summary>
    private static bool MatchesUserTag(string text, ItemRecord item)
    {
        var slash = text.IndexOf('/');
        if (slash < 0)
        {
            var name = text.Trim();
            return name.Length > 0 && item.Local.UserTags.Any(tag => Like(tag.Top, name) || tag.Subs.Any(sub => Like(sub, name)));
        }

        var top = text[..slash].Trim();
        var sub = text[(slash + 1)..].Trim();
        return item.Local.UserTags.Any(tag =>
            (top.Length == 0 || Like(tag.Top, top))
            && (sub.Length == 0 || tag.Subs.Any(name => Like(name, sub))));
    }

    /// <summary>名前が型に合うか。型は畳んである。<c>*</c> は0文字以上の何でも</summary>
    internal static bool Like(string name, string pattern)
    {
        var folded = SearchQuery.Normalize(name);
        if (!pattern.Contains('*'))
        {
            return folded == pattern;
        }

        var pieces = pattern.Split('*');
        if (!folded.StartsWith(pieces[0], StringComparison.Ordinal) || !folded.EndsWith(pieces[^1], StringComparison.Ordinal)
            || folded.Length < pieces[0].Length + pieces[^1].Length)
        {
            return false;
        }

        var at = pieces[0].Length;
        var end = folded.Length - pieces[^1].Length;
        for (var i = 1; i < pieces.Length - 1; i++)
        {
            var found = folded.IndexOf(pieces[i], at, end - at, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            at = found + pieces[i].Length;
        }

        return true;
    }

    // 絞り込みの条件（SearchViewModel.CreateModule）と同じ式。値を変えるときは両方を見る
    private static bool MatchesIs(string word, ItemRecord item) => word switch
    {
        "favorite" => item.Local.IsFavorite,
        "hidden" => item.Local.IsHidden,
        "local" => item.IsLocalOnly,

        // 所持の定義そのまま（条件「所持」の「すべて見つからない商品は未所持とする」の既定＝切と同じ）
        "owned" => item.IsOwned,
        "r18" or "r-18" => item.Booth.IsAdult,

        // 条件「公開状況」の「販売終了・非公開」と同じ。BOOTHに無い商品（仮ID）は公開状況を持たないので入れない
        "delisted" => !item.IsLocalOnly && (item.Booth.IsEndOfSale || item.Local.IsDelisted),
        _ => false,
    };

    private static bool MatchesHas(string word, ItemRecord item, SearchFacts? facts) => word switch
    {
        "update" => facts?.HasUnreadUpdate(item.Id) == true,
        "brokenzip" => item.HasBrokenArchive,

        // 条件「見つからないファイル」の既定（未所持も含める＝全部見つからない商品も入れる）と同じ
        "missing" => item.HasMissingFile,
        _ => false,
    };

    /// <summary>数の範囲（両端を含む）。片側が無ければ、その側は限りが無い。</summary>
    public readonly record struct NumberRange(long? Min, long? Max)
    {
        public bool Contains(long value) => (Min is null || value >= Min) && (Max is null || value <= Max);
    }

    /// <summary>
    /// 範囲の書き方を読む（ユーザ判断 2026-10-08）。<c>-400</c>（以下）・<c>100-500</c>・<c>500-</c>（以上）・<c>&lt;=400</c>・<c>&gt;=500</c>・
    /// <c>&lt;400</c>・<c>&gt;500</c>・<c>500</c>（ちょうど）。逆向き（<c>500&lt;=</c>）は受けない（「以上」とも「以下」とも読めるため）。
    /// 区切りは日本語入力のまま打てるよう、長音（ー）と波ダッシュ（〜・～）も受ける。桁区切りの「,」と後ろの「円」は読み飛ばす。
    /// </summary>
    /// <param name="text">畳んだ語（NFKC＋小文字。全角の数字・ハイフン・不等号は半角になっている）。</param>
    public static bool TryParseRange(string text, out NumberRange range)
    {
        range = default;
        var value = text.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (value.EndsWith('円'))
        {
            value = value[..^1];
        }

        foreach (var dash in new[] { 'ー', '〜', '~', '−' })
        {
            value = value.Replace(dash, '-');
        }

        if (value.StartsWith(">=", StringComparison.Ordinal))
        {
            return TryNumber(value[2..], out var min) && Set(new NumberRange(min, null), out range);
        }

        if (value.StartsWith("<=", StringComparison.Ordinal))
        {
            return TryNumber(value[2..], out var max) && Set(new NumberRange(null, max), out range);
        }

        if (value.StartsWith('>'))
        {
            return TryNumber(value[1..], out var min) && Set(new NumberRange(min + 1, null), out range);
        }

        if (value.StartsWith('<'))
        {
            return TryNumber(value[1..], out var max) && Set(new NumberRange(null, max - 1), out range);
        }

        var dashAt = value.IndexOf('-');
        if (dashAt < 0)
        {
            return TryNumber(value, out var exact) && Set(new NumberRange(exact, exact), out range);
        }

        var low = value[..dashAt];
        var high = value[(dashAt + 1)..];
        long? lowValue = null;
        long? highValue = null;
        if (low.Length > 0)
        {
            if (!TryNumber(low, out var parsed))
            {
                return false;
            }

            lowValue = parsed;
        }

        if (high.Length > 0)
        {
            if (!TryNumber(high, out var parsed))
            {
                return false;
            }

            highValue = parsed;
        }

        return (lowValue is not null || highValue is not null) && Set(new NumberRange(lowValue, highValue), out range);
    }

    private static bool TryNumber(string text, out long value)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool Set(NumberRange made, out NumberRange range)
    {
        range = made;
        return true;
    }
}
