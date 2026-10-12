using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query.Search;

/// <summary>
/// 検索の式（docs/spec/local-api-shapes.md「検索の式」）。どれか1つだけを書く：
/// 枝は <see cref="And"/>・<see cref="Or"/>・<see cref="Not"/>、葉は <see cref="Text"/>（検索欄の記法）と <see cref="Condition"/>（画面の絞り込みの条件1つ）。
/// 画面は条件を AND で並べるが、API はプログラムが組むので木にする（ユーザ判断 2026-10-12）。
/// </summary>
public sealed class SearchExpression
{
    public IReadOnlyList<SearchExpression>? And { get; init; }

    public IReadOnlyList<SearchExpression>? Or { get; init; }

    public SearchExpression? Not { get; init; }

    public string? Text { get; init; }

    public ConditionRequest? Condition { get; init; }
}

/// <summary>検索欄の記法の照らし方（画面の「対象」と大文字・小文字などの区別）。送らなければ画面の既定と同じ。</summary>
/// <param name="Targets">探す対象（<c>name</c>・<c>shop</c>・<c>memo</c> など。検索欄の前置きと同じ名前）。</param>
public sealed record TextOptionsRequest(IReadOnlyList<string>? Targets = null, bool CaseSensitive = false, bool WidthSensitive = false, bool KanaSensitive = false);

/// <summary>組み立てた式。葉の判定は作ったときに準備まで済ませる。</summary>
internal abstract class CompiledExpression
{
    /// <summary>
    /// 当てはまるか。<paramref name="negated"/> は上に掛かっている NOT の数が奇数か。
    /// NOT は葉まで下ろす（ド・モルガン）。条件の葉では、画面の「除く」と同じ判定（値の分からない商品も外す）を使う。
    /// </summary>
    public abstract bool Matches(ItemRecord item, bool negated);

    /// <summary>条件の葉の種類（選択肢の件数で、数える欄の条件を外すかを見るのに使う）。</summary>
    public virtual IEnumerable<SearchModuleKind> ConditionKinds => [];

    public sealed class All : CompiledExpression
    {
        public override bool Matches(ItemRecord item, bool negated) => !negated;
    }

    public sealed class And(IReadOnlyList<CompiledExpression> children) : CompiledExpression
    {
        // NOT (A AND B) = (NOT A) OR (NOT B)
        public override bool Matches(ItemRecord item, bool negated)
            => negated ? children.Any(child => child.Matches(item, true)) : children.All(child => child.Matches(item, false));

        public override IEnumerable<SearchModuleKind> ConditionKinds => children.SelectMany(child => child.ConditionKinds);
    }

    public sealed class Or(IReadOnlyList<CompiledExpression> children) : CompiledExpression
    {
        // NOT (A OR B) = (NOT A) AND (NOT B)
        public override bool Matches(ItemRecord item, bool negated)
            => negated ? children.All(child => child.Matches(item, true)) : children.Any(child => child.Matches(item, false));

        public override IEnumerable<SearchModuleKind> ConditionKinds => children.SelectMany(child => child.ConditionKinds);
    }

    public sealed class Not(CompiledExpression child) : CompiledExpression
    {
        public override bool Matches(ItemRecord item, bool negated) => child.Matches(item, !negated);

        public override IEnumerable<SearchModuleKind> ConditionKinds => child.ConditionKinds;
    }

    /// <summary>検索欄の記法。値の分からない商品という考えが無いので、NOT はそのまま裏返す。</summary>
    public sealed class Text(SearchNode node, Func<ItemRecord, SearchHaystack> haystack, SearchOptions options, SearchFacts facts) : CompiledExpression
    {
        public override bool Matches(ItemRecord item, bool negated)
            => SearchQuery.Matches(node, haystack(item), options, facts) != negated;
    }

    /// <summary>
    /// 画面の条件1つ。**絞らない値の条件（「両方」・境の無い範囲など）は、種類を問わず「すべてに当たる条件」として扱う**
    /// （not を付ければ何にも当たらない）。判定の側に任せると、選ぶ条件は何にも当たらず範囲はすべてに当たる、と種類で分かれる。
    /// </summary>
    public sealed class Condition(SearchModuleKind kind, ItemCondition condition, SearchModuleContext context) : CompiledExpression
    {
        private readonly bool _active = condition.IsActive;

        public override bool Matches(ItemRecord item, bool negated)
            => !_active
                ? !negated
                : negated ? condition.MatchesExcluded(item, context) : condition.Matches(item, context);

        public override IEnumerable<SearchModuleKind> ConditionKinds => [kind];
    }
}
