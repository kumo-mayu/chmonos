using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索画面：並べ替えの区切りの札（ユーザ判断 2026-10-01「7は付けよう。イメージは図書館やビデオショップの分類の為の偽アイテムだ」）。
///
/// まとまりのある順（カテゴリ・ショップ・公開日・入手日）で並べているとき、まとまりの最初の商品の前に札（<see cref="SortDivider"/>）を入れる。
/// 札は画面に出す並び（<see cref="_shown"/>）にだけ混ぜ、件数・選ぶ・まとめて操作は商品だけの並び（<see cref="_matches"/>）のまま。
/// </summary>
public sealed partial class SearchViewModel
{
    /// <summary>画面に出す並び（商品と札）。札を出さない並べ替えでは <see cref="_matches"/> と同じ物。</summary>
    private IReadOnlyList<object> _shown = [];

    /// <summary>
    /// <see cref="_shown"/> を作ったときの <see cref="_matches"/>。列数が変わっただけ（幅・カードの大きさ）なら、札を数え直さない。
    /// 並べ替え・絞り込み・設定の切り替えは、どれも <see cref="_matches"/> を作り直すので、同じ物かで見分けられる
    /// </summary>
    private List<ItemCardViewModel>? _shownFrom;

    /// <summary>札を鍵ごとに使い回す（<see cref="SortDivider"/> の注記）。</summary>
    private Dictionary<string, SortDivider> _dividers = new(StringComparer.Ordinal);

    /// <summary>リストに出す並び（商品と札）。カードの段と同じ並び。</summary>
    public IReadOnlyList<object> DisplayItems
    {
        get
        {
            EnsureShown();
            return _shown;
        }
    }

    /// <summary>今出している札（試験・確かめ用）。</summary>
    internal IEnumerable<SortDivider> ShownDividers => DisplayItems.OfType<SortDivider>();

    private void EnsureShown()
    {
        if (ReferenceEquals(_shownFrom, _matches))
        {
            return;
        }

        _shownFrom = _matches;
        var next = WithDividers(_matches);

        // 並びが前と同じなら前の入れ物のまま渡す（メモ2-① 2026-10-02）。何も絞っていない条件を消した・切ったときのように
        // 結果が変わらない絞り直しで、リストに新しい並びを渡すと、行を全部組み直していた（作り物の200件で約90ms）。
        // 札は鍵ごとに使い回しているので、同じ物かは参照で比べられる
        if (!SameSequence(_shown, next))
        {
            _shown = next;
        }
    }

