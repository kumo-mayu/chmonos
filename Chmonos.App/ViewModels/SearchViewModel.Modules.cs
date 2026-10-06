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
        Clock());

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
                attribute.RefreshHistograms();
                break;
            case ModificationModule modification:
                SetModificationSources(modification);
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
            case RecentModule recent:
                recent.SetRecords(_services.Recent.AllTimes(), _allItems, Clock());
                recent.IsSortedByThis = IsSortedBy(recent.SelectedKind);
                break;
        }
    }

    /// <summary>
    /// 今の時刻。「最近」の日数（帯と照合）はこれで数える。試験は日をまたいでも同じ答えになるよう差し替える
    /// （時計に左右される試験を書かない決め事・`docs/dev/app-tests.md`）。
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>
    /// 「最近」の帯と「新しい順に並べる」の押せるかを、今の足跡と表示順に合わせる。絞り直しのたびに呼ぶ
    /// （商品ページを開くと足跡が増え、表示順は欄からも変わる）。足跡が前と同じ入れ物で同じ日なら、帯は数え直さない。
    /// </summary>
    private void RefreshRecentModules()
    {
        foreach (var recent in Modules.OfType<RecentModule>())
        {
            recent.SetRecords(_recentTimes ?? _services.Recent.AllTimes(), _allItems, _moduleContext?.Now ?? Clock());
            recent.IsSortedByThis = IsSortedBy(recent.SelectedKind);
        }
    }

    private bool IsSortedBy(Core.Services.RecentKind kind) => _sort.Descending && RecentKindOf(_sort.Kind) == kind;

    /// <summary>
    /// 「最近」の条件の「新しい順に並べる」（ユーザ判断 2026-10-06）。表示順をその記録の項目・新しい順にする。絞り込みはそのまま
    /// （取り込みの結果から来る <see cref="ShowRecentlyAddedFirst"/> は値を戻すが、こちらは絞った上での順を見たい）。
    /// </summary>
    internal void SortByRecent(Core.Services.RecentKind kind)
    {
        var kindOfSort = kind switch
        {
            Core.Services.RecentKind.Added => SortKind.RecentlyAdded,
            Core.Services.RecentKind.Viewed => SortKind.RecentlyViewed,
            _ => SortKind.RecentlyUsed,
        };

        if (SortFields.FirstOrDefault(field => field.Kind == kindOfSort) is not { } field)
        {
            return;
        }

        _sortField = field;
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(AscendingLabel));
        OnPropertyChanged(nameof(DescendingLabel));
        Sort = field.ToOption(descending: true);
        OnPropertyChanged(nameof(SortsDescending));
        OnPropertyChanged(nameof(SortsAscending));
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
        SearchModuleKind.UnityProject => UnityProjectCandidates(),
        SearchModuleKind.Path => FolderTree.AllFolders(_allItems, _services.Volumes.Current).Select(path => (path, path)),
        _ => [],
    };

    /// <summary>
    /// ショップは名前が変わりうるので鍵はサブドメイン。候補の文字にも入れておく（同じ名前のショップを見分ける）。
    /// お気に入りのショップ（ショップ画面の星）を先に、区切り線の後にそれ以外（ユーザ判断 2026-10-06・メモ82）。
    /// 区切りは候補の欄の群で引く（見出しは出さない。星の付いた物が先に並ぶのは見れば分かる）
    /// </summary>
    private IEnumerable<(string Text, string Key)> ShopCandidates()
    {
        _shopSuggestInfo.Clear();
        var shops = _allItems
            .Where(item => item.ShopSubdomain is not null)
            .GroupBy(item => item.ShopSubdomain!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Select(item => item.ShopName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key, group.Key))
            .OrderBy(pair => _favoriteShops.Contains(pair.Key) ? 0 : 1)
            .ThenBy(pair => pair.Name, StringComparer.CurrentCulture)
            .ToList();

        foreach (var (name, key) in shops)
        {
            var text = $"{name}（{key}）";
            _shopSuggestInfo[text] = new Controls.SuggestInfo(_favoriteShops.Contains(key) ? 0 : 1, []);
            yield return (text, key);
        }
    }

    private readonly Dictionary<string, Controls.SuggestInfo> _shopSuggestInfo = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>ショップの条件の「お気に入りも出す」チェックの文。</summary>
    internal const string FavoriteShopsIncludeLabel = "お気に入りのショップはすべて表示";

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

    /// <summary>
    /// 条件「改変」に改変の一覧を入れる（2段の形・ユーザ判断 2026-10-06）。アバターの名前は登録簿の名前、呼び方・正式名でも当たる
    /// （前の候補と同じ <see cref="AvatarSearch"/> の語）。改変を読む前は何もしない（読み終えたらまた呼ばれる）
    /// </summary>
    private void SetModificationSources(ModificationModule module)
    {
        EnsureModificationsLoaded();
        if (_modificationUsage is not { } usage)
        {
            return;
        }

        var entries = _services.Store.Avatars.Load().Entries;
        var names = AvatarNames.Map(entries);
        var entryById = entries.GroupBy(entry => entry.ItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        string AvatarName(string id) => names.TryGetValue(id, out var name) ? name : id;

        module.SetSources(
            usage.Records,
            AvatarName,
            id => entryById.TryGetValue(id, out var entry)
                ? AvatarSearch.Hints(entry, AvatarName(id)).Select(hint => hint.Text).ToList()
                : []);
    }

    /// <summary>改変の絵（改変の写真の1枚目 → アバターの絵。改変の一覧・改変を選ぶ窓と同じ決め方）。</summary>
    private System.Windows.Media.ImageSource? ModificationIconOf(ModificationRecord record)
        => Core.Services.ModificationIcon.PathOf(_services.Paths, record, FindItem(record.AvatarItemId)) is { } path
            ? _thumbnails.LoadForTile(path)
            : null;

    /// <summary>アバターの絵（持っていれば商品の1枚目、持っていなければ控えの1枚。対応アバターの候補と同じ）。</summary>
    private System.Windows.Media.ImageSource? AvatarIconOf(string avatarItemId)
        => Core.Services.AvatarImageSync.IconPath(_services.Paths, avatarItemId, FindItem(avatarItemId)) is { } path
            ? _thumbnails.LoadForTile(path)
            : null;

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

    private SearchModule CreateModule(SearchModuleKind kind) => kind switch
    {
        // カテゴリと BOOTHタグは OR だけ（ユーザ判断 2026-10-06・メモ82・メモ84「AND できる必要が無い」）。
        // 「A と B の両方」が要るときは、同じ種類の条件をもう1つ置けば AND になる（条件どうしは AND）
        SearchModuleKind.Category => new ListModule(kind, allowsAnd: false, "カテゴリで絞り込む",
            "カテゴリがまだありません。商品を取り込むと付いてきます。",
            (item, _, key, _) => string.Equals(item.CategoryName, key, StringComparison.CurrentCulture)
                || string.Equals(item.Booth.Category?.ParentName, key, StringComparison.CurrentCulture)),

        SearchModuleKind.BoothTag => new ListModule(kind, allowsAnd: false, "BOOTHタグで絞り込む",
            "BOOTHタグがまだありません。商品を取り込むと付いてきます。",
            (item, _, key, _) => item.Booth.Tags.Any(tag => string.Equals(tag, key, StringComparison.CurrentCultureIgnoreCase))),

        // 1商品に1つなので「すべて（AND）」は意味が無い
        // お気に入りのショップ（ショップ画面の星）は、別の条件にせずここに持つ（ユーザ指示 2026-09-16）。
        // 文は「選んだショップに加えて、お気に入りをすべて出す」と読めるように（メモ82：「お気に入りのショップの商品」は、お気に入りだけに絞るように読めた）。
        // 候補はお気に入りのショップを先に、区切り線の後にそれ以外（メモ82）
        SearchModuleKind.Shop => new ListModule(kind, allowsAnd: false, "ショップ名で絞り込む",
            "ショップの分かる商品がまだありません。",
            (item, _, key, _) => string.Equals(item.ShopSubdomain, key, StringComparison.OrdinalIgnoreCase),
            includeLabel: FavoriteShopsIncludeLabel,
            includeMatches: (item, _) => IsFavoriteShop(item))
        {
            InfoSelector = text => _shopSuggestInfo.GetValueOrDefault(text),
        },

        SearchModuleKind.WishList => new RangeModule(kind, (item, _) => [item.Booth.WishListsCount], string.Empty)
        {
            AllValuesOf = _ => _allItems.Select(item => (long)item.Booth.WishListsCount),
        },

        SearchModuleKind.Price => new RangeModule(kind, PriceValues, "円",
            [new ChoiceOption(PaidSource, "払った額"), new ChoiceOption(BoothSource, "BOOTH価格")])
        {
            AllValuesOf = source => _allItems.SelectMany(item => PriceValues(item, source)),

            // BOOTH の有料販売は100円から。1〜99円はあり得ないので、目盛を取らせない（ユーザ指摘 2026-09-16）
            Floor = 100,

            // 支援用のバリエーションや、販売を止めるためのあり得ない高値を外せるようにする（ユーザ判断 2026-09-16）
            SupportsOutliers = true,

            // 払った額は自分で入れた数なので外れ値は無い（ユーザ判断 2026-10-03・メモ16-③）
            NoOutlierSource = PaidSource,

            // 種類ごとの価格が全部範囲に入る商品だけを探せるようにする（ユーザ判断 2026-10-06・メモ82-6）
            SupportsMatchAll = true,

            // 価格の分からない商品も足せるようにする（ユーザ判断 2026-10-06。既定は切・有料・無料のチェックとは同期しない）
            SupportsUnpriced = true,
            UnpricedLabelOf = source => source == BoothSource ? BoothUnpricedLabel : UnpricedLabel,
        },

        // ユーザ判断 2026-10-06（メモ84・案1）：使う人には非公開も削除も同じなので、販売終了と1つにまとめ、前の切り替え
        // 「非公開・削除された商品も表示する」は外した。仮IDの商品（BOOTHに無い商品）は「両方」のときだけ出す
        SearchModuleKind.EndOfSale => new ChoiceModule(kind,
            [new("ended", "販売終了・非公開"), new("selling", "公開中"), new("both", "両方")],
            "both", (item, key, _) => EndOfSaleMatches(item, key)),

        SearchModuleKind.PublishedAt => new DateModule(kind,
            item => item.Booth.PublishedAt is { } at ? DateOnly.FromDateTime(at.LocalDateTime) : null)
        {
            AllDatesOf = _ => _allItems
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

        // 「すべて見つからない商品は未所持とする」（ユーザ判断 2026-10-06・案A。既定は切＝所持の定義のまま。文はユーザ判断 2026-10-06 で短くした）：
        // 記録のファイル・フォルダが全部見つからない商品は手元に何も無いので、使う人には未所持と同じ。入れると「所持のみ」から外し「未所持のみ」に入れる。
        // 見つからないファイルの条件の「未所持も含める」は別の問い（見つからない商品の中で分ける）なので、そのまま残す
        SearchModuleKind.Owned => new ChoiceModule(kind,
            [new("owned", "所持のみ"), new("unowned", "未所持のみ"), new("both", "両方")],
            "both", (item, key, allMissingIsUnowned) => key switch
            {
                "owned" => IsOwned(item) && !(allMissingIsUnowned && item.HasAllFilesMissing),
                "unowned" => !IsOwned(item) || (allMissingIsUnowned && item.HasAllFilesMissing),
                _ => true,
            },
            new ChoiceFlag("すべて見つからない商品は未所持とする", Default: false, "すべて見つからない商品は未所持", "allMissingIsUnowned",
                new HashSet<string>(StringComparer.Ordinal) { "owned", "unowned" })),

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
        // 「未所持も含める」（ユーザ判断 2026-10-06・メモ83・判断7。既定は含める＝前と同じ）：記録のファイル・フォルダが**全部**見つからない商品は、
        // 手元には何も無い＝実際には未所持。切ると、一部だけ見つからない商品に絞れる（カードの札「一部見つからない」と同じ分け方・HasAllFilesMissing）
        SearchModuleKind.MissingFile => new ChoiceModule(kind,
            [new(MissingFileKey, "見つからないファイルがある"), new("none", "見つからないファイルは無い"), new("both", "両方")],
            "both", (item, key, includeAllMissing) => key switch
            {
                MissingFileKey => item.HasMissingFile && (includeAllMissing || !item.HasAllFilesMissing),
                "none" => !item.HasMissingFile,
                _ => true,
            },
            new ChoiceFlag("未所持も含める", Default: true, "すべて見つからない商品を除く", "includeUnowned",
                new HashSet<string>(StringComparer.Ordinal) { MissingFileKey })),

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
        // 既読にすると外れる（表が変わったら絞り直す）。変わった所の種類で絞れる（ユーザ判断 2026-10-06・更新のモジュールの判断）
        SearchModuleKind.Updated => new UpdateNoticeModule(UnreadUpdateKinds),

        // 純三項：「何も絞らない」選択肢を持たない。切るときは条件の切り替えで。
        // 3つで全部の購入記録を覆うので「両方」は持たない（全部選ぶのは外すのと同じ。メモ83）。貰って自分でも買った物は両方に出る。
        // 購入記録は編集画面で入れたときだけできるので、記録の無い商品がいちばん多い。どの選択肢でも
        // 「購入記録の無い商品も含める」で足せる（ユーザ判断 2026-10-06・判断1：既定は切・どの項目でも出す）
        SearchModuleKind.Gift => new ChoiceModule(kind,
            [new("given", "ギフトした"), new("received", "ギフトされた"), new("bought", "購入した")],
            null, (item, key, includeUnrecorded) => (includeUnrecorded && item.Local.Purchases.Count == 0) || key switch
            {
                "given" => Purchases.WasGiven(item),
                "received" => Purchases.WasReceived(item),
                _ => item.Local.Purchases.Any(purchase => purchase.Kind == PurchaseKind.ForSelf),
            },
            new ChoiceFlag("購入記録の無い商品も含める", Default: false, "購入記録の無い商品も含める", "includeUnrecorded")),

        // BOOTH のバリエーションの価格だけで分ける（ユーザ判断 2026-10-06・判断2）。使う人が知りたいのは「全部無料で使えるか・支援版があるか」。
        // 前は払った額を優先し、無料と有料の両方がある商品が両方に出ていた。
        // 絞らない選択肢を最後に持つ（ユーザ判断 2026-10-06：真ん中の「無料版と有料版がある」が両方に読めたので、絞らない物は別に最後に置く）。
        // 名前は「すべて」（ユーザ判断 2026-10-06。前は「両方」。選択肢が3つあり、両方では数が合わない）。
        // 価格の無い商品は既定でどれにも入らず、「非公開商品も含む」で足せる（ユーザ判断 2026-10-06「そもそも未設定がないのですね。
        // そうであれば非公開商品も含むという項目にすべきです」）。BOOTH の価格が無いのは、BOOTH の情報を一度も取れていない商品＝IDのまま登録した非公開の商品だけ
        // （一度取れた後に非公開になった商品は、最後に取れた価格で分かれる）。仮IDの商品（BOOTHに無い商品）は BOOTH の物ではなく有料・無料も無いので入れない
        // （販売終了の条件が仮IDの商品を「すべて」のときだけ出すのと同じ）。
        // 既定は切：「すべて無料」に価格の分からない商品が混ざると、無料で使えると読めてしまう（ギフトの「購入記録の無い商品も含める」と同じ形）
        SearchModuleKind.FreePaid => new ChoiceModule(kind,
            [new(AllFreeKey, "すべて無料"), new(FreeAndPaidKey, "無料版と有料版がある"), new(PaidOnlyKey, "有料のみ"), new("both", "すべて")],
            "both", (item, key, includePrivate) => (includePrivate && IsPrivateUnpriced(item)) || FreePaidMatches(item, key),
            new ChoiceFlag(PrivateItemsLabel, Default: false, PrivateItemsLabel, "includePrivate",
                new HashSet<string>(StringComparer.Ordinal) { AllFreeKey, FreeAndPaidKey, PaidOnlyKey })),

        // 大分類 → 小分類の2段（ユーザ指示 2026-09-28）。照合は Core の UserTagCondition
        SearchModuleKind.UserTag => new UserTagModule(),

        // 行ごとの分布の帯（メモ82）。照合（AttributeFilter.Matches）と同じ引き方で値を集める
        SearchModuleKind.Attribute => new AttributeModule { AllValuesOf = name => RatedValues(_allItems, name) },

        // 素体経由は推定なので含めるかを選べるようにする。既定で含めるのは「対応が確認できていないものを既定で隠さない」方針（メモ82 で確かめた）
        SearchModuleKind.Avatar => new ListModule(kind, allowsAnd: true, "名前、ID、素体で絞り込む",
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

        // 既定は最初の購入（並べ替え・区切りの札と同じ代表の日付）だけで見る。「すべての購入」にすると、購入記録ごとの日付のどれか1つが範囲に入れば当たる
        // （メモ45・2-A。2025年3月に別の種類を買い足した商品が「2025年3月」で出る。ユーザ判断 2026-10-06・判断8）。
        // ファイルの日付では代えない（並べ替えと同じく手で入れた値だけ）
        SearchModuleKind.AcquiredAt => new DateModule(kind, PurchaseDates.AnyEntered, PurchaseDates.EnteredEarliest)
        {
            AllDatesOf = firstOnly => firstOnly
                ? _allItems.Select(PurchaseDates.EnteredEarliest).OfType<DateOnly>()
                : _allItems.SelectMany(PurchaseDates.AllEntered),
        },

        SearchModuleKind.Hidden => new ChoiceModule(kind,
            [new("hidden", "非表示のみ"), new("visible", "表示している商品のみ"), new("both", "両方")],
            "both", (item, key, _) => key switch
            {
                "hidden" => item.Local.IsHidden,
                "visible" => !item.Local.IsHidden,
                _ => true,
            }),

        // 編集状況（前の「未編集」・ユーザ判断 2026-10-01）。既定はユーザータグが0件（前の決め Q6 と同じ）。
        // 取り込みの③がまだの商品は「未入力のみ」に数えない（編集画面に出てこないため・U8・U10）
        SearchModuleKind.Unedited => new UneditedModule(item => _main?.IsAwaitingDetection(item.Id) == true),

        // アバター → 改変の2段（ユーザ判断 2026-10-06。ユーザータグと同じ形）
        SearchModuleKind.Modification => new ModificationModule(AvatarIconOf, ModificationIconOf),

        // 改変を通してそのプロジェクトに紐付いた商品（ユーザ判断 Q7）。プロジェクトの中身は見ない（開くたびに照らすと重い）
        SearchModuleKind.UnityProject => new ListModule(kind, allowsAnd: true, "プロジェクトの名前で絞り込む",
            "Unityプロジェクトを紐付けた改変がまだありません。",
            (item, context, key, _) => context.Modifications.InProject(key, item.Id))
        {
            TrimsMiddle = true,
        },

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる。
        // フォルダは比べる形に1回だけ畳み（matchKey）、照らすときは畳んだ形を受ける
        SearchModuleKind.Path => new ListModule(kind, allowsAnd: true, "フォルダの名前で絞り込む",
            "手元にファイルのある商品がまだありません。",
            (item, context, prefix, _) => FolderTree.IsUnderPrefix(item, prefix, context.PathMap),
            matchKey: FolderTree.UnderPrefix)
        {
            TrimsMiddle = true,
        },

        SearchModuleKind.Recent => new RecentModule { SortRequested = SortByRecent },

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// その属性を評価した商品の値（属性の条件の分布の帯）。照合（<see cref="AttributeFilter.Matches"/>）と同じ引き方。
    /// 数を箱に入れずに数える（数千件で1回 240KB を割り当てていた。行を足すたび・読み込みのたびに走る）
    /// </summary>
    private static IEnumerable<int> RatedValues(IEnumerable<ItemRecord> items, string name)
    {
        foreach (var item in items)
        {
            if (item.Local.Attributes.TryGetValue(name, out var value))
            {
                yield return value;
            }
        }
    }

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
            // 候補の並び（お気に入りが先）も変わる
            foreach (var module in Modules.Where(module => module.Kind == SearchModuleKind.Shop).ToList())
            {
                RefreshModuleSource(module);
            }

            ApplyFilters();
        }
    }

    /// <summary>
    /// 所持＝ファイルかフォルダを1つ以上持つ。外していないファイルがあるかだけを見る（<c>OwnedFiles</c> は呼ぶたびに並びを作るので、
    /// 照らすたびに作ると所持の条件が5倍重かった・案c）。
    /// </summary>
    private static bool IsOwned(ItemRecord item) => item.IsOwned;

    /// <summary>
    /// 価格の条件で照らす数。既定は自分が払った額（ユーザ判断 Q2）。BOOTH の価格は種類ごとにあり、どれか1つでも範囲に入れば当たり。
    /// 払った額を入れていない商品は「0円」ではなく「分からない」ので、範囲のどこにも入らない。
    /// </summary>
    private static IReadOnlyList<long> PriceValues(ItemRecord item, string? source)
    {
        if (source == BoothSource)
        {
            return item.Booth.Variations.Select(variation => (long)variation.Price).ToList();
        }

        // 「払った額」の並べ替えと同じ数え方（Purchases.SelfPaidOrNull）
        return Core.Services.Purchases.SelfPaidOrNull(item) is { } paid ? [paid] : [];
    }

    /// <summary>
    /// 価格の条件の、値の無い商品も足す切り替えの文（ユーザ判断 2026-10-06）。有料・無料の「非公開商品も含む」とは同期しない。
    /// 元で出し分ける（ユーザ判断 2026-10-06）：払った額の間は値段を入れていない商品、BOOTHの価格の間はバリエーションの無い商品を足すので
    /// </summary>
    public const string UnpricedLabel = "払った額が未設定の商品も表示";

    /// <summary>価格の条件を「BOOTH価格」にした間の、同じ切り替えの文。</summary>
    public const string BoothUnpricedLabel = "BOOTH価格が未設定の商品も表示";

    /// <summary>有料・無料の、BOOTH の価格が無い商品も足す切り替えの文（ユーザ判断 2026-10-06）。</summary>
    public const string PrivateItemsLabel = "非公開商品も含む";

    /// <summary>BOOTH の価格が無い本物のIDの商品（IDのまま登録した非公開の商品）。仮IDの商品は有料・無料の外なので入れない。</summary>
    private static bool IsPrivateUnpriced(ItemRecord item) => !item.IsLocalOnly && item.Booth.Variations.Count == 0;

    private const string AllFreeKey = "free";
    private const string FreeAndPaidKey = "mixed";
    private const string PaidOnlyKey = "paid";

    /// <summary>
    /// 有料・無料（ユーザ判断 2026-10-06・判断2）。BOOTH のバリエーションの価格だけで、すべて0円・0円と有料の両方・すべて有料に分ける。
    /// 払った額は見ない（支援版を買っても、無料版があるかは変わらない）。バリエーションの無い商品（BOOTHに無い商品・取れていない商品）はどれにも入らない。
    /// </summary>
    internal static bool FreePaidMatches(ItemRecord item, string key)
    {
        // 「両方」は絞らない（価格の分からない商品も含めて全部。件数もここから数える）
        if (key == "both")
        {
            return true;
        }

        var variations = item.Booth.Variations;
        if (variations.Count == 0)
        {
            return false;
        }

        var hasFree = variations.Any(variation => variation.Price == 0);
        var hasPaid = variations.Any(variation => variation.Price > 0);
        return key switch
        {
            AllFreeKey => hasFree && !hasPaid,
            FreeAndPaidKey => hasFree && hasPaid,
            PaidOnlyKey => hasPaid && !hasFree,
            _ => false,
        };
    }

    /// <summary>
    /// 販売終了（ユーザ判断 2026-10-06・メモ84・案1）。**本物のIDの商品だけに効かせる**：
    /// 「販売終了・非公開」＝ページはあるが販売終了・一度取れた後に見つからないのが続いた（非公開と確定した）・IDのまま登録した物（登録のときに非公開の印を付ける）。
    /// 「公開中」＝それ以外。見つからない回数が非公開と確定する回数に届く前（確かめ中）は、まだ印が付いていないので公開中（判断4）。
    /// 仮IDの商品は BOOTH の状態を持たないので、「両方」のときだけ出す。売り切れは含めない（在庫の話で、販売が終わったわけではない）。
    /// </summary>
    internal static bool EndOfSaleMatches(ItemRecord item, string key)
    {
        if (key == "both")
        {
            return true;
        }

        if (item.IsLocalOnly)
        {
            return false;
        }

        var ended = item.Booth.IsEndOfSale || item.Local.IsDelisted;
        return key == "ended" ? ended : !ended;
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
