using System.Collections.ObjectModel;
using System.Windows;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索画面：保存した検索（ユーザ判断 2026-10-03・10-04 案A3）。
///
/// 今の検索の打った文字・条件・表示順・カードかリストかを名前を付けて残し、呼び出すと今の検索を**置き換える**。
/// 置き場は絞り込み欄のいちばん上の畳める節。並びは人が決め、履歴のように古い物から押し出さない。
/// 書き込みは全部 <see cref="Core.Commands.UiCommand.ChangeSavedSearches"/>（変え方を関数で渡し、錠の中で今の並びに当てる）
/// </summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 探す欄を出し、節の中だけを流し始める件数。5件までは全部が見え、6件目から節の高さを5件ぶんで止める
    /// （ユーザ判断 2026-10-04「多いときは探す欄を出し、5件ほどの高さで節の中だけを流す」）
    /// </summary>
    public const int SavedSearchVisibleRows = 5;

    /// <summary>名前の欄に入れて始める要約の長さ。条件が多いと要約は数百字になり、欄の頭しか見えない名前になる</summary>
    internal const int SavedNamePrefillLength = 60;

    private IReadOnlyList<SearchHistoryEntry> _saved = [];
    private string _savedFilter = string.Empty;
    private bool _isSavedSectionCollapsed;
    private string? _currentSavedName;

    /// <summary>節に並べる行（探す欄で絞った後）。</summary>
    public ObservableCollection<SavedSearchRow> SavedRows { get; } = [];

    public int SavedSearchCount => _saved.Count;

    public bool HasSavedSearches => _saved.Count > 0;

    public bool ShowsSavedFilter => _saved.Count > SavedSearchVisibleRows;

    /// <summary>探す欄の文字。名前と要約のどちらかに含む行だけを出す（節の中だけを絞る。検索の結果は変えない）</summary>
    public string SavedFilter
    {
        get => _savedFilter;
        set
        {
            if (SetField(ref _savedFilter, value ?? string.Empty))
            {
                RebuildSavedRows();
            }
        }
    }

    /// <summary>探して1件も当たらないとき（並べる物はあるのに行が空）。</summary>
    public bool ShowsSavedFilterEmpty => HasSavedSearches && SavedRows.Count == 0;

    public bool IsSavedSectionCollapsed
    {
        get => _isSavedSectionCollapsed;
        private set
        {
            if (SetField(ref _isSavedSectionCollapsed, value))
            {
                OnPropertyChanged(nameof(IsSavedSectionExpanded));
                OnPropertyChanged(nameof(ShowsCollapsedCurrent));
            }
        }
    }

    public bool IsSavedSectionExpanded => !_isSavedSectionCollapsed;

    /// <summary>畳んだ節の見出しに、今の検索と同じ保存した検索の名前を出すか（畳むと行の地の色が見えないため）。</summary>
    public bool ShowsCollapsedCurrent => _isSavedSectionCollapsed && _currentSavedName is not null;

    /// <summary>今の検索と同じ保存した検索の名前。無ければ null。畳んだ節の見出しにも出す</summary>
    public string? CurrentSavedName => _currentSavedName;

    /// <summary>畳んだ節の見出しの右に出す件数（開いているときは行が見えるので出さない）。</summary>
    public string SavedCountText => _saved.Count == 0 ? string.Empty : _saved.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public RelayCommand ToggleSavedSectionCommand => _toggleSaved ??= new RelayCommand(ToggleSavedSection);

    public RelayCommand SaveCurrentSearchCommand => _saveCurrent ??= new RelayCommand(AskSaveCurrent);

    private RelayCommand? _toggleSaved;
    private RelayCommand? _saveCurrent;

    /// <summary>保存した検索を読んで節に出す。画面を開くときに1回（読むだけなので命令を通さない）。</summary>
    public void RestoreSavedSearches()
    {
        _isSavedSectionCollapsed = _services.UiState.SavedSearchesCollapsed;
        OnPropertyChanged(nameof(IsSavedSectionCollapsed));
        OnPropertyChanged(nameof(IsSavedSectionExpanded));
        OnPropertyChanged(nameof(ShowsCollapsedCurrent));
        LoadSaved(_services.Store.SavedSearches.Load().Entries);
    }

    private void ToggleSavedSection()
    {
        IsSavedSectionCollapsed = !IsSavedSectionCollapsed;
        var collapsed = IsSavedSectionCollapsed;
        _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(
            state => state with { SavedSearchesCollapsed = collapsed })).Forget();
    }

    /// <summary>今の検索を、保存した検索の1件の形にする（名前は呼ぶ側が付ける）。</summary>
    private SearchHistoryEntry CurrentSaved(string? name) => CurrentSearch() with
    {
        Name = name,
        View = _isListMode ? ResultView.List : ResultView.Card,
    };

    /// <summary>
    /// 行に出す要約。条件の要約（履歴と同じ文）に、表示順（既定でなければ）とリストかを足す——
    /// どちらも呼び出せば変わる物なので、押す前に分かるようにする
    /// </summary>
    internal static string SavedSummary(SearchHistoryEntry entry)
    {
        var parts = new List<string> { (entry with { Name = null }).Summary };
        if (entry.Sort is { } sort)
        {
            parts.Add(sort);
        }

        if (entry.View == ResultView.List)
        {
            parts.Add("リスト");
        }

        return string.Join(" / ", parts);
    }

    /// <summary>
    /// 保存の小窓の名前の欄に入れて始める文（今の条件の要約）。何も絞っていなければ空——「条件なし」を名前にしても、どれか分からない
    /// </summary>
    internal string SavedNamePrefill()
    {
        var entry = CurrentSearch();
        if (entry.IsEmpty)
        {
            return string.Empty;
        }

        var summary = (entry with { Name = null }).Summary;
        return summary.Length <= SavedNamePrefillLength ? summary : summary[..SavedNamePrefillLength].TrimEnd();
    }

    private void AskSaveCurrent()
    {
        var model = new SavedSearchNameDialogViewModel(
            SavedSearchNameDialogViewModel.Purpose.Save, SavedNamePrefill(), _saved.Select(entry => entry.Name ?? string.Empty).ToList());
        if (new Views.SavedSearchNameDialog(model).ShowDialog() == true)
        {
            SaveCurrentSearchAsync(model.Name).Forget();
        }
    }

    /// <summary>今の検索を名前を付けて末尾に足す。</summary>
    public async Task SaveCurrentSearchAsync(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var entry = CurrentSaved(trimmed) with { UsedAt = DateTimeOffset.Now };
        await ChangeSavedAsync(list => SavedSearches.Add(list, entry));

        // 節を畳んでいると、保存できたかが見えない。保存した行が見えるように開く
        if (IsSavedSectionCollapsed)
        {
            ToggleSavedSection();
        }
    }

    /// <summary>
    /// 保存した検索を今の検索で上書きする。前の条件は残らないので、押す前に確かめる（取り返しがつかない操作は既定をキャンセル側に倒す・D4）
    /// </summary>
    public async Task OverwriteSavedAsync(string name)
    {
        var answer = Services.Notice.Show(
            $"「{name}」を今の検索で上書きします。\n上書きすると、前の内容には戻せません。",
            "保存した検索を上書き",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var entry = CurrentSaved(name) with { UsedAt = DateTimeOffset.Now };
        await ChangeSavedAsync(list => SavedSearches.Overwrite(list, name, entry), focus: name);
    }

    private void AskRenameSaved(string name)
    {
        var model = new SavedSearchNameDialogViewModel(
            SavedSearchNameDialogViewModel.Purpose.Rename, name,
            _saved.Select(entry => entry.Name ?? string.Empty).Where(other => !SavedSearches.SameName(other, name)).ToList());
        if (new Views.SavedSearchNameDialog(model).ShowDialog() == true)
        {
            RenameSavedAsync(name, model.Name).Forget();
        }
    }

    public Task RenameSavedAsync(string name, string newName)
        => ChangeSavedAsync(list => SavedSearches.Rename(list, name, newName), focus: newName.Trim());

    public async Task DeleteSavedAsync(string name)
    {
        var answer = Services.Notice.Show(
            $"保存した検索「{name}」を削除します。\n削除すると元に戻せません。",
            "保存した検索を削除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        // 消した行に止まっていたら、すぐ下の行（無ければ上の行）へ止まり直す。行が無くなれば「今の検索を保存」へ
        var rows = SavedRows.Select(row => row.Name).ToList();
        var at = rows.FindIndex(other => SavedSearches.SameName(other, name));
        var neighbor = at < 0 ? null : at + 1 < rows.Count ? rows[at + 1] : at > 0 ? rows[at - 1] : null;
        await ChangeSavedAsync(list => SavedSearches.Remove(list, name), focus: neighbor ?? string.Empty);
    }

    public Task MoveSavedAsync(string name, int delta)
        => ChangeSavedAsync(list => SavedSearches.Move(list, name, delta), focus: name);

    /// <summary>
    /// 行を作り直した後に止まり直す先（行の名前。空なら「今の検索を保存」）。行は書くたびに作り直すので、
    /// 止まっていた行が消えて止まり先が窓へ落ちる（`ui-input.md`「止まっていた行が消えたら」）
    /// </summary>
    public event Action<string>? SavedRowFocusRequested;

    private async Task ChangeSavedAsync(
        Func<IReadOnlyList<SearchHistoryEntry>, IReadOnlyList<SearchHistoryEntry>> change,
        string? focus = null)
    {
        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSavedSearches(
            stored => new SavedSearchList { Entries = change(stored.Entries) }));

        if (result is Core.Commands.CommandResult.SavedSearchesChanged changed)
        {
            LoadSaved(changed.Saved.Entries);
            if (focus is not null)
            {
                SavedRowFocusRequested?.Invoke(focus);
            }
        }
    }

    /// <summary>
    /// 保存した検索を呼び出す。今の検索を**置き換える**（ユーザ判断 2026-10-03「条件は置き換えます」）。
    ///
    /// 条件の当て方は履歴を押したときと同じ（値を全部戻してから、同じ種類の n 番目へ当てる）。
    /// 今は無い値（消したタグ・属性・カテゴリ）も、履歴と同じく**そのまま条件に入れる**——黙って落とすと、
    /// 保存したときより広い結果が「保存した検索の結果」として出る。入れておけば条件の欄にその値が見え、0件になる理由も読める。
    /// 表示順だけは「無い項目で並べる」ができない（消した属性で並べていた）ので、既定の順に戻して窓で言う
    /// </summary>
    public void ApplySaved(SearchHistoryEntry entry)
    {
        var sortFound = ApplySearch(entry, replaceSort: true, view: entry.View ?? ResultView.Card);
        if (!sortFound)
        {
            Services.Notice.Show(
                $"表示順「{entry.Sort}」は今は選べないため、{DefaultSort.Label}で並べています。",
                "保存した検索",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadSaved(IReadOnlyList<SearchHistoryEntry> entries)
    {
        // 手で直した JSON で、並びの中に null が書かれていても落ちないように飛ばす
        _saved = entries.Where(entry => entry is not null).ToList();
        RebuildSavedRows();
        OnPropertyChanged(nameof(SavedSearchCount));
        OnPropertyChanged(nameof(HasSavedSearches));
        OnPropertyChanged(nameof(ShowsSavedFilter));
        OnPropertyChanged(nameof(SavedCountText));
        if (!ShowsSavedFilter && _savedFilter.Length > 0)
        {
            // 探す欄が隠れたのに、前に打った文字で行が絞られたままにならないように
            _savedFilter = string.Empty;
            OnPropertyChanged(nameof(SavedFilter));
            RebuildSavedRows();
        }
    }

    private void RebuildSavedRows()
    {
        var filter = _savedFilter.Trim();
        SavedRows.Clear();
        for (var index = 0; index < _saved.Count; index++)
        {
            var entry = _saved[index];
            var summary = SavedSummary(entry);
            if (filter.Length > 0
                && !Contains(entry.Name, filter)
                && !Contains(summary, filter))
            {
                continue;
            }

            SavedRows.Add(new SavedSearchRow(this, entry, summary, canMoveUp: index > 0, canMoveDown: index < _saved.Count - 1));
        }

        OnPropertyChanged(nameof(ShowsSavedFilterEmpty));
        RefreshSavedCurrent();

        static bool Contains(string? text, string part)
            => text is not null && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                text, part, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreWidth | System.Globalization.CompareOptions.IgnoreKanaType) >= 0;
    }

    /// <summary>
    /// 今の検索と同じ保存した検索に印を付ける。同じとは、指紋（文字・条件・表示順・探す対象と区別）とカードかリストかが同じこと——
    /// 呼び出しても何も変わらない物だけを「今の検索」と言う。絞り直すたびと、カードとリストを切り替えたときに呼ぶ
    /// </summary>
    private void RefreshSavedCurrent()
    {
        string? current = null;
        if (_saved.Count > 0)
        {
            var now = CurrentSaved(null);
            var print = now.Fingerprint;
            current = _saved.FirstOrDefault(entry => entry.Fingerprint == print && (entry.View ?? ResultView.Card) == now.View)?.Name;
        }

        foreach (var row in SavedRows)
        {
            row.IsCurrent = current is not null && SavedSearches.SameName(row.Name, current);
        }

        if (!string.Equals(_currentSavedName, current, StringComparison.Ordinal))
        {
            _currentSavedName = current;
            OnPropertyChanged(nameof(CurrentSavedName));
            OnPropertyChanged(nameof(ShowsCollapsedCurrent));
        }
    }

    /// <summary>行の「…」と右クリックの操作の入口（窓を出す物は画面から呼ぶ）。</summary>
    internal void RenameSavedFromMenu(string name) => AskRenameSaved(name);
}

/// <summary>保存した検索の節の1行。</summary>
public sealed class SavedSearchRow : ViewModelBase
{
    private bool _isCurrent;

    internal SavedSearchRow(SearchViewModel owner, SearchHistoryEntry entry, string summary, bool canMoveUp, bool canMoveDown)
    {
        Entry = entry;
        Summary = summary;
        var name = entry.Name ?? string.Empty;
        ApplyCommand = new RelayCommand(() => owner.ApplySaved(entry));
        OverwriteCommand = new RelayCommand(() => owner.OverwriteSavedAsync(name).Forget(), () => !_isCurrent);
        RenameCommand = new RelayCommand(() => owner.RenameSavedFromMenu(name));
        MoveUpCommand = new RelayCommand(() => owner.MoveSavedAsync(name, -1).Forget(), () => canMoveUp);
        MoveDownCommand = new RelayCommand(() => owner.MoveSavedAsync(name, +1).Forget(), () => canMoveDown);
        DeleteCommand = new RelayCommand(() => owner.DeleteSavedAsync(name).Forget());
    }

    public SearchHistoryEntry Entry { get; }

    /// <summary>名前の無い行（手で書いた JSON）は要約を名前の代わりに出す。</summary>
    public string Name => Entry.IsNamed ? Entry.Name! : Summary;

    public string Summary { get; }

    /// <summary>今の検索がこの行と同じ（地の色を変える。上書きしても何も変わらないので、上書きは押せない）。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetField(ref _isCurrent, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand OverwriteCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public RelayCommand DeleteCommand { get; }
}
