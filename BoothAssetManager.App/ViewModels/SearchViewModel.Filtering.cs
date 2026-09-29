using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：絞り込み・広げ・並べ替え・カードの組み立て</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 「条件をクリア」。文字列を消し、条件の値を何も絞らない形に戻す。**追加した条件そのものは残す**
    /// （どれを使うかは人が選んだ物で、クリアは値を戻すこと）。
    /// </summary>
    private void ClearFilters(bool apply = true)
    {
        _queryText = string.Empty;
        _queryNode = new Core.Services.SearchNode.All();
        ClearWidening();

        foreach (var module in Modules)
        {
            module.Clear();
        }

        OnPropertyChanged(nameof(QueryText));
        SaveModulesLater();

        if (apply)
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        // 足跡は1回だけ読んで、この絞り込みの間は使い回す。
        // 1商品ごとに読み直すと、件数に比例してファイルを開くことになる
        _recentTimes = NeedsRecent() ? LoadRecentTimes() : null;

        // 改変はファイルを読むので待てない。**読めたらもう一度絞り込む**——
        // 一度きりの読みにしておくと、条件を積んだ直後だけ0件に見える
        if (NeedsModifications())
        {
            EnsureModificationsLoaded();
        }

        _matches = FilterMatches();

        // 別表記は「別表記でも検索」を入れているときだけ広げる。前は0件のときに自動で広げていたが、
        // 切っているのに広げると切っている意味が無い（ユーザ判断 2026-09-16）。0件の所にボタンを出す
        if (_searchAlternates)
        {
            TryWiden();
        }

        RefreshFacetCounts();
        RebuildRows();

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowsWidenOffer));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>今の条件で全商品を照らし、並べる。照らす材料（改変・足跡・素体の索引）はこの1回で使い回す。</summary>
    private List<ItemCardViewModel> FilterMatches()
    {
        // 非表示は、非表示の条件を足して効かせていなければ隠す（今までどおり・ユーザ判断 Q12）
        _allowsHidden = Modules.Any(module => module.Kind == SearchModuleKind.Hidden && module.IsEnabled);
        _hiddenCount = _allowsHidden ? 0 : _allItems.Count(item => item.Local.IsHidden);
        _moduleContext = CreateModuleContext();
        _activeModules = Modules.Where(module => module.IsActive).ToList();

        return SortItems(_allItems.Where(item => Matches(item)))
            .Select(item => _cards[item.Id])
            .ToList();
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
        var options = _bridgeOptions;
        var useCoined = _useCoined;

        Task.Run(() =>
        {
            var used = new Dictionary<string, List<Core.Search.BridgeCandidate>>(StringComparer.Ordinal);
            var widened = options.Any ? _services.Bridge.Widen(node, used, options) : node;

            // 造語変換は語を作らず、商品名の読みで照らすだけ。別表記が1つも作れなくても照らし直す
            if (used.Count == 0 && !useCoined)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                // 待っている間に打ち直されていたら捨てる
                if (token != _widenToken || !_searchAlternates)
                {
                    return;
                }

                _widenedNode = widened;
                _widenedTerms.Clear();
                foreach (var (term, candidates) in used)
                {
                    _widenedTerms[term] = candidates;
                }

                RefreshSearchOptions();
                _matches = FilterMatches();

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
                OnPropertyChanged(nameof(ShowsWidenOffer));
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
        RefreshSearchOptions();
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
    private List<string> FilterParts() => Modules.Where(module => module.IsActive).Select(module => module.SummaryText).ToList();

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
    /// 設定と非表示で、条件より先に外す商品。
    /// **前は検索画面だけ外していなかった**（設定「R-18 の商品を表示する」を切っても、非表示にしても検索には出ていた）。
    /// </summary>
    private bool PassesBase(ItemRecord item)
        => (_allowsHidden || !item.Local.IsHidden) && (_services.Settings.ShowAdult || !item.Booth.IsAdult);

    /// <param name="except">この条件だけ当てない。選択肢の件数を数えるときに指定する。</param>
    private bool Matches(ItemRecord item, SearchModule? except = null)
    {
        if (!PassesBase(item))
        {
            return false;
        }

        var context = _moduleContext ??= CreateModuleContext();
        foreach (var module in _activeModules)
        {
            if (!ReferenceEquals(module, except) && !module.Matches(item, context))
            {
                return false;
            }
        }

        return MatchesQuery(item);
    }

    /// <summary>
    /// 選択肢の横に出す件数を数え直す。
    ///
    /// 数えるのは「今の他の条件を適用した後」の件数。全体の件数だと、押してから0件と分かる。
    /// ただし自分の条件は自分を除いて数える。含めて数えると、選んだ瞬間に同じ条件の他の選択肢が全部0件になる。
    /// </summary>
    private void RefreshFacetCounts()
    {
        var context = _moduleContext ??= CreateModuleContext();
        foreach (var module in Modules)
        {
            var others = _allItems.Where(item => Matches(item, module)).ToList();
            module.RefreshCounts(others, context);
        }
    }

    private bool MatchesQuery(ItemRecord item)
        => !_haystacks.TryGetValue(item.Id, out var haystack)
            // 読みは広げていて造語変換が入のときだけ見る（_searchOptions.IncludeReadings）。組み立てた「あり得る読み」には
            // 外れも混じるので、普段の検索から当たると「なぜこれが出たのか」が説明できなくなる
            || Core.Services.SearchQuery.Matches(_widenedNode ?? _queryNode, haystack, _searchOptions);

    /// <summary>
    /// この絞り込みで足跡が要るか。
    ///
    /// 積んでいなければ読まない。関係の無い検索でファイルを開く理由が無い。
    /// </summary>
    private bool NeedsRecent() => Modules.Any(module => module is RecentModule && module.IsActive);

    /// <summary>足跡を1回だけ読んで3種類に分ける（前は種類ごとに読み直していた）。</summary>
    private RecentTimes LoadRecentTimes() => _services.Recent.AllTimes();

    /// <summary>改変を読む必要があるか。改変・Unityプロジェクトの条件を足していなければ読まない。</summary>
    private bool NeedsModifications()
        => Modules.Any(module => module.Kind is SearchModuleKind.Modification or SearchModuleKind.UnityProject);

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
            _loadingModifications = false;

            foreach (var module in Modules.Where(module => module.Kind is SearchModuleKind.Modification or SearchModuleKind.UnityProject))
            {
                RefreshModuleSource(module);
            }

            ApplyFilters();
        });
    }

    /// <summary>
    /// 改変が変わったので、読み直す。
    ///
    /// 改変は別の画面で増えたり減ったりする。持ち続けると
    /// 「作ったのに絞り込みに出ない」が起きる。
    /// </summary>
    public void NoteModificationsChanged()
    {
        _modificationUsage = null;
        if (NeedsModifications())
        {
            EnsureModificationsLoaded();
        }
    }

    /// <summary>その並び順が「最近」の足跡を見るものなら、どの種類か。</summary>
    private static Core.Services.RecentKind? RecentKindOf(SortKind kind) => kind switch
    {
        SortKind.RecentlyAdded => Core.Services.RecentKind.Added,
        SortKind.RecentlyUsed => Core.Services.RecentKind.Used,
        SortKind.RecentlyViewed => Core.Services.RecentKind.Viewed,
        _ => null,
    };

    /// <summary>
    /// 表示順を適用する。属性で並べたときは、未評価を昇順・降順どちらでも常に末尾に置く。
    /// 未評価は「値が小さい」のではなく「値が無い」ので、0として混ぜると誤読させる。
    /// </summary>
    private IEnumerable<ItemRecord> SortItems(IEnumerable<ItemRecord> items)
    {
        var sort = _sort;

        // 並べ方の決まり（値が無い商品を向きによらず後ろにまとめる・同じ値の中の2つ目の鍵・名前の読みの順）は
        // Core の ItemOrder にある（試験付き）。ここは選んだ項目を呼び分けるだけ
        var names = _services.NameOrder;
        var descending = sort.Descending;

        if (sort.Kind == SortKind.Attribute && sort.AttributeName is { } attributeName)
        {
            return Core.Services.ItemOrder.ByAttribute(items, attributeName, descending, names);
        }

        if (RecentKindOf(sort.Kind) is { } recentKind)
        {
            return Core.Services.ItemOrder.ByTime(items, _services.Recent.Times(recentKind), descending, names);
        }

        return sort.Kind switch
        {
            SortKind.Name => Core.Services.ItemOrder.ByName(items, descending, names),
            SortKind.Size => Core.Services.ItemOrder.BySize(items, descending, names),
            SortKind.WishList => Core.Services.ItemOrder.ByWishList(items, descending, names),
            SortKind.SelfPaid => Core.Services.ItemOrder.BySelfPaid(items, descending, names),
            SortKind.PublishedAt => Core.Services.ItemOrder.ByPublished(items, descending, names),
            SortKind.BoothPrice => Core.Services.ItemOrder.ByBoothPrice(items, descending, names),
            SortKind.Shop => Core.Services.ItemOrder.ByShop(items, descending, names),
            SortKind.Category => Core.Services.ItemOrder.ByCategory(items, _services.Categories, descending, names),
            _ => Core.Services.ItemOrder.ByAcquired(items, descending, names),
        };
    }

    internal ItemCardViewModel ToCard(ItemRecord item)
        => ToCard(
            item,
            _main?.IsImporting == true
                && _services.Settings.SaveImages
                && item.Booth.Images.Count > 0
                && !_thumbnails.ListFiles(_services.Paths.ItemImagesDir(item.Id)).Any());

    /// <summary>
    /// 読み直しで前のカードをそのまま使えるか。記録が同じでも、カードは記録の外の値
    /// （③待ちか・絵が届いたか・設定のサムネイルの役割と札の出し方）も映しているので、それも前と同じときだけ
    /// </summary>
    private bool CardStillFits(ItemCardViewModel card, ItemRecord item, bool imagePending)
        => card.IsAwaitingDetection == (_main?.IsAwaitingDetection(item.Id) == true)
           && card.IsImagePending == imagePending
           && card.ThumbnailRole == _services.Settings.ThumbnailRole
           && card.UserTagText == ItemCardViewModel.UserTagLine(item.Local.UserTags, _services.Settings.ShowSubTagsInList);

    /// <param name="imagePending">取り込みの途中で、絵がまだ1枚も無いか。フォルダを見るのは呼び手（読み直しは裏でまとめて見る）。</param>
    private ItemCardViewModel ToCard(ItemRecord item, bool imagePending)
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
            IsImagePending = imagePending,
            HasMissingFile = missing,
            UserTagText = ItemCardViewModel.UserTagLine(item.Local.UserTags, _services.Settings.ShowSubTagsInList),
        };
    }
}
