namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索画面：結果をカードかリストで出す（ユーザ指示 2026-09-14）。リストは1行ずつ、絵・星・名前・ショップ・札を並べる。
/// 列の幅は見出しの境目をドラッグで変え、全部の行で共通（<see cref="ItemListColumns"/>）。どちらで出すかは画面ごとに覚える。
/// </summary>
public sealed partial class SearchViewModel
{
    private bool _isListMode;
    private ItemListColumns? _listColumns;

    public bool IsListMode
    {
        get => _isListMode;
        set
        {
            if (SetField(ref _isListMode, value))
            {
                OnPropertyChanged(nameof(IsCardMode));
                OnPropertyChanged(nameof(ListItems));
                OnPropertyChanged(nameof(DisplayItems));
                OnPropertyChanged(nameof(ListViewItems));
                ItemListMode.Save(_services, "search", value);
                RefreshSavedCurrent();
            }
        }
    }

    public bool IsCardMode
    {
        get => !_isListMode;
        set => IsListMode = !value;
    }

    /// <summary>切り替えを押したときだけ変える（点いているかは読むだけ。ItemCardResources の ItemViewModeSwitch）。</summary>
    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => IsListMode = false);

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => IsListMode = true);

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public ItemListColumns ListColumns => _listColumns ??= new ItemListColumns(_services.PaneWidths, "search", hasSelect: true, shopHeader: "ショップ");

    /// <summary>リストに並べる物。カードと同じ物を同じ並びで（カードの ViewModel を使い回すので、星や選択も同じ）。</summary>
    public IReadOnlyList<ItemCardViewModel> ListItems => _matches;

    /// <summary>
    /// 画面のリスト（<c>ItemListView</c>）に渡す並び。**カードで出している間は空を渡す**（メモ2-① 2026-10-02）。
    /// 隠れたリストにも絞り込みのたびに新しい並びを渡していて、その組み直しが絞り直し1回に約90ms 乗っていた
    /// （作り物の200件・カード表示で、条件を1つ消すと画面まで約190ms のうち。`docs/research/search-modules-2026-10-01.md` §13）。
    /// </summary>
    public IReadOnlyList<object> ListViewItems => _isListMode ? DisplayItems : [];
}
