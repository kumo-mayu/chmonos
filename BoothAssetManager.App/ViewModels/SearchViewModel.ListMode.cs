namespace BoothAssetManager.App.ViewModels;

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
                ItemListMode.Save(_services, "search", value);
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
}
