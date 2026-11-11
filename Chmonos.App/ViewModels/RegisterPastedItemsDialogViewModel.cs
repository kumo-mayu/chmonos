namespace Chmonos.App.ViewModels;

/// <summary>コピーした物から見つけた商品1件（まとめて登録する窓の1行）。</summary>
public sealed class PastedItemRow : ViewModelBase
{
    private bool _isChecked;

    public required string ItemId { get; init; }

    /// <summary>ライブラリにある商品の名前。無い商品は取る前なので名前が分からない（null）。</summary>
    public string? KnownName { get; init; }

    /// <summary>ライブラリに既にあるか。ある物はチェックを外して並べ、押せなくする（登録しても何も変わらない）。</summary>
    public bool IsKnown => KnownName is not null;

    public bool CanCheck => !IsKnown;

    /// <summary>行の名前。無い商品は取る前なので ID で言う。</summary>
    public string Name => KnownName ?? $"商品ID {ItemId}";

    /// <summary>行の下の小さな字。ある物だけ。</summary>
    public string NoteText => IsKnown ? $"ライブラリにあります・商品ID {ItemId}" : string.Empty;

    public bool HasNote => IsKnown;

    /// <summary>読み上げの名前。行の名前だけでは、ある物と無い物を聞き分けられない。</summary>
    public string AutomationName => IsKnown ? $"{Name}、ライブラリにあります" : Name;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            // ある物はチェックできない（押せない見た目と食い違わないように、値でも止める）
            if (SetField(ref _isChecked, value && !IsKnown))
            {
                CheckedChanged?.Invoke();
            }
        }
    }

    internal Action? CheckedChanged { get; set; }
}

/// <summary>
/// コピーした物（何行かを選んだ文・HTML）から見つけた2件以上の商品を、まとめて登録する窓（計画 26・ユーザ判断 2026-11-11）。
/// 出てきた順に並べ、ライブラリに無い物にチェックを付けて開く。ある物はチェックを外して並べる（見つかったことは見せる）。
/// </summary>
public sealed class RegisterPastedItemsDialogViewModel : ViewModelBase
{
    private RelayCommand? _selectAll;
    private RelayCommand? _clearSelection;

    public RegisterPastedItemsDialogViewModel(IEnumerable<(string ItemId, string? KnownName)> items)
    {
        Rows = items.Select(item => new PastedItemRow { ItemId = item.ItemId, KnownName = item.KnownName }).ToList();
        foreach (var row in Rows)
        {
            row.IsChecked = !row.IsKnown;
            row.CheckedChanged = OnCheckedChanged;
        }
    }

    /// <summary>
    /// 試験から窓を出さずに答える口。登録するなら true（チェックは各行の <see cref="PastedItemRow.IsChecked"/> に残る）
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）。
    /// </summary>
    internal static Func<RegisterPastedItemsDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>選ばせる。「n 件を登録」なら true。</summary>
    public static bool Ask(RegisterPastedItemsDialogViewModel model)
        => Intercept is { } intercept ? intercept(model) : new Views.RegisterPastedItemsDialog(model).ShowDialog() == true;

    public IReadOnlyList<PastedItemRow> Rows { get; }

    public string Title => "商品をまとめて登録";

    private int NewCount => Rows.Count(row => !row.IsKnown);

    public bool AllKnown => NewCount == 0;

    public string HeadingText => AllKnown
        ? $"見つかった {Rows.Count} 件は、すべてライブラリにあります。"
        : $"{Rows.Count} 件の商品が見つかりました。登録する商品を選んでください。";

    /// <summary>通信が起きることと、ファイルは来ないこと（ui-writing「正確さを落とさない」）。すべてある時は何も起きないので出さない。</summary>
    public string NoteText => AllKnown
        ? string.Empty
        : "BOOTHから情報を取得して、ファイルを持たない商品として登録します。ファイルはダウンロードしません。";

    public bool HasNote => !AllKnown;

    public IReadOnlyList<string> CheckedIds => Rows.Where(row => row.IsChecked).Select(row => row.ItemId).ToList();

    public int CheckedCount => Rows.Count(row => row.IsChecked);

    public string CommitText => $"{CheckedCount} 件を登録";

    public bool CanCommit => CheckedCount > 0;

    /// <summary>押せないときの理由（E9）。すべてある時は、見出しの文がそのまま理由なので重ねない。</summary>
    public string CommitHint => CanCommit || AllKnown ? string.Empty : "登録する商品を選んでください。";

    public bool HasCommitHint => CommitHint.Length > 0;

    /// <summary>まとめて選ぶ・外すのは、ライブラリに無い物が2件以上あるときだけ（1件なら行を押せば済む）。</summary>
    public bool HasBulkSelect => NewCount >= 2;

    public RelayCommand SelectAllCommand => _selectAll ??= new RelayCommand(() => SetAll(true));

    public RelayCommand ClearSelectionCommand => _clearSelection ??= new RelayCommand(() => SetAll(false));

    private void SetAll(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsChecked = value;
        }
    }

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CommitText));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CommitHint));
        OnPropertyChanged(nameof(HasCommitHint));
    }
}
