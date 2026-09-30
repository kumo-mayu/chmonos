using System.Collections.ObjectModel;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：検索の履歴</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 履歴のスロット。新しいものが先。
    ///
    /// 検索欄の上に横に並べる。押すとその条件に戻る。
    /// </summary>
    public ObservableCollection<SearchHistorySlot> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>
    /// いまの画面の状態を1件の記録にする。絞り込みは**効いている条件だけ**を持つ（何も絞っていない条件は戻す意味が無い）。
    /// 非表示の条件は「両方」でも持つ——足していないと非表示の商品が隠れるので、「両方」も結果を変える。
    /// </summary>
    private Core.Models.SearchHistoryEntry CurrentSearch() => new()
    {
        Text = _queryText,
        Targets = TargetsForHistory(),
        CaseSensitive = _caseSensitive,
        WidthSensitive = _widthSensitive,
        KanaInsensitive = !_kanaSensitive,
        SearchAlternates = _searchAlternates,
        Modules = Modules
            .Where(module => module.IsActive || (module.Kind == SearchModuleKind.Hidden && module.IsEnabled))
            .Select(module => module.Save())
            .ToList(),
        Sort = _sort.Label == DefaultSort.Label ? null : _sort.Label,
        UsedAt = DateTimeOffset.Now,
    };

    /// <summary>
    /// 履歴を積んで保存する。
    ///
    /// **同じ条件は積まない**（ユーザ指示）。指紋が同じなら上に持ち上げるだけ。
    /// 判断は <see cref="Core.Services.SearchHistory.Add"/> に集めてある。
    /// </summary>
    private async Task RecordHistoryAsync()
    {
        var entry = CurrentSearch();
        if (entry.IsEmpty)
        {
            return;
        }

        // 読んで足して書く間を錠の中で行う（技術的負債 3-1）。前は画面が読んだ写しを丸ごと書いていた
        var keep = _services.Settings.SearchHistoryCount;
        var result = await ChangeHistoryAsync(stored =>
            new Core.Services.SearchHistoryList { Entries = Core.Services.SearchHistory.Add(stored.Entries, entry, keep) });

        if (result is Core.Commands.CommandResult.SearchHistoryChanged changed)
        {
            LoadHistory(changed.History.Entries);
        }
    }

    /// <summary>
    /// 検索の履歴を書き換える。道は今までどおり <see cref="Core.Commands.UiCommand.ChangeSearchHistory"/>（錠の中で今の履歴に当てる）で、
    /// **走らせる場所だけを画面のスレッドの外にする**（<see cref="Core.Storage.BackgroundWriteQueue"/>）。
    ///
    /// 履歴は商品を開くたびに書く。画面のスレッドから直に呼ぶと、読む・ディスクへ書き出す・置き換えるがそこで走り、
    /// 開く1回ごとに乗っていた。足すのも消すのも同じ列を通す——別の道で書くと、押した順と書く順が入れ替わり得る。
    /// </summary>
    private Task<Core.Commands.CommandResult> ChangeHistoryAsync(
        Func<Core.Services.SearchHistoryList, Core.Services.SearchHistoryList> change)
    {
        var commands = _services.Commands;
        return _services.BackgroundWrites.RunAsync(
            () => commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSearchHistory(change)));
    }

    /// <summary>スロットを組み直す。</summary>
    private void LoadHistory(IReadOnlyList<Core.Models.SearchHistoryEntry> entries)
    {
        History.Clear();
        foreach (var entry in entries)
        {
            History.Add(new SearchHistorySlot(entry, ApplyHistory, RemoveHistoryAsync));
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>保存済みの履歴を読んでスロットに出す。画面を開くときに1回。</summary>
    public void RestoreHistory()
        => LoadHistory(_services.Store.SearchHistory.Load().Entries);

    private async Task RemoveHistoryAsync(Core.Models.SearchHistoryEntry entry)
    {
        var result = await ChangeHistoryAsync(stored =>
            new Core.Services.SearchHistoryList { Entries = Core.Services.SearchHistory.Remove(stored.Entries, entry.Fingerprint) });

        if (result is Core.Commands.CommandResult.SearchHistoryChanged changed)
        {
            LoadHistory(changed.History.Entries);
        }
    }

    /// <summary>
    /// 今の絞り込みを控える（ユーザ判断 2026-09-21・P4）。
    ///
    /// 画面の履歴は「開き直す手順」だけを預かるが、**検索は1つを持ち回すので、
    /// 控えに条件が入っていないと戻っても条件が戻らない**。
    /// 「この商品だけ出す」などの入口は条件を全消ししてから絞るので、前の条件は戻す手段ごと失われていた。
    /// 形は検索の履歴と同じ物を使う（同じ「条件一式」なので、2つ持つ理由が無い）。
    /// </summary>
    public Core.Models.SearchHistoryEntry CaptureFilters() => CurrentSearch();

    /// <summary>控えた絞り込みに戻す（P4）。</summary>
    public void RestoreFilters(Core.Models.SearchHistoryEntry entry) => ApplyHistory(entry);

    /// <summary>
    /// 履歴の条件に戻す。
    ///
    /// **まず全部の値を戻してから当てる。**今の条件の上に重ねると、
    /// 履歴に無い条件が残って「押したのに違う結果」になる。履歴にある条件がパネルに無ければ足す。
    /// </summary>
    private void ApplyHistory(Core.Models.SearchHistoryEntry entry)
    {
        ClearFilters(apply: false);

        _queryText = entry.Text;
        _queryNode = Core.Services.SearchQuery.Parse(entry.Text);
        RestoreTextOptions(entry.Targets, entry.CaseSensitive, entry.WidthSensitive, !entry.KanaInsensitive);
        _searchAlternates = entry.SearchAlternates;

        foreach (var state in entry.Modules)
        {
            if (Enum.TryParse<SearchModuleKind>(state.Kind, out var kind))
            {
                AddModule(kind, apply: false).Load(state);
            }
        }

        // 履歴に残るのは「入手日が新しい順」のような1つの言い方。項目と向きに分けた今も、
        // その言い方から戻せるように、項目ごとの言い方と突き合わせる（M5）
        if (entry.Sort is not null)
        {
            foreach (var field in SortFields)
            {
                foreach (var descending in new[] { true, false })
                {
                    if (field.FullLabel(descending) == entry.Sort)
                    {
                        _sortField = field;
                        _sort = field.ToOption(descending);
                    }
                }
            }
        }

        foreach (var name in new[] { nameof(QueryText), nameof(SearchAlternates), nameof(Sort), nameof(SortField), nameof(SortsDescending), nameof(SortsAscending), nameof(AscendingLabel), nameof(DescendingLabel) })
        {
            OnPropertyChanged(name);
        }

        RefreshModuleMenu();
        SaveModulesLater();
        ApplyFilters();
    }
}
