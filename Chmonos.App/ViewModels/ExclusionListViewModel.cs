namespace Chmonos.App.ViewModels;

/// <summary>除外の一覧の1件の元。並びは渡す側が決める（除外は新しい順）。</summary>
/// <param name="Key">解除するときに渡す物（除外はハッシュ、外した記録は「ハッシュ:商品ID」）。</param>
/// <param name="Path">行に出すパス。探す欄はこれ（ファイル名もパスの一部）で絞る。</param>
/// <param name="SubText">行の2段目（除外の理由・どの商品から外したか）。</param>
public sealed record ExclusionEntry(string Key, string Path, string SubText = "");

/// <summary>除外の一覧の1行。チェックは一覧の側（キーの集合）が持つ——絞り込みで行が消えても選んだ物を忘れないため。</summary>
public sealed class ExclusionRowViewModel : ViewModelBase
{
    private readonly ExclusionListViewModel _owner;

    internal ExclusionRowViewModel(ExclusionListViewModel owner, ExclusionEntry entry)
    {
        _owner = owner;
        Entry = entry;
        LiftCommand = new RelayCommand(() => owner.LiftAsync([entry.Key]).Forget(), () => !owner.IsBusy);
    }

    internal ExclusionEntry Entry { get; }

    public string Key => Entry.Key;

    public string Path => Entry.Path;

    public string SubText => Entry.SubText;

    public RelayCommand LiftCommand { get; }

    /// <summary>行の右のボタンの文字（「解除」「戻す」）。読み上げの名前にも使う。</summary>
    public string ActionLabel => _owner.RowAction;

    public string ActionTip => _owner.RowTip;

    public bool IsChecked
    {
        get => _owner.IsChecked(Key);
        set => _owner.SetChecked(Key, value);
    }

    internal void NotifyChecked() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>
/// 除外の一覧の共通部品（open.md 132・171・ユーザ判断 2026-11-19）。設定の「管理対象から除外したファイル」「商品から外したファイル」と、
/// 未確定の「除外の解除…」の窓で同じ物を使う。
///
/// - 並びは渡された順（除外は新しい順）。上の探す欄でパス・ファイル名を絞る（空白で区切ると全部を含む物）
/// - 行のチェック（行を押しても切り替わる）と、行ごとの「解除」。下の「選んだ n 件の除外を解除」は **見えている行のうち選んだ物だけ** に効く
///   （絞り込みで隠れた行まで解除すると、何を解除したのか画面から分からない）
/// - 行は <see cref="IsActive"/> の間だけ作る（設定の欄は畳んでいる間は件数だけ。除外は上限なく溜まる。large-files-2026-09-30.md）
/// - 解除は <paramref name="lift"/> に1回で渡す（数千件でも記録の書き込みは1回。<c>UiCommand.RestoreExcluded</c>）
/// </summary>
public sealed class ExclusionListViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<string>, Task<string>> _lift;
    private readonly string _bulkFormat;
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExclusionRowViewModel> _rows = new(StringComparer.Ordinal);
    private IReadOnlyList<ExclusionEntry> _entries = [];
    private bool _isActive;
    private string _query = string.Empty;
    private string _note = string.Empty;
    private bool _isBusy;

    /// <param name="rowAction">行のボタンの文字（「解除」「戻す」）。</param>
    /// <param name="bulkFormat">下のボタンの文字。{0} に選んだ件数が入る。</param>
    /// <param name="rowTip">行のボタンの吹き出し。</param>
    /// <param name="lift">解除してから一覧を読み直し（<see cref="SetEntries"/>）、結果の1行を返す。</param>
    public ExclusionListViewModel(string rowAction, string bulkFormat, string rowTip, Func<IReadOnlyList<string>, Task<string>> lift)
    {
        RowAction = rowAction;
        RowTip = rowTip;
        _bulkFormat = bulkFormat;
        _lift = lift;
        LiftCheckedCommand = new RelayCommand(() => LiftAsync(CheckedVisibleKeys()).Forget(), () => !IsBusy && CheckedVisibleCount > 0);
    }

    public string RowAction { get; }

    public string RowTip { get; }

    /// <summary>見えている行（絞り込んだ後）。</summary>
    public RangeObservableCollection<ExclusionRowViewModel> Rows { get; } = new();

    public int TotalCount => _entries.Count;

    public bool HasAny => _entries.Count > 0;

