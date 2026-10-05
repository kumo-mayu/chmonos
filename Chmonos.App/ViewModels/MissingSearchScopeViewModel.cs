using System.Collections.ObjectModel;
using System.IO;

namespace Chmonos.App.ViewModels;

/// <summary>「見つからないファイルを探す」の窓の1行（探すフォルダ1つ）。</summary>
public sealed class SearchFolderRow : ViewModelBase
{
    private bool _isChecked = true;

    public required string Path { get; init; }

    /// <summary>監視フォルダか（違えばこの回だけ足したフォルダ）。</summary>
    public bool IsWatched { get; init; }

    public string KindText => IsWatched ? "監視フォルダ" : "今回だけ";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                Changed?.Invoke();
            }
        }
    }

    internal Action? Changed { get; set; }
}

/// <summary>
/// 「見つからないファイルを探す」を押したときに、どこを探すかを選ぶ窓（見つからない・移動の点検 11-A・ユーザ判断 2026-10-05）。
/// </summary>
/// <remarks>
/// 前は監視フォルダの中だけを探していて、監視していない場所へ移した物は「移した先を監視フォルダに追加してから、もう一度押して」と
/// 言うしかなかった。探したいだけの場所を監視に足すと、起動のたびにそこを新着として見に行くようになる。
/// だから監視フォルダは既定で入れておき、ほかの場所はこの回だけ足せるようにする。**足したフォルダは監視にも覚えにも残さない**
/// （覚えると「足した場所をいつ外すか」の操作が要る。次に探すときは移した先が違うのが普通なので、覚えない方が簡単）。
/// </remarks>
public sealed class MissingSearchScopeViewModel : ViewModelBase
{
    public MissingSearchScopeViewModel(IEnumerable<string> watchedFolders)
    {
        foreach (var folder in watchedFolders)
        {
            Add(folder, isWatched: true);
        }

        AddFolderCommand = new RelayCommand(PickFolders);
    }

    public ObservableCollection<SearchFolderRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public RelayCommand AddFolderCommand { get; }

    /// <summary>1つ以上選んでいるか（「探す」を押せるか）。</summary>
    public bool CanSearch => Rows.Any(row => row.IsChecked);

    /// <summary>押せないときの理由（E9）。押せるときは空。</summary>
    public string CommitHint => CanSearch ? string.Empty : "探すフォルダを1つ以上選んでください。";

    public string EmptyText => "監視フォルダがありません。「フォルダを追加…」で探す場所を選んでください。";

    /// <summary>選んだフォルダ（並びは窓の並び）。</summary>
    public IReadOnlyList<string> SelectedFolders => [.. Rows.Where(row => row.IsChecked).Select(row => row.Path)];

    /// <summary>
    /// この回だけ探すフォルダを足す。既に並んでいる場所はチェックを入れ直すだけ（同じ場所を2回探さない）。
    /// </summary>
    public void AddFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            var normalized = Path.TrimEndingDirectorySeparator(folder);
            var existing = Rows.FirstOrDefault(row => string.Equals(
                Path.TrimEndingDirectorySeparator(row.Path), normalized, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.IsChecked = true;
                continue;
            }

            Add(folder, isWatched: false);
        }

        OnPropertyChanged(nameof(HasRows));
        RaiseChecks();
    }

    private void Add(string folder, bool isWatched)
    {
        var row = new SearchFolderRow { Path = folder, IsWatched = isWatched };
        row.Changed = RaiseChecks;
        Rows.Add(row);
    }

    private void RaiseChecks()
    {
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CommitHint));
    }

    private void PickFolders()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "探すフォルダを選択",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true)
        {
            AddFolders(dialog.FolderNames);
        }
    }

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<MissingSearchScopeViewModel, bool>? Intercept { get; set; }

    /// <summary>窓で探す場所を選ばせる。「探す」なら true。</summary>
    public static bool Ask(MissingSearchScopeViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.MissingSearchScopeDialog(model).ShowDialog() == true;
}
