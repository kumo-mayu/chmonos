using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 絞り込みの条件1つの判定（2026-10-12・外から使う API のために画面の条件から分けた。`docs/spec/local-api.md`）。
///
/// 画面の条件（App の <c>SearchModule</c>）は値を持って見せる役、ここは照らす役。画面と API が同じ判定を使うので、
/// 同じ条件に同じ商品が当たる。値は作るときに決め、作った後は変えない（変わったら作り直す）。
/// </summary>
public abstract class ItemCondition
{
    /// <summary>何か絞っているか（何も絞らない値なら、条件として使わない）。<see cref="Prepare"/> の後に読む。</summary>
    public abstract bool IsActive { get; }

    /// <summary>除かないときに当てはまるか（値と AND／OR などの設定どおりに照らす）。</summary>
    public abstract bool Matches(ItemRecord item, SearchModuleContext context);

    /// <summary>
    /// 除くときに通すか。既定は「除かないときに当てはまる物、以外」。
    /// 範囲・日付・属性は**値の分からない商品を、除くときも外す**（ユーザ判断 2026-10-01。
    /// 値の分からない商品は「範囲の外」とは言えない。除かないときも外れているので、除いたら出てくると食い違う）。
    /// </summary>
    public virtual bool MatchesExcluded(ItemRecord item, SearchModuleContext context) => !Matches(item, context);

    /// <summary>
    /// 絞り込みの1回の始めに1回だけ呼ぶ（照らす重さの案c・`docs/research/search-modules-2026-10-01.md` §9）。
    /// 商品の全体から決まる値（外れ値の境・日付の端）を求めるなど、商品ごとにやり直さない準備をここで済ませる。
    /// </summary>
    public virtual void Prepare(SearchModuleContext context)
    {
    }
}

/// <summary>プルダウンで1つ選ぶ条件（三項など）。選んだ鍵と補助の切り替えで照らす。</summary>
/// <param name="neutralKey">何も絞らない選択肢の鍵。持たない条件（ギフト）は null で、いつも絞る。</param>
public sealed class ChoiceCondition(
    string key,
    bool flag,
    string? neutralKey,
    Func<ItemRecord, SearchModuleContext, string, bool, bool> matches) : ItemCondition
{
    public override bool IsActive => key != neutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => matches(item, context, key, flag);
}

/// <summary>
/// 候補から選んで積む条件（カテゴリ・タグ・ショップ・対応アバター・Unityプロジェクト・ファイルの場所）。
/// 積んだ値どうしは AND か OR（<paramref name="matchAll"/>）。
/// </summary>
/// <param name="keys">積んだ値の鍵。</param>
/// <param name="matchAll">全部を満たすか（AND を選べない種類では false を渡す）。</param>
/// <param name="flag">補助の切り替え（対応アバターの「素体経由の対応も含める」）。</param>
/// <param name="isUnspecified">
/// 商品が「指定が無い」かたまりに入るか（対応アバターだけ）。渡すと「対応している」「対応の指定が無い」のどちらを出すかで照らす。
/// </param>
/// <param name="showMatched">対応している商品を出すか（<paramref name="isUnspecified"/> を渡したときだけ見る）。</param>
/// <param name="showUnspecified">対応の指定が無い商品を出すか（同上）。</param>
/// <param name="includeMatches">選んだ値に加えて当てるかたまり（ショップの「お気に入りのショップ」）。null なら無い。</param>
/// <param name="includeOn">そのかたまりを当てるか。</param>
/// <param name="matchKey">照らす前に鍵を畳む決まり（ファイルの場所）。作るときに1回だけ畳む。</param>
public sealed class ListCondition(
    IReadOnlyList<string> keys,
    bool matchAll,
    bool flag,
    Func<ItemRecord, SearchModuleContext, string, bool, bool> matches,
    Func<ItemRecord, SearchModuleContext, bool>? isUnspecified = null,
    bool showMatched = true,
    bool showUnspecified = false,
    Func<ItemRecord, SearchModuleContext, bool>? includeMatches = null,
    bool includeOn = false,
    Func<string, string>? matchKey = null) : ItemCondition
{
    private readonly IReadOnlyList<string> _matchKeys = matchKey is null ? keys : keys.Select(matchKey).ToList();

    private bool HasGroups => isUnspecified is not null;

    private bool IncludeOn => includeMatches is not null && includeOn;

    /// <summary>
    /// 「指定が無い商品だけ」はアバターを選んでいなくても絞る。それ以外は、値を選んで初めて絞る
    /// （足した直後に、何も選んでいないのに「指定が無い商品」が消えるのを避ける）。
    /// </summary>
    public override bool IsActive => keys.Count > 0 || (HasGroups && !showMatched && showUnspecified) || IncludeOn;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (isUnspecified is not null && isUnspecified(item, context))
        {
            // 指定が無い商品は、選んだアバターと照らさない。出すかどうかだけ
            return showUnspecified;
        }

        if (HasGroups && !showMatched)
        {
            return false;
        }

        if (IncludeOn)
        {
            // 選んだ値のどれかと同じ扱い（OR）。何も選んでいなければ、このかたまりだけ
            return includeMatches!(item, context) || (_matchKeys.Count > 0 && MatchesKeys(item, context));
        }

        return MatchesKeys(item, context);
    }

    /// <summary>積んだ値（AND／OR どおり）に当たるか。「指定が無い」かたまりは見ない（件数を数えるときにも使う）。</summary>
    public bool MatchesKeys(ItemRecord item, SearchModuleContext context)
        => _matchKeys.Count == 0
            || (matchAll
                ? _matchKeys.All(key => matches(item, context, key, flag))
                : _matchKeys.Any(key => matches(item, context, key, flag)));
}