    private static bool SameSequence(IReadOnlyList<object> before, IReadOnlyList<object> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Count; index++)
        {
            if (!ReferenceEquals(before[index], after[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 札を入れた並び。札を出さない並べ替え・設定で切っているときは、商品の並びをそのまま返す。
    /// まとまりが1つしか無くても札は出す（「全部が同じショップ」と分かるのも札の役目）。
    /// </summary>
    private IReadOnlyList<object> WithDividers(List<ItemCardViewModel> matches)
    {
        if (!_services.Settings.ShowSortDividers || matches.Count == 0 || GroupingOf(_sort.Kind) is not { } grouping)
        {
            _dividers.Clear();
            return matches;
        }

        var groups = ItemGroups.Split(matches, card => grouping.GroupOf(card.Item));
        _shopIcons = null;
        var shown = new List<object>(matches.Count + groups.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var kept = new Dictionary<string, SortDivider>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            // 並べ替えと切り方が食い違って同じ鍵が2回来たら、2枚目は別の札にする（同じ物が並びに2回入ると、段の組み方が見分けられない）
            var key = used.Add(group.Key) ? group.Key : $"{group.Key}#{group.Start}";
            var cacheKey = $"{grouping.Kind}|{key}";
            var shop = grouping.Kind == ShopKind ? ShopOfGroup(matches[group.Start].Item) : null;
            if (!_dividers.TryGetValue(cacheKey, out var divider) || divider.Label != group.Label || divider.Parent != group.Parent
                || divider.IconPath != shop?.IconPath)
            {
                divider = shop is null
                    ? new SortDivider(key, grouping.Kind, group.Label, group.Parent)
                    : new SortDivider(key, grouping.Kind, group.Label, group.Parent)
                    {
                        IconPath = shop.IconPath,
                        Thumbnails = _thumbnails,
                        OpenShopCommand = new RelayCommand(() => _main?.ShowShopAsync(shop.Subdomain).Forget()),
                        OpenInBoothCommand = shop.BoothUrl is { } url ? new RelayCommand(() => Services.Shell.OpenUrl(url)) : null,
                    };
            }

            kept[cacheKey] = divider;
            divider.Count = group.Count;
            shown.Add(divider);
            for (var index = group.Start; index < group.Start + group.Count; index++)
            {
                shown.Add(matches[index]);
            }
        }

        // 今の並びにある札だけを持ち越す（絞り込みで消えたまとまり・前の並べ替えの札を溜めない）
        _dividers = kept;
        return shown;
    }

    /// <summary>
    /// どの並べ替えで札を出すか（spec の「並べ替えの区切り」）。
    /// - カテゴリ・ショップ：頼まれた2つ（同じ値の商品が続く）
    /// - 公開日：年と月。新作の棚のように「いつ頃の物か」で区切る（仮決め 2026-10-01）
    /// - 入手日：公開日と同じ年と月（ユーザ判断 2026-10-01）。既定の並べ替えなので、普段の一覧に札が並ぶのを好まない人のために
    ///   子の設定（<see cref="AppSettings.ShowAcquiredSortDividers"/>）で消せる
    /// - 数の項目（価格・払った額・スキ数・容量・属性）は帯の切り方を決めないと区切れない。名前は読みが推定で、頭の字の境が誤る。
    ///   「最近」の足跡は「今日・今週」のように時計で切ることになる。どれも出さない（ユーザ判断 2026-10-01）
    /// </summary>
    private ItemGrouping? GroupingOf(SortKind kind) => kind switch
    {
        SortKind.Category => new ItemGrouping("カテゴリ", item => ItemGroups.CategoryOf(item, _services.Categories)),
        SortKind.Shop => new ItemGrouping(ShopKind, ItemGroups.ShopOf),
        SortKind.PublishedAt => new ItemGrouping("公開日", ItemGroups.PublishedOf),
        SortKind.AcquiredAt when _services.Settings.ShowAcquiredSortDividers => new ItemGrouping("入手日", ItemGroups.AcquiredOf),
        _ => null,
    };

    private const string ShopKind = "ショップ";

    /// <summary>
    /// ショップの札に付けるショップ（メモ2-⑤）。まとまりの最初の商品から取る（同じショップ名でまとめているので、どの商品でも同じ店）。
    /// ショップの分からない商品（「ショップなし」）は null で、札は今までどおり押せない。
    /// アイコンの置き場は札を作り直すときに1回だけ列挙する（店ごとに列挙すると店の数だけ置き場をなめる・<see cref="Core.Storage.ShopIconIndex"/>）。
    /// </summary>
    private DividerShop? ShopOfGroup(ItemRecord item)
    {
        if (item.ShopSubdomain is not { Length: > 0 } subdomain)
        {
            return null;
        }

        _shopIcons ??= _services.Paths.ReadShopIconIndex();

        // 手元だけのショップ（local-）には BOOTH のページが無い。BOOTH のショップなら、鍵を出した側（人が入れたショップが先）の URL か、
        // サブドメインから作る（ShopSubdomain と同じ順に見ないと、人が直したショップの札で取れた側の店を開く）
        var known = item.Local.Shop is { } local ? local.Url : item.Booth.Shop?.Url;
        var url = Core.Models.LocalShopKey.IsLocal(subdomain)
            ? null
            : known is { Length: > 0 } ? known : $"https://{subdomain}.booth.pm/";
        return new DividerShop(subdomain, _shopIcons.FindIcon(subdomain), url);
    }

    /// <summary>札を作り直す1回の間だけ持つ、アイコンの置き場の表。</summary>
    private Core.Storage.ShopIconIndex? _shopIcons;

    private sealed record DividerShop(string Subdomain, string? IconPath, string? BoothUrl);

    private sealed record ItemGrouping(string Kind, Func<ItemRecord, (string Key, string Label, string? Parent)> GroupOf);
}
