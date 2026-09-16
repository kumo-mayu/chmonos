using System.Collections.ObjectModel;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：絞り込みのモジュール（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>ユーザタグの「親›子」の鍵の区切り。名前で繋ぐので、タグの名前に入らない字にする。</summary>
    private const char UserTagSeparator = '›';

    private readonly Dictionary<SearchModuleKind, SearchModuleMenuEntry> _moduleMenuEntries = [];

    /// <summary>候補の元（全商品・マスタ）を読み終えたか。読む前に候補を入れると、戻した値（属性など）が「候補に無い」として外れる。</summary>
    private bool _moduleSourcesReady;

    /// <summary>非表示の商品を出すか。絞り込み1回ぶんの間だけ持つ。</summary>
    private bool _allowsHidden;

    private int _hiddenCount;
    private int _moduleSaveToken;
    private bool _loadingModifications;
    private SearchModuleContext? _moduleContext;
    private List<SearchModule> _activeModules = [];

    /// <summary>追加してある条件。並びは追加した順。</summary>
    public ObservableCollection<SearchModule> Modules { get; } = [];

    /// <summary>「条件を追加」のメニュー。見出しの下に条件が並ぶ（同じ条件が2つの見出しに出ることがある）。</summary>
    public IReadOnlyList<SearchModuleMenuHeading> ModuleMenu { get; private set; } = [];

    public bool HasModules => Modules.Count > 0;

    /// <summary>
    /// 前回の条件を戻す。**値まで戻す**（ユーザ判断 2026-09-16 Q10）。
    /// 一度も保存していなければ最低限の条件（所持・ユーザタグ・対応アバター）で始める（Q9）。
    /// </summary>
    private void InitializeModules(IReadOnlyList<SearchModuleState>? saved)
    {
        foreach (var info in SearchModuleCatalog.All)
        {
            _moduleMenuEntries[info.Kind] = new SearchModuleMenuEntry(info, kind => AddModule(kind));
        }

        ModuleMenu = SearchModuleCatalog.Headings
            .Select(title => new SearchModuleMenuHeading(
                title,
                SearchModuleCatalog.All
                    .Where(info => info.Headings.Contains(title))
                    .Select(info => _moduleMenuEntries[info.Kind])
                    .ToList()))
            .ToList();

        if (saved is null)
        {
            foreach (var kind in SearchModuleCatalog.Defaults)
            {
                AddModule(kind, apply: false);
            }
        }
        else
        {
            foreach (var state in saved)
            {
                // 読めない種類（名前を変えた・無くした）は黙って飛ばす。公開前なので古い名前の読み替えは作らない
                if (Enum.TryParse<SearchModuleKind>(state.Kind, out var kind))
                {
                    AddModule(kind, state, apply: false);
                }
            }
        }

        RefreshModuleMenu();
    }

    private SearchModule AddModule(SearchModuleKind kind, SearchModuleState? state = null, bool apply = true)
    {
        if (Modules.FirstOrDefault(module => module.Kind == kind) is { } existing)
        {
            return existing;
        }

        var module = CreateModule(kind);
        if (state is not null)
        {
            module.Load(state);
        }

        module.Changed += OnModuleChanged;

        // 畳んだ・開いたは結果を変えないので、絞り直さずに状態だけ書く
        module.ViewChanged += SaveModulesLater;
        module.RemoveCommand = new RelayCommand(() => RemoveModule(module));
        Modules.Add(module);
        RefreshModuleSource(module);
        RefreshModuleMenu();

        if (apply)
        {
            SaveModulesLater();
            ApplyFilters();
        }

        return module;
    }

    /// <summary>
    /// 条件の順番を入れ替える（ドラッグ・ユーザ指示 2026-09-16）。よく触る条件を上に置けるようにするため。
    /// 結果は変わらないので絞り直さない。順番は状態に残る。
    /// </summary>
    public void MoveModule(SearchModule moved, SearchModule target, bool after)
    {
        var from = Modules.IndexOf(moved);
        var to = Modules.IndexOf(target);
        if (from < 0 || to < 0 || ReferenceEquals(moved, target))
        {
            return;
        }

        // 抜いた分だけ落とし先がずれる（自分より後ろへ動かすとき）
        var destination = after ? to + 1 : to;
        if (destination > from)
        {
            destination--;
        }

        if (destination != from)
        {
            Modules.Move(from, destination);
            SaveModulesLater();
            OnPropertyChanged(nameof(FilterSummary));
        }
    }

    private void RemoveModule(SearchModule module)
    {
        module.Changed -= OnModuleChanged;
        module.ViewChanged -= SaveModulesLater;
        Modules.Remove(module);
        RefreshModuleMenu();
        SaveModulesLater();
        ApplyFilters();
    }

    /// <summary>他の画面から条件を渡すとき：無ければ足し、切ってあれば入れる。絞り直しは呼ぶ側がまとめて1回。</summary>
    private T EnsureModule<T>(SearchModuleKind kind)
        where T : SearchModule
    {
        var module = AddModule(kind, apply: false);
        module.SetEnabledQuietly(true);
        return (T)module;
    }

    private void OnModuleChanged()
    {
        SaveModulesLater();
        ApplyFilters();
    }

    private void RefreshModuleMenu()
    {
        foreach (var (kind, entry) in _moduleMenuEntries)
        {
            entry.IsAvailable = Modules.All(module => module.Kind != kind);
        }

        OnPropertyChanged(nameof(HasModules));
    }

    /// <summary>
    /// 条件を画面の状態に書く。打つたびに書かず、止まってから1回（スライダを動かす間に何十回も書かない）。
    /// 要約は書かない（計算で出せる値）。
    /// </summary>
    private void SaveModulesLater()
    {
        var token = ++_moduleSaveToken;
        Task.Delay(500).ContinueWith(
            _ => RunOnUiThread(() =>
            {
                if (token != _moduleSaveToken)
                {
                    return;
                }

                var states = Modules.Select(module => module.Save() with { Summary = null }).ToList();
                _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
                    state => state with { SearchModules = states })).Forget();
            }),
            TaskScheduler.Default);
    }

    /// <summary>絞り込み1回ぶんの材料。</summary>
    private SearchModuleContext CreateModuleContext() => new(
        () => _compatibility ??= AvatarCompatibilityIndex.Build(_services.Store.Avatars.Load()),
        _modificationUsage ?? ModificationUsage.Empty,
        _recentTimes ?? RecentTimes.Empty,
        _services.Volumes.Current,
        DateTimeOffset.Now);

    /// <summary>候補・スライダの右端・使えない理由を入れ直す（読み込み・マスタの変更のあと）。</summary>
    private void RefreshModuleSources()
    {
        foreach (var module in Modules.ToList())
        {
            RefreshModuleSource(module);
        }
    }

    private void RefreshModuleSource(SearchModule module)
    {
        // R-18 は設定を優先する（ユーザ判断 Q11）。設定で隠しているときに「R-18のみ」を選べても0件になるだけなので、理由を書いて使わない
        module.DisabledReason = module.Kind == SearchModuleKind.Adult && !_services.Settings.ShowAdult
            ? "設定で R-18 の商品を隠しているので、この条件は使えません。設定の「R-18 の商品を表示する」を入れると使えます。"
            : null;

        if (!_moduleSourcesReady)
        {
            return;
        }

        switch (module)
        {
            case ListModule list:
                list.SetSuggestions(CandidatesFor(list.Kind));
                break;
            case AttributeModule attribute:
                attribute.SetNames(_attributeNames);
                break;
            case RangeModule range:
                range.RefreshBounds();
                break;
            case DateModule date:
                date.RefreshBounds();
                break;
        }
    }

    private IEnumerable<(string Text, string Key)> CandidatesFor(SearchModuleKind kind) => kind switch
    {
        // カテゴリは親（「3Dモデル」など）でも選べる。子だけだと1商品に1つなので AND が意味を持たない
        SearchModuleKind.Category => _allItems
            .SelectMany(item => new[] { item.CategoryName, item.Booth.Category?.ParentName })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .Select(name => (name, name)),
        SearchModuleKind.BoothTag => _boothTagNames.Select(name => (name, name)),
        SearchModuleKind.Shop => ShopCandidates(),
        SearchModuleKind.UserTag => UserTagCandidates(),
        SearchModuleKind.Avatar => AvatarCandidates(),
        SearchModuleKind.Modification => ModificationCandidates(),
        SearchModuleKind.UnityProject => UnityProjectCandidates(),
        SearchModuleKind.Path => FolderTree.AllFolders(_allItems, _services.Volumes.Current).Select(path => (path, path)),
        _ => [],
    };

    /// <summary>ショップは名前が変わりうるので鍵はサブドメイン。候補の文字にも入れておく（同じ名前のショップを見分ける）。</summary>
    private IEnumerable<(string Text, string Key)> ShopCandidates()
        => _allItems
            .Where(item => item.ShopSubdomain is not null)
            .GroupBy(item => item.ShopSubdomain!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Select(item => item.ShopName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key, group.Key))
            .OrderBy(pair => pair.Name, StringComparer.CurrentCulture)
            .Select(pair => ($"{pair.Name}（{pair.Key}）", pair.Key));

    private IEnumerable<(string Text, string Key)> UserTagCandidates()
    {
        foreach (var top in _services.Store.UserTags.Load().Tops)
        {
            yield return (top.Name, top.Name);
            foreach (var sub in top.Subs)
            {
                yield return ($"{top.Name} {UserTagSeparator} {sub.Name}", $"{top.Name}{UserTagSeparator}{sub.Name}");
            }
        }
    }

    /// <summary>
    /// 対応アバターの候補。並びは 持っているアバター → 共通素体 → 持っていないアバター（ユーザ判断 Q1）。
    /// 共通素体は独立の条件にせず、ここに混ぜる（素体に属するアバターなら、衣装は素体の名前で探すことが多い）。
    /// </summary>
    private IEnumerable<(string Text, string Key)> AvatarCandidates()
    {
        var registry = _services.Store.Avatars.Load();
        var names = AvatarNames.Map(registry.Entries);
        var owned = OwnedItemIds();

        var avatars = registry.Entries
            .Where(AvatarService.IsAvatar)
            .Select(entry => (Entry: entry, Name: names.TryGetValue(entry.ItemId, out var name) ? name : entry.ItemId))
            .OrderBy(pair => pair.Name, StringComparer.CurrentCulture)
            .ToList();

        bool IsOwned(AvatarRegistryEntry entry) => entry.IsOwnedManually || owned.Contains(entry.ItemId);

        foreach (var (entry, name) in avatars.Where(pair => IsOwned(pair.Entry)))
        {
            yield return (AvatarSuggestionText.Format(name, entry.ItemId), AvatarKey + entry.ItemId);
        }

        foreach (var baseName in registry.BaseGroups.Select(group => group.Name)
            .Concat(registry.Entries.Select(entry => entry.BaseName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture))
        {
            yield return ($"{baseName}（共通素体）", BaseKey + baseName);
        }

        foreach (var (entry, name) in avatars.Where(pair => !IsOwned(pair.Entry)))
        {
            yield return (AvatarSuggestionText.Format(name, entry.ItemId), AvatarKey + entry.ItemId);
        }
    }

    /// <summary>改変の候補。アバターでも選べる（そのアバターの改変すべて・ユーザ判断 R3 の「着せているアバター」）。</summary>
    private IEnumerable<(string Text, string Key)> ModificationCandidates()
    {
        EnsureModificationsLoaded();
        var records = (_modificationUsage ?? ModificationUsage.Empty).Records;
        var names = AvatarNames.Map(_services.Store.Avatars.Load().Entries);
        string AvatarName(string id) => names.TryGetValue(id, out var name) ? name : id;

        foreach (var avatarId in records.Select(record => record.AvatarItemId).Distinct(StringComparer.Ordinal)
            .OrderBy(AvatarName, StringComparer.CurrentCulture))
        {
            yield return ($"{AvatarName(avatarId)}（このアバターの改変すべて）", AvatarKey + avatarId);
        }

        foreach (var record in records.OrderBy(record => AvatarName(record.AvatarItemId), StringComparer.CurrentCulture)
            .ThenBy(record => record.Name, StringComparer.CurrentCulture))
        {
            yield return ($"{AvatarName(record.AvatarItemId)}：{record.Name}", ModificationKey + record.Id);
        }
    }

    private IEnumerable<(string Text, string Key)> UnityProjectCandidates()
    {
        EnsureModificationsLoaded();
        return (_modificationUsage ?? ModificationUsage.Empty).Records
            .Where(record => record.HasUnityProject)
            .Select(record => record.UnityProject!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCulture)
            .Select(path => ($"{System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))}（{path}）", path));
    }

    private const string AvatarKey = "avatar:";
    private const string BaseKey = "base:";
    private const string ModificationKey = "mod:";

    private SearchModule CreateModule(SearchModuleKind kind) => kind switch
    {
        SearchModuleKind.Category => new ListModule(kind, allowsAnd: true, "カテゴリで絞り込む",
            "カテゴリがまだありません。商品を取り込むと付いてきます。",
            (item, _, key, _) => string.Equals(item.CategoryName, key, StringComparison.CurrentCulture)
                || string.Equals(item.Booth.Category?.ParentName, key, StringComparison.CurrentCulture)),

        SearchModuleKind.BoothTag => new ListModule(kind, allowsAnd: true, "BOOTHタグで絞り込む",
            "BOOTHタグがまだありません。商品を取り込むと付いてきます。",
            (item, _, key, _) => item.Booth.Tags.Any(tag => string.Equals(tag, key, StringComparison.CurrentCultureIgnoreCase))),

        // 1商品に1つなので「すべて（AND）」は意味が無い
        SearchModuleKind.Shop => new ListModule(kind, allowsAnd: false, "ショップ名で絞り込む",
            "ショップの分かる商品がまだありません。",
            (item, _, key, _) => string.Equals(item.ShopSubdomain, key, StringComparison.OrdinalIgnoreCase)),

        SearchModuleKind.WishList => new RangeModule(kind, (item, _) => [item.Booth.WishListsCount], string.Empty)
        {
            AllValuesOf = _ => _allItems.Select(item => item.Booth.WishListsCount),
        },

        SearchModuleKind.Price => new RangeModule(kind, PriceValues, "円",
            [new ChoiceOption(PaidSource, "購入額"), new ChoiceOption(BoothSource, "BOOTHの価格")])
        {
            AllValuesOf = source => _allItems.SelectMany(item => PriceValues(item, source)),

            // BOOTH の有料販売は100円から。1〜99円はあり得ないので、目盛を取らせない（ユーザ指摘 2026-09-16）
            Floor = 100,

            // 支援用の種類や、販売を止めるためのあり得ない高値を外せるようにする（ユーザ判断 2026-09-16）
            SupportsOutliers = true,
        },

        SearchModuleKind.EndOfSale => new ChoiceModule(kind,
            [new("ended", "販売終了のみ"), new("both", "販売中と販売終了の両方"), new("selling", "販売中のみ")],
            "both", EndOfSaleMatches, "非公開・削除された商品も表示する"),

        SearchModuleKind.PublishedAt => new DateModule(kind,
            item => item.Booth.PublishedAt is { } at ? DateOnly.FromDateTime(at.LocalDateTime) : null)
        {
            AllDatesOf = () => _allItems
                .Select(item => item.Booth.PublishedAt)
                .Where(at => at is not null)
                .Select(at => DateOnly.FromDateTime(at!.Value.LocalDateTime)),
        },

        SearchModuleKind.Adult => new ChoiceModule(kind,
            [new("adult", "R-18のみ"), new("general", "R-18以外のみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "adult" => item.Booth.IsAdult,
                "general" => !item.Booth.IsAdult,
                _ => true,
            }),

        SearchModuleKind.Owned => new ChoiceModule(kind,
            [new("owned", "所持している"), new("unowned", "所持していない"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "owned" => IsOwned(item),
                "unowned" => !IsOwned(item),
                _ => true,
            }),

        // 純三項：「何も絞らない」選択肢を持たない。切るときは条件の切り替えで
        SearchModuleKind.Gift => new ChoiceModule(kind,
            [new("received", "ギフトされた"), new("given", "ギフトした"), new("other", "その他（自分で入手・記録なし）")],
            null, (item, key, _) => key switch
            {
                "received" => Purchases.WasReceived(item),
                "given" => Purchases.WasGiven(item),

                // 購入記録の無い商品は「その他」（ユーザ判断 Q4）。貰って自分でも買った物は両方に出る
                _ => item.Local.Purchases.Count == 0 || item.Local.Purchases.Any(purchase => purchase.Kind == PurchaseKind.ForSelf),
            }),

        SearchModuleKind.FreePaid => new ChoiceModule(kind,
            [new("free", "無料のみ"), new("paid", "有料のみ"), new("both", "両方")],
            "both", (item, key, _) => FreePaidMatches(item, key)),

        SearchModuleKind.UserTag => new ListModule(kind, allowsAnd: true, "ユーザタグで絞り込む",
            "ユーザタグがまだ登録されていません。編集画面から追加できます。", (item, _, key, _) => UserTagMatches(item, key)),

        SearchModuleKind.Attribute => new AttributeModule(),

        // 素体経由は推定なので含めるかを選べるようにする。既定で含めるのは「対応が確認できていないものを既定で隠さない」方針
        SearchModuleKind.Avatar => new ListModule(kind, allowsAnd: true, "アバター名・商品ID・共通素体で絞り込む",
            "アバターがまだ見つかっていません。アバターの管理から検出できます。", AvatarMatches,
            "素体経由の対応も含める", flagDefault: true,
            isUnspecified: IsUnspecifiedAvatar)
        {
            IconSelector = AvatarIconSelector,
        },

        SearchModuleKind.Favorite => new ChoiceModule(kind,
            [new("favorite", "お気に入りのみ"), new("other", "お気に入り以外のみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "favorite" => item.Local.IsFavorite,
                "other" => !item.Local.IsFavorite,
                _ => true,
            }),

        SearchModuleKind.AcquiredAt => new DateModule(kind, item => item.Local.AcquiredAt)
        {
            AllDatesOf = () => _allItems
                .Select(item => item.Local.AcquiredAt)
                .Where(date => date is not null)
                .Select(date => date!.Value),
        },

        SearchModuleKind.Hidden => new ChoiceModule(kind,
            [new("hidden", "非表示のみ"), new("both", "両方"), new("visible", "表示している商品のみ")],
            "both", (item, key, _) => key switch
            {
                "hidden" => item.Local.IsHidden,
                "visible" => !item.Local.IsHidden,
                _ => true,
            }),

        // 未編集＝ユーザタグが0件（ユーザ判断 Q6）。取り込みの③がまだの商品は数えない（編集画面に出てこないため・U8・U10）
        SearchModuleKind.Unedited => new ChoiceModule(kind,
            [new("unedited", "未編集のみ"), new("edited", "編集済みのみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "unedited" => item.Local.UserTags.Count == 0 && _main?.IsAwaitingDetection(item.Id) != true,
                "edited" => item.Local.UserTags.Count > 0,
                _ => true,
            }),

        SearchModuleKind.Modification => new ListModule(kind, allowsAnd: true, "改変の名前かアバター名で絞り込む",
            "改変がまだありません。アバターの管理から作れます。",
            (item, context, key, _) => key.StartsWith(AvatarKey, StringComparison.Ordinal)
                ? context.Modifications.Used(key[AvatarKey.Length..], item.Id)
                : key.StartsWith(ModificationKey, StringComparison.Ordinal)
                    && context.Modifications.InModification(key[ModificationKey.Length..], item.Id)),

        // 改変を通してそのプロジェクトに紐付いた商品（ユーザ判断 Q7）。プロジェクトの中身は見ない（開くたびに照らすと重い）
        SearchModuleKind.UnityProject => new ListModule(kind, allowsAnd: true, "プロジェクトの名前で絞り込む",
            "Unityプロジェクトを紐付けた改変がまだありません。",
            (item, context, key, _) => context.Modifications.InProject(key, item.Id)),

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる
        SearchModuleKind.Path => new ListModule(kind, allowsAnd: true, "フォルダの名前で絞り込む",
            "手元にファイルのある商品がまだありません。",
            (item, context, key, _) => FolderTree.IsUnder(item, key, context.PathMap)),

        SearchModuleKind.Recent => new RecentModule(),

        SearchModuleKind.FavoriteShop => new ChoiceModule(kind,
            [new("favorite", "お気に入りのショップの商品のみ"), new("other", "それ以外のショップの商品のみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "favorite" => IsFavoriteShop(item),
                "other" => !IsFavoriteShop(item),
                _ => true,
            }),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private const string PaidSource = "paid";
    private const string BoothSource = "booth";

    /// <summary>
    /// お気に入りのショップの鍵（shops.json の星）。読み込みのときと、ショップ画面で星を変えたときに入れ直す。
    /// 1商品ごとにファイルを読まないよう、ここに持つ。
    /// </summary>
    private IReadOnlySet<string> _favoriteShops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool IsFavoriteShop(ItemRecord item)
        => item.ShopSubdomain is { } key && _favoriteShops.Contains(key);

    /// <summary>ショップ画面で星やメモを変えたことを知る。お気に入りのショップの条件を足していれば絞り直す。</summary>
    public void NoteShopNotesChanged(IReadOnlyList<ShopNoteRecord> notes)
    {
        _favoriteShops = ShopNotes.FavoriteKeys(notes);
        if (Modules.Any(module => module.Kind == SearchModuleKind.FavoriteShop))
        {
            ApplyFilters();
        }
    }

    /// <summary>所持＝ファイルかフォルダを1つ以上持つ。</summary>
    private static bool IsOwned(ItemRecord item) => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0;

    /// <summary>
    /// 価格の条件で照らす数。既定は自分が払った額（ユーザ判断 Q2）。BOOTH の価格は種類ごとにあり、どれか1つでも範囲に入れば当たり。
    /// 払った額を入れていない商品は「0円」ではなく「分からない」ので、範囲のどこにも入らない。
    /// </summary>
    private static IReadOnlyList<int> PriceValues(ItemRecord item, string? source)
    {
        if (source == BoothSource)
        {
            return item.Booth.Variations.Select(variation => variation.Price).ToList();
        }

        var priced = item.Local.Purchases
            .Where(purchase => purchase.Kind == PurchaseKind.ForSelf && purchase.Price is not null)
            .ToList();
        return priced.Count == 0 ? [] : [priced.Sum(purchase => purchase.Price!.Value)];
    }

    /// <summary>
    /// 有料・無料。払った額があればそれで決め、無ければ BOOTH の種類ごとの価格（無料と有料の種類が両方あれば両方に出す）。
    /// どちらも分からない商品（BOOTH に無く、額も入れていない）は「両方」のときだけ出す（ユーザ判断 Q5）。
    /// </summary>
    private static bool FreePaidMatches(ItemRecord item, string key)
    {
        if (key is not ("free" or "paid"))
        {
            return true;
        }

        IReadOnlyList<int> prices = item.Local.Purchases
            .Where(purchase => purchase.Kind == PurchaseKind.ForSelf && purchase.Price is not null)
            .Select(purchase => purchase.Price!.Value)
            .ToList();

        if (prices.Count == 0)
        {
            prices = item.Booth.Variations.Select(variation => variation.Price).ToList();
        }

        return key == "free" ? prices.Any(price => price == 0) : prices.Any(price => price > 0);
    }

    /// <summary>
    /// 販売終了。非公開・削除（BOOTH で見つからないのが続いた）も販売終了に含めるが、基本は隠す（ユーザ判断 R1）。
    /// 売り切れは含めない（在庫の話で、販売が終わったわけではない）。
    /// </summary>
    private static bool EndOfSaleMatches(ItemRecord item, string key, bool showDelisted)
    {
        if (item.Local.IsDelisted && !showDelisted)
        {
            return false;
        }

        var ended = item.Booth.IsEndOfSale || item.Local.IsDelisted;
        return key switch
        {
            "ended" => ended,
            "selling" => !ended,
            _ => true,
        };
    }

    private static bool UserTagMatches(ItemRecord item, string key)
    {
        var separator = key.IndexOf(UserTagSeparator);
        var top = separator < 0 ? key : key[..separator];
        var assignment = item.Local.UserTags.FirstOrDefault(entry =>
            string.Equals(entry.Top, top, StringComparison.CurrentCultureIgnoreCase));

        return assignment is not null
            && (separator < 0 || assignment.Subs.Contains(key[(separator + 1)..], StringComparer.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// 対応アバターの**指定が無い**商品か（どのアバターにも使える扱いにするかたまり・ユーザ判断 2026-09-16）。
    ///
    /// 出品者の宣言（消していない分）と共通素体の宣言が両方とも無いこと。1つでも書いてあるものは書いてある内容どおりに照らす。
    /// 要確認（説明文のリンク）は宣言に数えていない（絞り込みでも数えない）ので、それだけの商品は「指定が無い」側に入る。
    /// </summary>
    private static bool IsUnspecifiedAvatar(ItemRecord item, SearchModuleContext context)
        => context.Compatibility.Resolve(item.Local).Count == 0
            && item.Local.AvatarBases.All(link => link.Rejected);

    private static bool AvatarMatches(ItemRecord item, SearchModuleContext context, string key, bool viaBase)
    {
        var resolved = context.Compatibility.Resolve(item.Local);

        if (key.StartsWith(AvatarKey, StringComparison.Ordinal))
        {
            return resolved.TryGetValue(key[AvatarKey.Length..], out var match)
                && (match == AvatarMatch.Direct || (viaBase && match == AvatarMatch.ViaBase));
        }

        if (!key.StartsWith(BaseKey, StringComparison.Ordinal))
        {
            return false;
        }

        // 共通素体：素体への対応を宣言している商品。素体経由を含めるなら、素体に属するアバターに対応している商品も
        var baseName = key[BaseKey.Length..];
        if (item.Local.AvatarBases.Any(link => !link.Rejected
            && string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return true;
        }

        if (!viaBase)
        {
            return false;
        }

        var members = context.Compatibility.MembersOf(baseName);
        return members.Count > 0 && members.Any(resolved.ContainsKey);
    }

    /// <summary>改変を読む（候補と「改変」「Unityプロジェクト」の条件に要る）。読めたら候補を入れ直し、絞り直す。</summary>
    private void EnsureModificationsLoaded()
    {
        if (_modificationUsage is null && !_loadingModifications)
        {
            _loadingModifications = true;
            LoadModificationUsageAsync().Forget();
        }
    }

    /// <summary>
    /// 「対応アバター」の候補の頭に出す絵（U18）。候補の行（名前（ID））からIDを取り出し、
    /// 持っていれば商品の1枚目、持っていなければ控えの1枚。候補は見えた行だけで読む
    /// </summary>
    public Func<string, System.Windows.Media.ImageSource?> AvatarIconSelector => entry =>
        AvatarSuggestionText.IdOf(entry) is { } id
        && Core.Services.AvatarImageSync.IconPath(_services.Paths, id, FindItem(id)) is { } path
            ? _thumbnails.LoadForTile(path)
            : null;
}