/// <summary>
/// 数の範囲（スキ数・価格）。商品の数のどれか1つが範囲に入れば当たる（<paramref name="matchAll"/> なら全部が入れば）。
/// 数の分からない商品（値段を入れていない・BOOTH に無い）は範囲のどこにも入らない。
/// </summary>
/// <param name="values">商品の数（価格なら種類ごと）。</param>
/// <param name="hasCondition">片側でも効かせているか。効かせた側の境が空でも、数の分かる商品だけに絞る。</param>
/// <param name="min">下限。null は制限しない。</param>
/// <param name="max">上限。null は制限しない。</param>
/// <param name="includeUnpriced">数の分からない商品も足すか（価格だけ）。除くときも足す。</param>
/// <param name="outlierFence">外れ値の境を求める（null なら外さない）。境以上の数を照らす数から外す。</param>
public sealed class RangeCondition(
    Func<ItemRecord, IReadOnlyList<long>> values,
    bool hasCondition,
    long? min,
    long? max,
    bool matchAll,
    bool includeUnpriced,
    Func<SearchModuleContext, long?>? outlierFence = null) : ItemCondition
{
    private long? _fence;

    public override bool IsActive => hasCondition;

    public override void Prepare(SearchModuleContext context) => _fence = outlierFence?.Invoke(context);

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (!hasCondition)
        {
            return true;
        }

        if (includeUnpriced && values(item).Count == 0)
        {
            return true;
        }

        var known = KnownValues(item);
        if (matchAll)
        {
            // 数の分からない商品は当てない（「全部が範囲に入る」の全部が空で真になるのを避ける）
            if (known.Count == 0)
            {
                return false;
            }

            foreach (var value in known)
            {
                if ((min is not null && value < min) || (max is not null && value > max))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var value in known)
        {
            if ((min is null || value >= min) && (max is null || value <= max))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 数が分かっていて、除かないときに当たらない商品（ユーザ判断 2026-10-01）。どれか1つで見るときは**どの数も**範囲に入らない商品、
    /// 「すべての価格が範囲内」のときは**範囲の外の数を1つでも持つ**商品（メモ82-6。表は `docs/spec/search-filters.md`）。
    /// 数の分からない商品（値段を入れていない・外れ値を外したら1つも残らない）は、除くときも外す。
    /// </summary>
    public override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
        => !hasCondition
            || (includeUnpriced && values(item).Count == 0)
            || (KnownValues(item).Count > 0 && !Matches(item, context));

    /// <summary>照らす数。外れ値を外していれば、その数だけを外す（商品は他の種類の価格で照らす）。</summary>
    private IReadOnlyList<long> KnownValues(ItemRecord item)
    {
        var all = values(item);
        if (_fence is not { } fence)
        {
            return all;
        }

        return all.Where(value => value < fence).ToList();
    }
}

/// <summary>
/// 日付の範囲（公開日・入手日）。指定した日をまるまる含む。日付の分からない商品は範囲のどこにも入らない。
/// </summary>
/// <param name="any">商品の日付のどれかが条件に当たるか。日付を1つも持たない商品は false。</param>
/// <param name="first">最初の1つだけで見るときの日付（入手日の「最初の購入のみ」）。null なら <paramref name="any"/> で見る。</param>
/// <param name="bounds">始まりと終わり（null は制限しない）。<see cref="Prepare"/> で1回だけ求める（打った字を読み直さない）。</param>
public sealed class DateCondition(
    Func<ItemRecord, Func<DateOnly, bool>, bool> any,
    Func<ItemRecord, DateOnly?>? first,
    Func<SearchModuleContext, (DateOnly? Since, DateOnly? Till)> bounds) : ItemCondition
{
    private static readonly Func<DateOnly, bool> AnyDate = _ => true;
    private DateOnly? _since;
    private DateOnly? _till;
    private Func<DateOnly, bool> _inRange = AnyDate;
    private bool _prepared;

    /// <summary>
    /// 準備の前に読まれたら、商品の全体を見ずに求めた境で答える（覚えない。後の <see cref="Prepare"/> や照らす時に、渡された材料で求め直す）。
    /// 覚えると、空欄の境が手元の端を指さないまま照らしてしまう。
    /// </summary>
    public override bool IsActive
    {
        get
        {
            var (since, till) = _prepared ? (_since, _till) : bounds(SearchModuleContext.Unused);
            return since is not null || till is not null;
        }
    }

    public override void Prepare(SearchModuleContext context) => EnsurePrepared(context, force: true);

    private void EnsurePrepared(SearchModuleContext context, bool force = false)
    {
        if (_prepared && !force)
        {
            return;
        }

        var (since, till) = bounds(context);
        (_since, _till) = (since, till);
        _inRange = date => (since is null || date >= since) && (till is null || date <= till);
        _prepared = true;
    }

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        EnsurePrepared(context);
        if (_since is null && _till is null)
        {
            return true;
        }

        // 日付が分からない商品は、日付で絞った時点で外す。「値が小さい」ではなく「値が無い」ので、範囲のどこにも当てはまらない
        return first is not null
            ? first(item) is { } date && _inRange(date)
            : any(item, _inRange);
    }

    /// <summary>
    /// 日付が分かっていて、範囲の外の商品。日付の分からない商品は、除くときも外す（ユーザ判断 2026-10-01）。
    /// 日付をいくつも持つ商品は、どれも範囲に入らないときだけ残る（当たる＝どれか1つが入る、の裏）。
    /// 最初の購入だけで見るときは、最初の日付が範囲の外の商品（後で買い足した日が範囲に入っていても残る）。
    /// </summary>
    public override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
    {
        EnsurePrepared(context);
        var known = first is not null ? first(item) is not null : any(item, AnyDate);
        return (_since is null && _till is null) || (known && !Matches(item, context));
    }
}

/// <summary>属性の値の幅（属性の行ごと）。評価していない商品はどの行にも当たらない。</summary>
public sealed class AttributeCondition(IReadOnlyList<AttributeRange> ranges, bool matchAll) : ItemCondition
{
    public override bool IsActive => ranges.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => ranges.Count == 0 || (matchAll ? ranges.All(range => InRange(item, range)) : ranges.Any(range => InRange(item, range)));

    /// <summary>
    /// 選んだ属性が**全部**評価済みで、当てはまらない商品（D2）。1行のときの「値が分かっていて範囲の外」を、そのまま全部の行に広げた形。
    /// 評価していない属性が1つでもあれば、除くときも外す。
    /// </summary>
    public override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
        => ranges.Count == 0 || (ranges.All(range => item.Local.Attributes.ContainsKey(range.Name)) && !Matches(item, context));

    /// <summary>その属性を評価していて、値が幅に入るか。</summary>
    public static bool InRange(ItemRecord item, AttributeRange range)
        => item.Local.Attributes.TryGetValue(range.Name, out var value) && value >= range.Min && value <= range.Max;
}

/// <summary>ユーザータグ（大分類ごとの条件。大分類どうしは AND か OR）。照合は <see cref="UserTagCondition"/>。</summary>
public sealed class UserTagsCondition(IReadOnlyList<UserTagCondition> conditions, bool matchAll) : ItemCondition
{
    public override bool IsActive => conditions.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => UserTagCondition.MatchesAll(conditions, matchAll, item.Local.UserTags);
}

/// <summary>改変（アバターごとの条件。アバターどうしは AND か OR）。</summary>
public sealed class ModificationsCondition(IReadOnlyList<ModificationCondition> conditions, bool matchAll) : ItemCondition
{
    public override bool IsActive => conditions.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => matchAll
            ? conditions.All(condition => MatchesOne(condition, item.Id, context.Modifications))
            : conditions.Any(condition => MatchesOne(condition, item.Id, context.Modifications));

    /// <summary>改変を選んでいなければこのアバターの改変のどれか、選んでいれば選んだ改変のどれか（AND ならすべて）に使った商品。</summary>
    public static bool MatchesOne(ModificationCondition condition, string itemId, ModificationUsage usage)
        => condition.Modifications.Count == 0
            ? usage.Used(condition.Avatar, itemId)
            : condition.MatchAll
                ? condition.Modifications.All(id => usage.InModification(id, itemId))
                : condition.Modifications.Any(id => usage.InModification(id, itemId));
}

/// <summary>編集状況（見る項目が未入力か）。取り込みの③を待っている商品は「未入力のみ」に数えない（編集画面に出てこないため）。</summary>
public sealed class EditStatusCondition(
    string key,
    IReadOnlyList<EditField> fields,
    bool matchAll,
    Func<ItemRecord, SearchModuleContext, bool> isAwaiting) : ItemCondition
{
    public const string MissingKey = "unedited";
    public const string FilledKey = "edited";
    public const string NeutralKey = "both";

    public override bool IsActive => key != NeutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => MatchesKey(item, context, key);

    /// <summary>選択肢ごとの件数にも使う。</summary>
    public bool MatchesKey(ItemRecord item, SearchModuleContext context, string choice) => choice switch
    {
        MissingKey => !isAwaiting(item, context) && Missing(item),
        FilledKey => !Missing(item),
        _ => true,
    };

    private bool Missing(ItemRecord item)
        => matchAll
            ? fields.All(field => EditFieldsMissing.IsMissing(item, field))
            : fields.Any(field => EditFieldsMissing.IsMissing(item, field));
}

/// <summary>更新通知あり（未読の「商品の更新」の知らせが、選んだ種類を含むか）。</summary>
public sealed class UpdateNoticeCondition(
    string key,
    BoothChangeKind kinds,
    bool matchAll,
    Func<string, SearchModuleContext, BoothChangeKind> kindsOf) : ItemCondition
{
    public const string UpdatedKey = "updated";
    public const string OtherKey = "other";
    public const string NeutralKey = "both";

    /// <summary>画面の並び（ユーザ判断の並び）。</summary>
    public static IReadOnlyList<BoothChangeKind> AllKinds { get; } =
        [BoothChangeKind.Content, BoothChangeKind.Variations, BoothChangeKind.Price, BoothChangeKind.Sale, BoothChangeKind.Page];

    public static BoothChangeKind AllMask { get; } = AllKinds.Aggregate(BoothChangeKind.None, (all, kind) => all | kind);

    public override bool IsActive => key != NeutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => MatchesKey(item, context, key);

    public bool MatchesKey(ItemRecord item, SearchModuleContext context, string choice) => choice switch
    {
        UpdatedKey => HasSelected(item, context),
        OtherKey => !HasSelected(item, context),
        _ => true,
    };

    private bool HasSelected(ItemRecord item, SearchModuleContext context)
        => matchAll
            ? (kindsOf(item.Id, context) & kinds) == kinds
            : (kindsOf(item.Id, context) & kinds) != BoothChangeKind.None;

    /// <summary>状態に書く名前。</summary>
    public static string KeyOf(BoothChangeKind kind) => kind switch
    {
        BoothChangeKind.Content => "content",
        BoothChangeKind.Variations => "variations",
        BoothChangeKind.Price => "price",
        BoothChangeKind.Sale => "sale",
        _ => "page",
    };

    /// <summary>状態の名前から種類を読む。知らない名前は飛ばし、残らなければ全部（編集状況の項目と同じ読み方）。</summary>
    public static BoothChangeKind Parse(IEnumerable<string> names)
    {
        var kinds = names
            .Select(name => AllKinds.FirstOrDefault(kind => KeyOf(kind) == name))
            .Aggregate(BoothChangeKind.None, (all, kind) => all | kind);
        return kinds == BoothChangeKind.None ? AllMask : kinds;
    }
}

/// <summary>最近（足跡の日が、何日前から何日前までに入るか）。足跡の無い商品は当たらない。</summary>
/// <param name="high">何日前まで。null はそれより前もすべて。</param>
public sealed class RecentCondition(RecentKind kind, int low, int? high) : ItemCondition
{
    /// <summary>足しただけで絞る（記録の無い商品が外れる）。数の範囲と同じく、足すこと自体が「この記録で選ぶ」という意思表示。</summary>
    public override bool IsActive => true;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (context.Recent.Of(item.Id, kind) is not { } at)
        {
            return false;
        }

        var days = DaysAgo(at, context.Now);
        return days >= low && (high is not { } limit || days <= limit);
    }

    /// <summary>暦の日で何日前か（今日＝0）。先の時刻（時計のずれ）は今日に数える。</summary>
    public static int DaysAgo(DateTimeOffset at, DateTimeOffset now)
        => Math.Max(0, (now.Date - at.ToOffset(now.Offset).Date).Days);
}
