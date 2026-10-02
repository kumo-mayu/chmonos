using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 検索画面と同じ書き方（スペース＝AND・<c>-語</c>・<c>"…"</c>・<c>OR</c>・<c>( )</c>・<c>name:</c>）で、
/// 商品と、分類・属性の名前を照らす（ユーザ指示 2026-09-19：タグの管理・属性の管理の商品の検索）。
///
/// 画面ごとに書き方が違うと、同じ語を打っても当たりが変わって覚え直しになる。
/// 探す所は検索画面の既定（商品名・ショップ名・メモ）に揃え、それ以外は前置きで指す。
/// </summary>
public sealed class ItemTextFilter
{
    private readonly SearchNode _node;
    private readonly Dictionary<string, SearchHaystack> _haystacks = new(StringComparer.Ordinal);

    private ItemTextFilter(SearchNode node)
    {
        _node = node;
    }

    /// <summary>空白だけなら null（絞らない）。</summary>
    public static ItemTextFilter? Create(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : new ItemTextFilter(SearchQuery.Parse(text));

    /// <summary>
    /// 商品を照らす。同じ絞り込みの間に同じ商品を何度も照らす（大分類ごと・小分類ごと）ので、
    /// 畳んだ文字列は商品ごとに1回だけ作る
    /// </summary>
    public bool Matches(ItemRecord item)
    {
        if (!_haystacks.TryGetValue(item.Id, out var haystack))
        {
            haystack = SearchText.Build(item);
            _haystacks[item.Id] = haystack;
        }

        return SearchQuery.Matches(_node, haystack, SearchOptions.Default);
    }

    /// <summary>
    /// 分類や属性の名前を、商品名と同じ扱いで照らす。<c>-語</c> や <c>OR</c> も名前に効く
    /// （「リボン -黒」で名前に黒を含む小分類を外せる）
    /// </summary>
    public bool MatchesName(string name)
        => SearchQuery.Matches(
            _node,
            SearchHaystack.FromValues(new Dictionary<SearchField, string[]> { [SearchField.Name] = [name] }),
            SearchOptions.Default);

    /// <summary>
    /// 小分類・属性の名前に加えて、その小分類・属性自身のメモも探す（メモ10-③ 2026-10-02）。
    /// 商品名とメモを同じ検索で引く検索画面に合わせた。名前とメモは別の欄として持つので、
    /// <c>-語</c> は「名前にもメモにも無い」を意味し、<c>name:</c>・<c>memo:</c> の前置きも効く
    /// </summary>
    public bool MatchesNameOrMemo(string name, string? memo)
    {
        var values = new Dictionary<SearchField, string[]> { [SearchField.Name] = [name] };
        if (!string.IsNullOrWhiteSpace(memo))
        {
            values[SearchField.Memo] = [memo];
        }

        return SearchQuery.Matches(_node, SearchHaystack.FromValues(values), SearchOptions.Default);
    }
}
