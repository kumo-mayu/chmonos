using System.Collections.ObjectModel;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>検索画面：絞り込みのモジュール（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）</summary>
public sealed partial class SearchViewModel
{
    private readonly Dictionary<SearchModuleKind, SearchModuleMenuEntry> _moduleMenuEntries = [];

    /// <summary>候補の元（全商品・マスタ）を読み終えたか。読む前に候補を入れると、戻した値（属性など）が「候補に無い」として外れる。</summary>
    private bool _moduleSourcesReady;

    /// <summary>非表示の商品を出すか。絞り込み1回ぶんの間だけ持つ。</summary>
    private bool _allowsHidden;

    private int _hiddenCount;
    private int _moduleSaveToken;
    private bool _loadingModifications;
    private SearchModuleContext? _moduleContext;

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
            // メニューから足したときだけ、足した条件へ画面を送って止まる（人が足した物をすぐ触れるように）
            // （?. で呼ぶと、聞き手がいないときに引数の AddModule ごと飛ばされるので、足してから知らせる）
            _moduleMenuEntries[info.Kind] = new SearchModuleMenuEntry(info, kind =>
            {
                var added = AddModule(kind);
                ModuleAdded?.Invoke(added);
            });
        }

        // 見出しの中は意味のまとまりの順に並べ、まとまりの間に区切り線を入れる（ユーザ判断 2026-09-16・案1）
        ModuleMenu = SearchModuleCatalog.Menu
            .Select(layout => new SearchModuleMenuHeading(
                layout.Title,
                layout.Groups
                    .SelectMany((group, index) => (index == 0 ? [] : new object[] { new SearchModuleMenuSeparator() })
                        .Concat(group.Select(kind => (object)_moduleMenuEntries[kind])))
                    .ToList()))
            .ToList();

        if (saved is null)
        {
            foreach (var kind in SearchModuleCatalog.Defaults)
            {
                AddModule(kind, apply: false);
            }

            // 既定で出しておく条件は、足しただけでは絞らない形で始める。選ぶ形の条件は、人が足したときの既定が
            // 先頭（お気に入りなら「お気に入りのみ」）なので、そのままだと初めて開いた検索が0件になっていた
            // （2026-09-30。既定に「お気に入り」を入れた 2026-09-29 から。窓を出さずに描く台の場面で見つかった）
            foreach (var module in Modules.OfType<ChoiceModule>())
            {
                module.Clear();
            }
        }
        else
        {
            foreach (var state in saved)
            {
                // 読めない種類（名前を変えた・無くした）は黙って飛ばす。公開前なので古い名前の読み替えは作らない。
                // 1つまでの種類が2つ書かれていたら、2つ目は飛ばす（手で直した JSON。報告は要らない画面の状態）。
                // 並びは保存した並びのまま（設定の「同じ種類のすぐ下」は、人が新しく足すときの決まり）
                if (Enum.TryParse<SearchModuleKind>(state.Kind, out var kind)
                    && (SearchModuleCatalog.Of(kind).AllowsMany || Modules.All(module => module.Kind != kind)))
                {
                    AddModule(kind, state, apply: false, atEnd: true);
                }
            }
        }

        RefreshModuleMenu();
    }

    /// <summary>メニューから条件を足した。画面がその条件へ送り、入力欄に止まる。</summary>
    public event Action<SearchModule>? ModuleAdded;

    /// <summary>
    /// 条件を足す。1つまでの種類が既にあれば、それを返す（足さない）。
    /// 位置は既定で一番下、設定「追加する条件を同じ種類の条件のすぐ下に置く」なら同じ種類のそば（<see cref="SearchModuleOrder.InsertIndex"/>）。
    /// 設定は足すたびに今の値を読む（変えたら次に足すときから効く。今ある並びは動かさない）。
    /// </summary>
    /// <param name="atEnd">保存した並びを戻すとき。設定によらず一番下に積む。</param>
    private SearchModule AddModule(SearchModuleKind kind, SearchModuleState? state = null, bool apply = true, bool atEnd = false)
    {
        if (!SearchModuleCatalog.Of(kind).AllowsMany && Modules.FirstOrDefault(module => module.Kind == kind) is { } existing)
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
        module.MoveUpCommand = new RelayCommand(() => MoveModuleBy(module, -1), () => Modules.IndexOf(module) > 0);
        module.MoveDownCommand = new RelayCommand(
            () => MoveModuleBy(module, +1),
            () => Modules.IndexOf(module) is var index && index >= 0 && index < Modules.Count - 1);
        var index = atEnd
            ? Modules.Count
            : SearchModuleOrder.InsertIndex(Modules.Select(entry => entry.Kind).ToList(), kind, _services.Settings.PlaceNewConditionNearSameKind);
        Modules.Insert(index, module);
        RenumberModules();
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
            AfterReorder();
        }
    }

    /// <summary>
    /// 条件のメニューの「上へ移動」「下へ移動」（D9）。キーボードからも並べ替えられるようにする（前はドラッグだけ）。
    /// 動かした条件は枠が作り直されるので、その条件の「…」に止まり直してもらう。
    /// </summary>
    public void MoveModuleBy(SearchModule module, int delta)
    {
        var from = Modules.IndexOf(module);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Modules.Count)
        {
            return;
        }

        Modules.Move(from, to);
        AfterReorder();
        ModuleFocusRequested?.Invoke(module);
    }

    /// <summary>並びを変えた後。結果は変わらないので絞り直さない（要約の並びだけ変わる）。並びは状態に残す。</summary>
    private void AfterReorder()
    {
        RenumberModules();
        SaveModulesLater();
        OnPropertyChanged(nameof(FilterSummary));
    }

    /// <summary>
    /// 画面に「この条件の『…』へ止まり直して」と頼む。並べ替え・外すで枠が作り直されると、キーボードの止まり先が消えるため
    /// （`ui-input.md`「止まっていた行が消えたら」）。外したときは次の条件、最後なら前の条件。条件が無くなれば null。
    /// </summary>
    public event Action<SearchModule?>? ModuleFocusRequested;

    private void RemoveModule(SearchModule module)
    {
        var index = Modules.IndexOf(module);
        module.Changed -= OnModuleChanged;
        module.ViewChanged -= SaveModulesLater;
        Modules.Remove(module);
        RenumberModules();
        RefreshModuleMenu();
        SaveModulesLater();
        ApplyFilters();

        if (index >= 0)
        {
            ModuleFocusRequested?.Invoke(Modules.Count == 0 ? null : Modules[Math.Min(index, Modules.Count - 1)]);
        }
    }

    /// <summary>
    /// 同じ種類の何番目かを振り直す（読み上げの名前と ID の番号・D7）。並びの位置から毎回求める。
    /// 上へ／下へ移動が押せるかも並びで変わるので、ここで知らせる。
    /// </summary>
    private void RenumberModules()
    {
        var seen = new Dictionary<SearchModuleKind, int>();
        foreach (var module in Modules)
        {
            seen[module.Kind] = seen.GetValueOrDefault(module.Kind) + 1;
            module.Ordinal = seen[module.Kind];
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 他の画面から条件を渡すとき：その種類の**いちばん上**の条件に入れる。無ければ足し、切ってあれば入れる。絞り直しは呼ぶ側がまとめて1回。
    /// 入口は値を戻してから来るので、同じ種類の2つ目以降も空になっていて、絞るのは入れた1つだけ（D5）。
    /// 2つ目以降の空の条件は残す（人が足した物を勝手に消さない。クリアと同じ考え方）。
    /// </summary>
    private T EnsureModule<T>(SearchModuleKind kind)
        where T : SearchModule
    {
        var module = Modules.FirstOrDefault(entry => entry.Kind == kind) ?? AddModule(kind, apply: false);
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
            entry.IsAvailable = SearchModuleCatalog.Of(kind).AllowsMany || Modules.All(module => module.Kind != kind);
        }

        OnPropertyChanged(nameof(HasModules));
    }

    private RelayCommand? _groupModules;

    /// <summary>
    /// パネルの見出しのメニューの「同じ種類の条件を隣に並べる」（ユーザ判断 2026-10-01・案の §5）。
    /// 押したときに1回だけ並べ替える（常にまとまった形を保つのではない）。結果を変えないので絞り直さない。並びは覚える。戻す手段は付けない。
    /// 分かれている種類が無いときは押せない。
    /// </summary>
    public RelayCommand GroupModulesCommand => _groupModules ??= new RelayCommand(GroupModules, () => !ModulesGrouped);

    /// <summary>同じ種類の条件がもう隣に並んでいるか（並べても変わらない）。</summary>
    public bool ModulesGrouped => SearchModuleOrder.IsGrouped(Modules.Select(module => module.Kind).ToList());

    private void GroupModules()
    {
        var order = SearchModuleOrder.GroupByKind(Modules.Select(module => module.Kind).ToList());
        var wanted = order.Select(index => Modules[index]).ToList();

        // 作り直さず、動かすだけで合わせる（枠を作り直すと、入力の途中の値やフォーカスが消える）
        for (var target = 0; target < wanted.Count; target++)
        {
            var current = Modules.IndexOf(wanted[target]);
            if (current != target)
            {
                Modules.Move(current, target);
            }
        }

        AfterReorder();
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
                // 後から変わった（そちらが書く）か、閉じる前の書き切りがもう書いた
                if (token != _moduleSaveToken || token == _moduleSavedToken)
                {
                    return;
                }

                SaveModulesNowAsync().Forget();
            }),
            TaskScheduler.Default);
    }

    /// <summary>今の条件を書いた所までの印。<see cref="_moduleSaveToken"/> と違えば、まだ書いていない変更がある。</summary>
    private int _moduleSavedToken;

    private Task SaveModulesNowAsync()
    {
        _moduleSavedToken = _moduleSaveToken;
        var states = Modules.Select(module => module.Save() with { Summary = null }).ToList();
        return _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
            state => state with { SearchModules = states }));
    }

    /// <summary>
    /// 待っている条件の保存を今書く。閉じる前に主画面が呼ぶ（2026-09-30。条件を変えて0.5秒以内に閉じると、
    /// 遅らせた保存が走る前にアプリが終わり、その変更だけが次の起動に残らなかった）。画面のスレッドで呼ぶ
    /// </summary>
    public Task FlushModulesAsync()
        => _moduleSavedToken == _moduleSaveToken ? Task.CompletedTask : SaveModulesNowAsync();

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
            ? "設定でR-18 の商品を隠しているため使えません。設定の「R-18 の商品を表示する」をオンにしてください。"
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
            case UserTagModule userTag:
                userTag.SetMasters(_services.Store.UserTags.Load().Tops
                    .Select(top => (top.Name, (IReadOnlyList<string>)top.Subs.Select(sub => sub.Name).ToList())));
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

    /// <summary>
    /// 対応アバターの候補。並びは 持っているアバター → 共通素体 → 持っていないアバター（ユーザ判断 Q1）。
    /// 共通素体は独立の条件にせず、ここに混ぜる（素体に属するアバターなら、衣装は素体の名前で探すことが多い）。
    /// </summary>
    private IEnumerable<(string Text, string Key)> AvatarCandidates()
    {
        _avatarSuggestInfo.Clear();
        var registry = _services.Store.Avatars.Load();
        var names = AvatarNames.Map(registry.Entries);
        var owned = OwnedItemIds();

        var avatars = registry.Entries
            .Where(AvatarService.IsAvatar)
            .Select(entry => (Entry: entry, Name: names.TryGetValue(entry.ItemId, out var name) ? name : entry.ItemId))
            .OrderBy(pair => pair.Name, StringComparer.CurrentCulture)
            .ToList();

        bool IsOwned(AvatarRegistryEntry entry) => entry.IsOwnedManually || owned.Contains(entry.ItemId);

        // 群と呼び方は候補の文字から引く（欄の候補は文字の並びで渡るため）。呼び方でも当たる照らし方は AvatarSearch に1つ
        (string Text, string Key) AvatarRow(AvatarRegistryEntry entry, string name, int group)
        {
            var text = AvatarSuggestionText.Format(name, entry.ItemId);
            _avatarSuggestInfo[text] = AvatarSuggestionText.InfoOf(entry, name, group);
            return (text, AvatarKey + entry.ItemId);
        }

        foreach (var (entry, name) in avatars.Where(pair => IsOwned(pair.Entry)))
        {
            yield return AvatarRow(entry, name, AvatarSuggestionText.OwnedGroup);
        }

        // 削除した素体（印の付いたグループ）は候補に出さない。一覧にも照合にも出さない約束で、
        // アバターの記録に同じ名前が残っていても出さない（点検 2026-09-28：削除した素体が候補に出ていた）
        var deleted = registry.BaseGroups.Where(group => group.Rejected)
            .Select(group => group.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var baseName in registry.BaseGroups.Where(group => !group.Rejected).Select(group => group.Name)
            // アバターでない記録（「アバターとして扱わない」にした物）に残った素体名は拾わない
            .Concat(registry.Entries.Where(AvatarService.IsAvatar).Select(entry => entry.BaseName))
            .Where(name => !string.IsNullOrWhiteSpace(name) && !deleted.Contains(name!))
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture))
        {
            var text = $"{baseName}（共通素体）";
            _avatarSuggestInfo[text] = new Controls.SuggestInfo(AvatarSuggestionText.BaseGroup, []);
            yield return (text, BaseKey + baseName);
        }

        foreach (var (entry, name) in avatars.Where(pair => !IsOwned(pair.Entry)))
        {
            yield return AvatarRow(entry, name, AvatarSuggestionText.OtherGroup);
        }
    }

    private readonly Dictionary<string, Controls.SuggestInfo> _avatarSuggestInfo = new(StringComparer.CurrentCultureIgnoreCase);

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
        // お気に入りのショップ（ショップ画面の星）は、別の条件にせずここに持つ（ユーザ指示 2026-09-16）
        SearchModuleKind.Shop => new ListModule(kind, allowsAnd: false, "ショップ名で絞り込む",
            "ショップの分かる商品がまだありません。",
            (item, _, key, _) => string.Equals(item.ShopSubdomain, key, StringComparison.OrdinalIgnoreCase),
            includeLabel: "お気に入りのショップの商品",
            includeMatches: (item, _) => IsFavoriteShop(item)),

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

            // 支援用のバリエーションや、販売を止めるためのあり得ない高値を外せるようにする（ユーザ判断 2026-09-16）
            SupportsOutliers = true,

            // 払った額は自分で入れた数なので外れ値は無い（ユーザ判断 2026-10-03・メモ16-③）
            NoOutlierSource = PaidSource,
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

        // 説明文から読み取っただけの対応アバター（`H2Link`）は、対応と数えず確認待ちにしてある。
        // 商品ページで確かめる作業へ、まとめて回れるようにする（ユーザ判断 2026-09-18）
        SearchModuleKind.AvatarUnconfirmed => new ChoiceModule(kind,
            [new("unconfirmed", "確かめていない推定がある"), new("none", "確かめていない推定は無い"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "unconfirmed" => HasUnconfirmedAvatars(item),
                "none" => !HasUnconfirmedAvatars(item),
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

        // 壊れた zip は商品ページの札でしか分からず、取り込みの結果の文は数しか言わない。どの商品かをまとめて出せるようにする
        // （ユーザ判断 2026-09-30）。照合は記録だけ（ItemRecord.HasBrokenArchive）
        SearchModuleKind.BrokenZip => new ChoiceModule(kind,
            [new(BrokenZipKey, "壊れたzipがある"), new("none", "壊れたzipは無い"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                BrokenZipKey => item.HasBrokenArchive,
                "none" => !item.HasBrokenArchive,
                _ => true,
            }),

        // 記録の上では持っているが置き場が無いファイル（ユーザ判断 2026-10-04）。カードの印と同じ式（ItemRecord.HasMissingFile）
        SearchModuleKind.MissingFile => new ChoiceModule(kind,
            [new(MissingFileKey, "見つからないファイルがある"), new("none", "見つからないファイルは無い"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                MissingFileKey => item.HasMissingFile,
                "none" => !item.HasMissingFile,
                _ => true,
            }),

        // 仮のIDで登録した商品（BOOTHに無い商品・ローカル登録。ユーザ指示 2026-10-04 メモ31）。カードの「BOOTHで開く」を出さない判定と同じ式（ItemRecord.IsLocalOnly）
        SearchModuleKind.NotOnBooth => new ChoiceModule(kind,
            [new(NotOnBoothKey, "BOOTHに無い商品だけ"), new("booth", "BOOTHの商品だけ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                NotOnBoothKey => item.IsLocalOnly,
                "booth" => !item.IsLocalOnly,
                _ => true,
            }),

        // 要確認に未読の更新がある商品（ユーザ指示 2026-10-02）。カードの札「更新あり」と同じ表（SearchViewModel.Updates）を見る。
        // 既読にすると外れる（表が変わったら絞り直す）
        SearchModuleKind.Updated => new ChoiceModule(kind,
            [new("updated", "更新ありのみ"), new("other", "更新あり以外のみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "updated" => HasUnreadUpdate(item.Id),
                "other" => !HasUnreadUpdate(item.Id),
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

        // 大分類 → 小分類の2段（ユーザ指示 2026-09-28）。照合は Core の UserTagCondition
        SearchModuleKind.UserTag => new UserTagModule(),

        SearchModuleKind.Attribute => new AttributeModule(),

        // 素体経由は推定なので含めるかを選べるようにする。既定で含めるのは「対応が確認できていないものを既定で隠さない」方針
        SearchModuleKind.Avatar => new ListModule(kind, allowsAnd: true, "アバター名・商品ID・共通素体で絞り込む",
            "アバターがまだ見つかっていません。アバターの管理から検出できます。", AvatarMatches,
            "素体経由の対応も含める", flagDefault: true,
            isUnspecified: IsUnspecifiedAvatar)
        {
            IconSelector = AvatarIconSelector,
            InfoSelector = text => _avatarSuggestInfo.GetValueOrDefault(text),
            GroupHeadings = AvatarSuggestionText.Headings,
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

        // 編集状況（前の「未編集」・ユーザ判断 2026-10-01）。既定はユーザータグが0件（前の決め Q6 と同じ）。
        // 取り込みの③がまだの商品は「未入力のみ」に数えない（編集画面に出てこないため・U8・U10）
        SearchModuleKind.Unedited => new UneditedModule(item => _main?.IsAwaitingDetection(item.Id) == true),

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

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる。
        // フォルダは比べる形に1回だけ畳み（matchKey）、照らすときは畳んだ形を受ける
        SearchModuleKind.Path => new ListModule(kind, allowsAnd: true, "フォルダの名前で絞り込む",
            "手元にファイルのある商品がまだありません。",
            (item, context, prefix, _) => FolderTree.IsUnderPrefix(item, prefix, context.PathMap),
            matchKey: FolderTree.UnderPrefix),

        SearchModuleKind.Recent => new RecentModule(),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>条件「壊れたzip」の「ある」の鍵。取り込みの結果から入る口（<see cref="ShowOnlyBrokenZip"/>）と同じ物を指す。</summary>
    private const string BrokenZipKey = "broken";

    /// <summary>条件「見つからないファイル」の「ある」の鍵。</summary>
    private const string MissingFileKey = "missing";

    /// <summary>条件「BOOTHに無い商品」の「BOOTHに無い商品だけ」の鍵。</summary>
    private const string NotOnBoothKey = "local";

    private const string PaidSource = "paid";
    private const string BoothSource = "booth";

    /// <summary>
    /// お気に入りのショップの鍵（shops.json の星）。読み込みのときと、ショップ画面で星を変えたときに入れ直す。
    /// 1商品ごとにファイルを読まないよう、ここに持つ。
    /// </summary>
    private IReadOnlySet<string> _favoriteShops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool IsFavoriteShop(ItemRecord item)
        => item.ShopSubdomain is { } key && _favoriteShops.Contains(key);

    /// <summary>ショップ画面で星やメモを変えたことを知る。ショップの条件を足していれば絞り直す（件数も変わる）。</summary>
    public void NoteShopNotesChanged(IReadOnlyList<ShopNoteRecord> notes)
    {
        _favoriteShops = ShopNotes.FavoriteKeys(notes);
        if (Modules.Any(module => module.Kind == SearchModuleKind.Shop))
        {
            ApplyFilters();
        }
    }

    /// <summary>
    /// 所持＝ファイルかフォルダを1つ以上持つ。外していないファイルがあるかだけを見る（<c>OwnedFiles</c> は呼ぶたびに並びを作るので、
    /// 照らすたびに作ると所持の条件が5倍重かった・案c）。
    /// </summary>
    private static bool IsOwned(ItemRecord item) => item.Local.LocalFolders.Count > 0 || item.Local.LocalFiles.Any(file => !file.Detached);

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

        // 「払った額」の並べ替えと同じ数え方（Purchases.SelfPaidOrNull）
        return Core.Services.Purchases.SelfPaidOrNull(item) is { } paid ? [paid] : [];
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

    /// <summary>
    /// 対応アバターの**指定が無い**商品か（どのアバターにも使える扱いにするかたまり・ユーザ判断 2026-09-16）。
    ///
    /// 出品者の宣言（消していない分）と共通素体の宣言が両方とも無いこと。1つでも書いてあるものは書いてある内容どおりに照らす。
    /// 要確認（説明文のリンク）は宣言に数えていない（絞り込みでも数えない）ので、それだけの商品は「指定が無い」側に入る。
    /// </summary>
    /// <summary>
    /// まだ確かめていない対応アバターがあるか。説明文のその他のリンク（`H2Link`）は対応と数えず、
    /// 商品ページで人が確かめるまで `Confirmed` が立たない
    /// </summary>
    private static bool HasUnconfirmedAvatars(Core.Models.ItemRecord item)
        => item.Local.Avatars.Any(link => !link.Rejected && !link.Confirmed);

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
