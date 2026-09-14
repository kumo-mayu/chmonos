using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：絞り込み・広げ・並べ替え・カードの組み立て（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    private void ClearFilters()
    {
        _queryText = string.Empty;
        _selectedCategory = AllCategories;
        _ownedOnly = false;
        _missingOnly = false;
        _givenOnly = false;
        _receivedOnly = false;
        _avatarFilterId = null;
        _avatarFilterName = null;
        _avatarFilterHasBase = false;
        RaiseAvatarFilterChanged();
        _shopFilterKey = null;
        _shopFilterName = null;
        RaiseShopFilterChanged();

        foreach (var tag in TagFilters)
        {
            tag.Reset();
        }

        foreach (var attribute in AttributeFilters)
        {
            attribute.Min = 0;
            attribute.Max = 100;
        }

        // 積んだタグは「条件をクリア」で外す。属性と違って幅を戻す概念が無いため
        BoothTagFilters.Clear();
        RefreshBoothTagSuggestions();

        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(OwnedOnly));
        OnPropertyChanged(nameof(GivenOnly));
        OnPropertyChanged(nameof(ReceivedOnly));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        // 足跡は1回だけ読んで、この絞り込みの間は使い回す。
        // 1商品ごとに読み直すと、件数に比例してファイルを開くことになる
        _recentTimes = NeedsRecent() ? LoadRecentTimes() : null;

        // 改変はファイルを読むので待てない。**読めたらもう一度絞り込む**——
        // 一度きりの読みにしておくと、条件を積んだ直後だけ0件に見える
        if (NeedsModifications() && _modificationUsage is null)
        {
            LoadModificationUsageAsync().Forget();
        }

        _matches = SortItems(_allItems.Where(item => Matches(item)))
            .Select(item => _cards[item.Id])
            .ToList();

        // 0件のときは自動で広げる。悪くなりようが無い（0件のままか、増えるか）。
        // 「別の表記も探す」を入れているときは、当たっていても広げる——
        // 0件のときしか使えないのはこちらの都合で、ユーザの都合ではない
        if (_matches.Count == 0 || _searchAlternates)
        {
            TryWiden();
        }

        RefreshFacetCounts();
        RebuildRows();

        foreach (var filter in ExtraFilters.Where(f => f.Kind == ExtraFilterKind.Folder && f.Rows.Count == 0))
        {
            RebuildFolderRows(filter);
        }

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>広げて探したときに使った別表記。0件でなければ空。</summary>
    private Core.Services.SearchNode? _widenedNode;

    private readonly Dictionary<string, List<Core.Search.BridgeCandidate>> _widenedTerms = [];

    /// <summary>
    /// 打った語の別表記でもう一度探す。
    ///
    /// 索引を組むのに数秒かかることがあるので、**別のスレッドで**動かして
    /// 出来たら結果を差し替える。打っている手は止めない。
    /// </summary>
    private void TryWiden()
    {
        var node = _queryNode;
        if (node is Core.Services.SearchNode.All || !_services.Bridge.IsAvailable)
        {
            ClearWidening();
            return;
        }

        var token = ++_widenToken;

        Task.Run(() =>
        {
            var used = new Dictionary<string, List<Core.Search.BridgeCandidate>>(StringComparer.Ordinal);
            var widened = _services.Bridge.Widen(node, used);
            if (used.Count == 0)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                // 待っている間に打ち直されていたら捨てる。
                // 当たっている検索を広げるのは、トグルを入れているときだけ
                if (token != _widenToken || (_matches.Count > 0 && !_searchAlternates))
                {
                    return;
                }

                _widenedNode = widened;
                _widenedTerms.Clear();
                foreach (var (term, candidates) in used)
                {
                    _widenedTerms[term] = candidates;
                }

                _matches = SortItems(_allItems.Where(item => Matches(item)))
                    .Select(item => _cards[item.Id])
                    .ToList();

                if (_matches.Count == 0)
                {
                    // 広げても出なかった。広げた印は出さない（何も変わっていないので）
                    ClearWidening();
                }

                // 広げた結果でも件数の内訳は合っていてほしい
                RefreshFacetCounts();
                RebuildRows();
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(WidenedText));
                OnPropertyChanged(nameof(HasWidened));
                OnPropertyChanged(nameof(EmptyHint));
            });
        }).Forget();
    }

    private int _widenToken;

    private void ClearWidening()
    {
        if (_widenedNode is null && _widenedTerms.Count == 0)
        {
            return;
        }

        _widenedNode = null;
        _widenedTerms.Clear();
        OnPropertyChanged(nameof(WidenedText));
        OnPropertyChanged(nameof(HasWidened));
    }

    public bool HasWidened => _widenedTerms.Count > 0;

    /// <summary>
    /// 何で当たったかを1行で出す。
    /// **辞書は引いた結果を説明できる**のが埋め込みとの分かれ目なので、説明を捨てない。
    /// </summary>
    public string WidenedText
    {
        get
        {
            if (_widenedTerms.Count == 0)
            {
                return string.Empty;
            }

            var parts = _widenedTerms.Select(entry =>
                $"「{entry.Key}」を {string.Join("・", entry.Value.Take(4).Select(c => c.Text))}");

            return string.Join(" / ", parts) + " としても探しました。";
        }
    }

    /// <summary>
    /// 今どの条件で絞っているかを1行で示す。
    /// 「なぜこの結果になったか」が結果の隣で読めるようにするため。
    /// </summary>
    public string FilterSummary => string.Join(" / ", FilterParts());

    /// <summary>
    /// 効いている条件の数。畳んだパネルに出す。
    /// 畳むと条件そのものが見えなくなるので、数だけでも残さないと
    /// 「なぜか商品が少ない」の原因を探す場所が無くなる。
    /// </summary>
    public int ActiveFilterCount => FilterParts().Count;

    /// <summary>効いている条件を1つずつ文にする。要約にも件数にも同じものを使う。</summary>
    private List<string> FilterParts()
    {
        var parts = new List<string>();

        var tags = TagFilters.Where(filter => filter.IsSelected).ToList();
        foreach (var tag in tags)
        {
            var subs = tag.SelectedSubs.ToList();
            parts.Add(subs.Count == 0 ? tag.Name : $"{tag.Name}（{string.Join("・", subs)}）");
        }

        foreach (var attribute in AttributeFilters)
        {
            parts.Add($"{attribute.Name} {attribute.Min}〜{attribute.Max}%");
        }

        foreach (var tag in BoothTagFilters)
        {
            parts.Add($"タグ：{tag.Name}");
        }

        foreach (var extra in ExtraFilters.Where(filter => filter.IsActive))
        {
            parts.Add(extra.SummaryText);
        }

        if (_ownedOnly)
        {
            parts.Add("所持のみ");
        }

        if (_missingOnly)
        {
            parts.Add("ファイルが見つからない");
        }

        if (_givenOnly && _receivedOnly)
        {
            parts.Add("贈った・貰った");
        }
        else if (_givenOnly)
        {
            parts.Add("贈った");
        }
        else if (_receivedOnly)
        {
            parts.Add("貰った");
        }

        if (_shopFilterName is not null)
        {
            parts.Add($"ショップ：{_shopFilterName}");
        }

        if (_avatarFilterName is not null)
        {
            // 素体を持たないアバターに「素体経由を含む」と書かない。
            // 経由する先が無いので、書いてあると効いていないのに効いたように読める
            parts.Add(_includeViaBase && _avatarFilterHasBase
                ? $"{_avatarFilterName}（素体経由を含む）"
                : _avatarFilterName);
        }

        if (!string.IsNullOrEmpty(_selectedCategory) && _selectedCategory != AllCategories)
        {
            parts.Add(_selectedCategory);
        }

        return parts;
    }

    public bool HasActiveFilters => ActiveFilterCount > 0;

    /// <summary>
    /// 0件のときの案内。絞って0件なのか、そもそも空なのかで次にやることが違う。
    /// </summary>
    public string EmptyHint => _allItems.Count == 0
        ? "「取り込み」からフォルダを読み込んでください"
        : HasActiveFilters || _queryText.Trim().Length > 0
            ? "「条件をクリア」で全件に戻ります"
            : "「取り込み」からフォルダを読み込んでください";

    /// <summary>
    /// 絞り込み結果を、現在の列数で行に切り直す。
    ///
    /// **行を全部消して作り直さない。**作り直すと見えている行のカードの見た目が全部作り直され、
    /// 列数が変わるたび（ナビや絞り込みを畳むたび）に画面が 146〜380ms 固まった（U28、実測）。
    /// 並びの合っているカードには触らず、ずれた所だけを抜き差しする。
    /// 列が8から9に増えると、行 k で作り直すのは k+1 枚だけになる（見えている5行なら45枚→15枚）。
    /// カードの ViewModel は使い回している（<c>_cards</c>）ので、同じ商品は同じ物として比べられる。
    /// </summary>
    private void RebuildRows()
    {
        // リストで出しているときは、同じ並び（_matches）をそのまま渡す
        OnPropertyChanged(nameof(ListItems));

        var needed = (_matches.Count + _columns - 1) / _columns;

        while (Rows.Count > needed)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        while (Rows.Count < needed)
        {
            Rows.Add(new CardRow());
        }

        for (var row = 0; row < needed; row++)
        {
            var cards = Rows[row].Cards;
            var start = row * _columns;
            var count = Math.Min(_columns, _matches.Count - start);

            for (var index = 0; index < count; index++)
            {
                var card = _matches[start + index];
                if (index < cards.Count && ReferenceEquals(cards[index], card))
                {
                    continue;
                }

                // 同じ行の後ろにあるなら、手前のずれた物を抜いて詰める（列が増えたときの普通の形）
                var later = -1;
                for (var look = index + 1; look < cards.Count; look++)
                {
                    if (ReferenceEquals(cards[look], card))
                    {
                        later = look;
                        break;
                    }
                }

                if (later > 0)
                {
                    for (var remove = later - 1; remove >= index; remove--)
                    {
                        cards.RemoveAt(remove);
                    }
                }
                else
                {
                    cards.Insert(index, card);
                }
            }

            while (cards.Count > count)
            {
                cards.RemoveAt(cards.Count - 1);
            }
        }
    }

    /// <summary>
    /// 絞り込みの軸。ファセットの件数を数えるとき、自分の軸だけを外して数えるために使う。
    /// 外さないと、userTagで「衣装」を選んだ瞬間に同じ欄の他のuserTagが全部0件になる。
    /// </summary>
    private enum FilterAxis
    {
        Owned,
        Avatar,
        Category,
        UserTag,
        BoothTag,
        Attribute,
        Extra,
    }

    /// <param name="except">この軸だけ適用しない。ファセットの件数を数えるときに指定する。</param>
    private bool Matches(ItemRecord item, FilterAxis? except = null)
    {
        if (except != FilterAxis.Owned)
        {
            if (_ownedOnly && !item.IsDownloaded)
            {
                return false;
            }

            if (_missingOnly && !item.Local.OwnedFiles.Any(file => file.Paths.Count == 0))
            {
                return false;
            }

            // 贈った・貰ったは所持とは別の軸。貰ったものは手元にあり、贈ったものは手元に無いので、
            // 同じ札に入れると読み違える。両方選んだ場合は「どちらかに当てはまるもの」
            if (_givenOnly || _receivedOnly)
            {
                var matched = (_givenOnly && Core.Services.Purchases.WasGiven(item))
                    || (_receivedOnly && Core.Services.Purchases.WasReceived(item));

                if (!matched)
                {
                    return false;
                }
            }
        }

        // ショップでの絞り込み。束ねる鍵はショップ一覧と同じ（名前は変わり得るので鍵で見る）
        if (_shopFilterKey is not null
            && !string.Equals(item.ShopSubdomain, _shopFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 対応アバターでの絞り込み。素体経由は推定なので、含めるかを選べるようにする
        if (except != FilterAxis.Avatar && _avatarFilterId is not null)
        {
            var match = (_compatibility ??= Core.Services.AvatarCompatibilityIndex.Build(
                _services.Store.Avatars.Load())).MatchFor(item.Local, _avatarFilterId);

            var accepted = _includeViaBase
                ? match is Core.Services.AvatarMatch.Direct or Core.Services.AvatarMatch.ViaBase
                : match == Core.Services.AvatarMatch.Direct;

            if (!accepted)
            {
                return false;
            }
        }

        if (except != FilterAxis.Category
            && !string.IsNullOrEmpty(_selectedCategory)
            && _selectedCategory != AllCategories
            && !string.Equals(item.CategoryName, _selectedCategory, StringComparison.CurrentCulture))
        {
            return false;
        }

        // 選ばれたトップのいずれかに当てはまればよい（別のトップ同士はORで扱う）
        if (except != FilterAxis.UserTag)
        {
            var selectedTags = TagFilters.Where(filter => filter.IsSelected).ToList();
            if (selectedTags.Count > 0 && !selectedTags.Any(filter => filter.Matches(item)))
            {
                return false;
            }
        }

        // 属性は軸ごとにANDで積む。片側でも動かした軸では未評価が落ちる
        if (except != FilterAxis.Attribute && AttributeFilters.Any(filter => !filter.Matches(item)))
        {
            return false;
        }

        // BOOTHタグも積んだものをANDで。積むこと自体が「このタグで絞る」という意思表示
        if (except != FilterAxis.BoothTag && BoothTagFilters.Any(filter => !filter.Matches(item)))
        {
            return false;
        }

        // 積んだ条件は軸ごとにANDで積む。積むこと自体が「この軸で選ぶ」という意思表示
        if (except != FilterAxis.Extra
            && ExtraFilters.Any(filter => !filter.Matches(item, _unreadItemIds, _recentTimes, _modificationUsage)))
        {
            return false;
        }

        return MatchesQuery(item);
    }

    /// <summary>
    /// 選択肢の横に出す件数を数え直す。
    ///
    /// 数えるのは「今の他の条件を適用した後」の件数。全体の件数だと、押してから0件と分かる。
    /// ただし自分の軸は自分を除いて数える（<see cref="FilterAxis"/> の説明を参照）。
    /// 0件の選択肢は消さずに薄く出す。消えると「さっきあった項目が無い」と探すことになる。
    /// </summary>
    private void RefreshFacetCounts()
    {
        var forCategory = _allItems.Where(item => Matches(item, FilterAxis.Category)).ToList();
        foreach (var option in Categories)
        {
            option.Count = option.IsAll
                ? forCategory.Count
                : forCategory.Count(item =>
                    string.Equals(item.CategoryName, option.Name, StringComparison.CurrentCulture));
        }

        var forTags = _allItems.Where(item => Matches(item, FilterAxis.UserTag)).ToList();
        foreach (var filter in TagFilters)
        {
            filter.Count = forTags.Count(item => item.Local.UserTags.Any(entry =>
                string.Equals(entry.Top, filter.Name, StringComparison.CurrentCultureIgnoreCase)));

            foreach (var sub in filter.Subs)
            {
                sub.Count = forTags.Count(item => item.Local.UserTags.Any(entry =>
                    string.Equals(entry.Top, filter.Name, StringComparison.CurrentCultureIgnoreCase)
                    && entry.Subs.Contains(sub.Name, StringComparer.CurrentCultureIgnoreCase)));
            }
        }

        // 積んだタグは自分の軸を除いて数える。含めて数えると、積んだ瞬間に
        // 「今の結果と同じ件数」しか出ず、他のタグを足す判断ができない
        var forBoothTags = _allItems.Where(item => Matches(item, FilterAxis.BoothTag)).ToList();
        foreach (var filter in BoothTagFilters)
        {
            filter.Count = forBoothTags.Count(filter.Matches);
        }

        var forOwned = _allItems.Where(item => Matches(item, FilterAxis.Owned)).ToList();
        OwnedCount = forOwned.Count(item => item.IsDownloaded);
        MissingCount = forOwned.Count(item => item.Local.OwnedFiles.Any(file => file.Paths.Count == 0));
        GivenCount = forOwned.Count(Core.Services.Purchases.WasGiven);
        ReceivedCount = forOwned.Count(Core.Services.Purchases.WasReceived);

        foreach (var name in new[]
        {
            nameof(OwnedCount), nameof(MissingCount), nameof(GivenCount), nameof(ReceivedCount),
            nameof(HasGiftRecords),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>「ファイルを持っているものだけ」を押したときの件数。</summary>
    public int OwnedCount { get; private set; }

    /// <summary>「ファイルが見つからない」を押したときの件数。</summary>
    public int MissingCount { get; private set; }

    /// <summary>
    /// 贈った・貰った商品の数。回数ではなく商品数（1つの商品を3人に贈っても1件）。
    /// 統計側は「贈った回数」「贈答に使った額」と書き分ける。
    /// </summary>
    public int GivenCount { get; private set; }

    public int ReceivedCount { get; private set; }

    /// <summary>贈答の記録が1件も無ければ、この行ごと出さない。</summary>
    public bool HasGiftRecords => GivenCount > 0 || ReceivedCount > 0;

    /// <summary>贈った商品だけに絞る。</summary>
    public bool GivenOnly
    {
        get => _givenOnly;
        set
        {
            if (SetField(ref _givenOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>貰った商品だけに絞る。</summary>
    public bool ReceivedOnly
    {
        get => _receivedOnly;
        set
        {
            if (SetField(ref _receivedOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    private bool MatchesQuery(ItemRecord item)
        => !_haystacks.TryGetValue(item.Id, out var haystack)
            || Core.Services.SearchQuery.Matches(
                _widenedNode ?? _queryNode,
                haystack,
                _searchBody,
                _searchPaths,

                // 読みは広げるときだけ見る。組み立てた「あり得る読み」には外れも混じるので、
                // 普段の検索から当たると「なぜこれが出たのか」が説明できなくなる
                includeReadings: _widenedNode is not null);

    /// <summary>
    /// 表示順を適用する。属性で並べたときは、未評価を昇順・降順どちらでも常に末尾に置く。
    /// 未評価は「値が小さい」のではなく「値が無い」ので、0として混ぜると誤読させる。
    /// </summary>
    /// <summary>
    /// この絞り込みで足跡が要るか。
    ///
    /// 積んでいなければ読まない。関係の無い検索でファイルを開く理由が無い。
    /// </summary>
    private bool NeedsRecent()
        => ExtraFilters.Any(filter => filter.Shape == ExtraFilterShape.Days && filter.IsActive);

    private RecentTimes LoadRecentTimes() => new(
        _services.Recent.Times(Core.Services.RecentKind.Added),
        _services.Recent.Times(Core.Services.RecentKind.Used),
        _services.Recent.Times(Core.Services.RecentKind.Viewed));

    /// <summary>改変を読む必要があるか。積んでいなければ読まない。</summary>
    private bool NeedsModifications()
        => ExtraFilters.Any(filter => filter.Kind == ExtraFilterKind.UsedOn && filter.IsActive);

    private async Task LoadModificationUsageAsync()
    {
        ModificationUsage usage;
        try
        {
            usage = ModificationUsage.From((await _services.Modifications.LoadAllAsync()).Modifications);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めなくても「空」で置く。null のままだと読み直しを繰り返す
            usage = ModificationUsage.Empty;
        }

        RunOnUiThread(() =>
        {
            _modificationUsage = usage;
            ApplyFilters();
        });
    }

    /// <summary>
    /// 改変が変わったので、次の絞り込みで読み直す。
    ///
    /// 改変は別の画面で増えたり減ったりする。持ち続けると
    /// 「作ったのに絞り込みに出ない」が起きる。
    /// </summary>
    public void NoteModificationsChanged()
    {
        _modificationUsage = null;
        RefreshExtraSuggestions();
    }

    /// <summary>その並び順が「最近」の足跡を見るものなら、どの種類か。</summary>
    private static Core.Services.RecentKind? RecentKindOf(SortKind kind) => kind switch
    {
        SortKind.RecentlyAdded => Core.Services.RecentKind.Added,
        SortKind.RecentlyUsed => Core.Services.RecentKind.Used,
        SortKind.RecentlyViewed => Core.Services.RecentKind.Viewed,
        _ => null,
    };

    private IEnumerable<ItemRecord> SortItems(IEnumerable<ItemRecord> items)
    {
        var sort = _sort;

        // 値が無い商品（属性を付けていない・足跡が無い・入手日が無い）を後ろにまとめる決まりは Core の ItemOrder にある（試験付き）
        if (sort.Kind == SortKind.Attribute && sort.AttributeName is { } attributeName)
        {
            return Core.Services.ItemOrder.ByAttribute(items, attributeName, sort.Descending);
        }

        if (RecentKindOf(sort.Kind) is { } recentKind)
        {
            return Core.Services.ItemOrder.ByTime(items, _services.Recent.Times(recentKind), sort.Descending);
        }

        return sort.Kind switch
        {
            SortKind.Name => sort.Descending
                ? items.OrderByDescending(item => item.DisplayName, StringComparer.CurrentCulture)
                : items.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture),
            SortKind.Size => sort.Descending
                ? items.OrderByDescending(item => item.LogicalSizeBytes)
                : items.OrderBy(item => item.LogicalSizeBytes),
            SortKind.WishList => sort.Descending
                ? items.OrderByDescending(item => item.Booth.WishListsCount)
                : items.OrderBy(item => item.Booth.WishListsCount),
            _ => Core.Services.ItemOrder.ByAcquired(items, sort.Descending),
        };
    }

    private ItemCardViewModel ToCard(ItemRecord item)
    {
        var missing = item.Local.OwnedFiles.Any(file => file.Paths.Count == 0);

        // 取り込みの③がまだの商品は「未編集」ではなく「取り込み中」と出す（U8・U10）
        var awaiting = _main?.IsAwaitingDetection(item.Id) == true;

        return new ItemCardViewModel(
            item,
            _thumbnails,
            _services.Paths.ItemImagesDir(item.Id),
            _services.Settings.ThumbnailRole)
        {
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            SizeText = item.IsDownloaded ? Core.Models.DisplayText.Size(item.LogicalSizeBytes) : "未取得",
            IsOwned = item.IsDownloaded,
            NeedsEdit = item.Local.UserTags.Count == 0 && !awaiting,
            IsAwaitingDetection = awaiting,
            // 取り込みの途中で、絵がまだ1枚も無い。灰色の枠だけだと壊れて見える（U8）
            // 画像を保存しない設定では絵は来ないので、「取得中」と言うと嘘になる
            IsImagePending = _main?.IsImporting == true
                && _services.Settings.SaveImages
                && item.Booth.Images.Count > 0
                && !_thumbnails.ListFiles(_services.Paths.ItemImagesDir(item.Id)).Any(),
            HasMissingFile = missing,
            UserTagText = string.Join(" / ", item.Local.UserTags.Select(tag => tag.Top)),
        };
    }
}
