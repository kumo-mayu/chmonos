using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：検索の履歴（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 履歴のスロット。新しいものが先。
    ///
    /// 検索欄の上に横に並べる。押すとその条件に戻る。
    /// </summary>
    public ObservableCollection<SearchHistorySlot> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>いまの画面の状態を1件の記録にする。</summary>
    private Core.Models.SearchHistoryEntry CurrentSearch()
    {
        var tags = new List<string>();
        foreach (var tag in TagFilters)
        {
            // 入れ子は「親/子」で持つ。親だけ選んでいる場合は親の名前だけ
            if (tag.HasSelectedSubs)
            {
                tags.AddRange(tag.SelectedSubs.Select(sub => $"{tag.Name}/{sub}"));
            }
            else if (tag.IsSelected)
            {
                tags.Add(tag.Name);
            }
        }

        return new Core.Models.SearchHistoryEntry
        {
            Text = _queryText,
            Category = _selectedCategory == AllCategories ? null : _selectedCategory,
            OwnedOnly = _ownedOnly,
            MissingOnly = _missingOnly,
            GivenOnly = _givenOnly,
            ReceivedOnly = _receivedOnly,
            SearchBody = _searchBody,
            SearchPaths = _searchPaths,
            SearchAlternates = _searchAlternates,
            AvatarName = _avatarFilterName,
            AvatarId = long.TryParse(_avatarFilterId, out var avatarId) ? avatarId : null,
            AvatarHasBase = _avatarFilterHasBase,
            UserTags = tags,
            BoothTags = BoothTagFilters.Select(tag => tag.Name).ToList(),

            // 全開の軸は条件になっていないので持たない
            Attributes = AttributeFilters
                .Where(filter => filter.Min > 0 || filter.Max < 100)
                .Select(filter => new Core.Models.AttributeRange(filter.Name, filter.Min, filter.Max))
                .ToList(),
            Sort = _sort.Label == DefaultSort.Label ? null : _sort.Label,
            UsedAt = DateTimeOffset.Now,
        };
    }

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
        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSearchHistory(stored =>
            new Core.Services.SearchHistoryList { Entries = Core.Services.SearchHistory.Add(stored.Entries, entry, keep) }));

        if (result is Core.Commands.CommandResult.SearchHistoryChanged changed)
        {
            LoadHistory(changed.History.Entries);
        }
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
        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSearchHistory(stored =>
            new Core.Services.SearchHistoryList { Entries = Core.Services.SearchHistory.Remove(stored.Entries, entry.Fingerprint) }));

        if (result is Core.Commands.CommandResult.SearchHistoryChanged changed)
        {
            LoadHistory(changed.History.Entries);
        }
    }

    /// <summary>
    /// 履歴の条件に戻す。
    ///
    /// **まず全部クリアしてから積む。**今の条件の上に重ねると、
    /// 履歴に無い条件が残って「押したのに違う結果」になる。
    /// </summary>
    private void ApplyHistory(Core.Models.SearchHistoryEntry entry)
    {
        ClearFilters();

        _queryText = entry.Text;
        _queryNode = Core.Services.SearchQuery.Parse(entry.Text);
        _selectedCategory = entry.Category ?? AllCategories;
        _ownedOnly = entry.OwnedOnly;
        _missingOnly = entry.MissingOnly;
        _givenOnly = entry.GivenOnly;
        _receivedOnly = entry.ReceivedOnly;
        _searchBody = entry.SearchBody;
        _searchPaths = entry.SearchPaths;
        _searchAlternates = entry.SearchAlternates;

        _avatarFilterName = entry.AvatarName;
        _avatarFilterId = entry.AvatarId?.ToString();
        _avatarFilterHasBase = entry.AvatarHasBase;
        RaiseAvatarFilterChanged();

        foreach (var tag in TagFilters)
        {
            // 親だけの指定と「親/子」の両方を受ける
            if (entry.UserTags.Contains(tag.Name))
            {
                tag.SetSilently(true);
            }

            foreach (var sub in tag.Subs)
            {
                sub.SetSilently(entry.UserTags.Contains($"{tag.Name}/{sub.Name}"));
            }
        }

        foreach (var tag in entry.BoothTags)
        {
            AddBoothTagFilter(tag);
        }

        foreach (var range in entry.Attributes)
        {
            AddAttributeFilter(range.Name);
            if (AttributeFilters.FirstOrDefault(filter =>
                    string.Equals(filter.Name, range.Name, StringComparison.CurrentCultureIgnoreCase)) is { } filter)
            {
                filter.Min = range.Min;
                filter.Max = range.Max;
            }
        }

        if (entry.Sort is not null
            && SortOptions.FirstOrDefault(option => option.Label == entry.Sort) is { } sort)
        {
            _sort = sort;
        }

        foreach (var name in new[]
        {
            nameof(QueryText), nameof(SelectedCategory), nameof(OwnedOnly),
            nameof(GivenOnly), nameof(ReceivedOnly), nameof(SearchBody),
            nameof(SearchPaths), nameof(SearchAlternates), nameof(Sort),
        })
        {
            OnPropertyChanged(name);
        }

        ApplyFilters();
    }
}