    public string CountText => $"{_entries.Count} 件";

    /// <summary>行を作るか（設定の欄を開いている間・窓を開いている間）。閉じたら行を捨てる（選んだ物は覚えておく）。</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                Refilter();
            }
        }
    }

    /// <summary>探す欄。</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasQuery));
                Refilter();
            }
        }
    }

    public bool HasQuery => Query.Length > 0;

    private RelayCommand? _clearQueryCommand;

    /// <summary>探す欄の ×。</summary>
    public RelayCommand ClearQueryCommand => _clearQueryCommand ??= new RelayCommand(() => Query = string.Empty);

    /// <summary>探して1件も無いとき（記録はある）。</summary>
    public bool IsFilteredEmpty => IsActive && HasAny && Rows.Count == 0;

    public int CheckedVisibleCount => Rows.Count(row => _checked.Contains(row.Key));

    public string LiftCheckedText => string.Format(System.Globalization.CultureInfo.CurrentCulture, _bulkFormat, CheckedVisibleCount);

    public RelayCommand LiftCheckedCommand { get; }

    private RelayCommand? _selectAllCommand;
    private RelayCommand? _clearChecksCommand;

    /// <summary>「すべて選択」。未確定の一覧と同じく、見えている行だけに効く。</summary>
    public RelayCommand SelectAllCommand => _selectAllCommand ??= new RelayCommand(() => CheckVisible(true));

    /// <summary>「選択を解除」。見えている行の選びを外す。</summary>
    public RelayCommand ClearChecksCommand => _clearChecksCommand ??= new RelayCommand(() => CheckVisible(false));

    private void CheckVisible(bool check)
    {
        foreach (var row in Rows)
        {
            if (check ? _checked.Add(row.Key) : _checked.Remove(row.Key))
            {
                row.NotifyChecked();
            }
        }

        NotifyChecks();
    }

    /// <summary>解除の結果の1行。</summary>
    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                // RelayCommand は RequerySuggested で押せるかを引き直す
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>記録を差し替える（読み直した後）。無くなった物は選びからも外す。</summary>
    public void SetEntries(IReadOnlyList<ExclusionEntry> entries)
    {
        _entries = entries;
        var alive = new HashSet<string>(entries.Select(entry => entry.Key), StringComparer.Ordinal);
        _checked.RemoveWhere(key => !alive.Contains(key));
        foreach (var key in _rows.Keys.Where(key => !alive.Contains(key)).ToList())
        {
            _rows.Remove(key);
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(CountText));
        Refilter();
    }

    internal bool IsChecked(string key) => _checked.Contains(key);

    internal void SetChecked(string key, bool value)
    {
        if (value ? _checked.Add(key) : _checked.Remove(key))
        {
            if (_rows.TryGetValue(key, out var row))
            {
                row.NotifyChecked();
            }

            NotifyChecks();
        }
    }

    internal IReadOnlyList<string> CheckedVisibleKeys() => Rows.Where(row => _checked.Contains(row.Key)).Select(row => row.Key).ToList();

    internal async Task LiftAsync(IReadOnlyList<string> keys)
    {
        if (IsBusy || keys.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Note = await _lift(keys);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refilter()
    {
        if (!IsActive)
        {
            _rows.Clear();
            if (Rows.Count > 0)
            {
                Rows.ReplaceAll([]);
            }
        }
        else
        {
            var terms = Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var target = _entries
                .Where(entry => terms.All(term => entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .Select(entry =>
                {
                    // 同じ鍵でも中身（パス・理由）が変わったら作り直す（読み直しで場所が増えた時など）
                    if (!_rows.TryGetValue(entry.Key, out var row) || row.Entry != entry)
                    {
                        row = new ExclusionRowViewModel(this, entry);
                        _rows[entry.Key] = row;
                    }

                    return row;
                })
                .ToList();

            // 残った行は使い回し、差分だけ抜き差しする——まとめて差し替えると、一覧の流した位置が先頭へ戻り、解除した近くを見失う
            if (Rows.Count == 0)
            {
                Rows.ReplaceAll(target);
            }
            else
            {
                CollectionSync.Apply(Rows, target);
            }
        }

        OnPropertyChanged(nameof(IsFilteredEmpty));
        NotifyChecks();
    }

    private void NotifyChecks()
    {
        OnPropertyChanged(nameof(CheckedVisibleCount));
        OnPropertyChanged(nameof(LiftCheckedText));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
