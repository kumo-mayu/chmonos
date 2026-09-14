using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：選んだカードへのまとめた操作（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand SendSelectionToEditCommand { get; }

    /// <summary>選んだ物をまとめてお気に入りに入れる（#44）。</summary>
    public RelayCommand AddSelectionToFavoritesCommand { get; }

    /// <summary>選んだ物をまとめて改変に足す（#44）。</summary>
    public RelayCommand AddSelectionToModificationCommand { get; }

    /// <summary>選んだ物の unitypackage を、開いている Unity へ順に送る（#69）。</summary>
    public RelayCommand SendSelectionToUnityCommand { get; }

    private bool _isSendingToUnity;

    /// <summary>送っている最中か。二重に始めさせない。</summary>
    public bool IsSendingToUnity
    {
        get => _isSendingToUnity;
        private set
        {
            if (SetField(ref _isSendingToUnity, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _unityQueueText = string.Empty;

    /// <summary>今どこまで送ったか。取り込み画面は Unity 側に出るので、こちらには進み具合だけを出す。</summary>
    public string UnityQueueText
    {
        get => _unityQueueText;
        private set
        {
            if (SetField(ref _unityQueueText, value))
            {
                OnPropertyChanged(nameof(HasUnityQueueText));
            }
        }
    }

    public bool HasUnityQueueText => UnityQueueText.Length > 0;

    /// <summary>
    /// 選んだ商品の unitypackage を、選んだ順（表示中の並び）に1件ずつ Unity へ積む（#69・ユーザ追加要望）。
    /// 1件ずつ取り込み画面が出るので、利用者が Import か Cancel を押すと次が出る。
    /// </summary>
    /// <remarks>中身はフォルダビューの右側と共通（<see cref="ItemSelectionActions"/>）。</remarks>
    private Task SendSelectionToUnityAsync()
        => ItemSelectionActions.SendToUnityAsync(
            _services, SelectedCards(), sending => IsSendingToUnity = sending, text => UnityQueueText = text);

    /// <summary>選んだカード。表示中の並びを先に、絞り込みを変えて見えなくなった物を後に。</summary>
    private List<ItemCardViewModel> SelectedCards()
    {
        var cards = _matches.Where(card => card.IsSelected).ToList();
        cards.AddRange(_cards.Values.Where(card => card.IsSelected && !cards.Contains(card)));
        return cards;
    }

    /// <summary>
    /// 選んだ物に星を付ける。付いている物はそのまま（外す操作ではない）。
    /// 選択は解かない——続けて「改変に足す」などをしたいことがある。
    /// </summary>
    private async Task AddSelectionToFavoritesAsync()
    {
        foreach (var card in SelectedCards().Where(card => !card.IsFavorite))
        {
            await ToggleFavoriteAsync(card);
        }
    }

    /// <summary>
    /// 選んだ物を1つの改変に足す。どの改変かは商品ページの「改変に足す」と同じ画面で1回だけ選ぶ。
    ///
    /// **既にその改変に入っている商品は重ねて足さない。**まとめて足すときは、
    /// どれが入っていたかを1件ずつ覚えていないので、同じ物が2行並ぶと記録を確かめにくい
    /// （別の版を2回入れたいときは、商品ページから1件ずつ足せる）。
    /// </summary>
    /// <remarks>中身はフォルダビューの右側と共通（<see cref="ItemSelectionActions"/>）。</remarks>
    private async Task AddSelectionToModificationAsync()
    {
        if (await ItemSelectionActions.AddToModificationAsync(_services, SelectedCards()))
        {
            // 改変が変わったので、「着せているアバター」の絞り込みが読み直すようにする
            NoteModificationsChanged();
        }
    }

    /// <summary>選択中の件数。0より大きいときだけ操作バーを出す。</summary>
    public int SelectedCount => _cards.Values.Count(card => card.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionText => $"{SelectedCount} 件を選択中";

    private void OnCardSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));

        // 1件でも選ぶと「選ぶ操作」が主になる。カード全体が選択の的になり、
        // 中を見るのは専用のボタンへ移る（カードごとに知らせる必要がある）
        var selecting = HasSelection;
        foreach (var card in _cards.Values)
        {
            card.IsSelectionMode = selecting;
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>今の絞り込み結果を全部選ぶ。画面に出ていないものは選ばない。</summary>
    private void SelectAllMatches()
    {
        foreach (var card in _matches)
        {
            card.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var card in _cards.Values.Where(card => card.IsSelected))
        {
            card.IsSelected = false;
        }
    }

    /// <summary>
    /// 選んだitemを編集画面のキューに積んで送る。
    /// 絞り込んでから選ぶ流れになるので、並び順はそのまま渡す。
    /// </summary>
    private void SendSelectionToEdit()
    {
        var ids = _matches
            .Where(card => card.IsSelected)
            .Select(card => card.Item.Id)
            .ToList();

        // 絞り込みを変えた後でも、選択したものは全て送る
        foreach (var card in _cards.Values.Where(card => card.IsSelected && !ids.Contains(card.Item.Id)))
        {
            ids.Add(card.Item.Id);
        }

        if (ids.Count == 0 || _main is null)
        {
            return;
        }

        ClearSelection();
        _main.ShowEditAsync(ids).Forget();
    }

    /// <summary>
    /// 文字列で探す。スペースでAND、<c>-語</c>で除外、<c>"..."</c>でフレーズ、
    /// <c>OR</c> と <c>( )</c> が使える。
    ///
    /// 既定の対象は 商品名／ショップ名／サブドメイン／メモ／BOOTHタグ。
    /// 本文とパスは当たりすぎて「なぜこれが出たのか」が分からなくなるので、
    /// トグルで明示的に広げたときだけ見る。
    /// </summary>
    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetField(ref _queryText, value))
            {
                // 式の解釈は入力ごとに1回。商品ごとにやると件数ぶん無駄に走る
                _queryNode = Core.Services.SearchQuery.Parse(_queryText);

                // 打ち直したら、前の語で広げた式は捨てる。
                // 残すと次の検索が前の語の別表記で当たってしまう
                ClearWidening();
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// 別の表記でも探すか。
    ///
    /// 切っていても**0件のときは自動で広げる**（何も出ないより出た方がよく、
    /// 広げたことは結果の上に出るので誤解も生まない）。
    /// 入にすると、当たっているときも一緒に広げる——
    /// 「tori」で当たった商品があっても『鳥』の商品を見たい場面があるため。
    /// </summary>
    public bool SearchAlternates
    {
        get => _searchAlternates;
        set
        {
            if (SetField(ref _searchAlternates, value))
            {
                ClearWidening();
                ApplyFilters();
            }
        }
    }

    /// <summary>説明文とh2セクションも探すか。</summary>
    public bool SearchBody
    {
        get => _searchBody;
        set
        {
            if (SetField(ref _searchBody, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// ファイルのパスも探すか。
    /// 自分でリネームしたファイルは商品名と一致しないので、パスしか手掛かりが無い場合がある。
    /// </summary>
    public bool SearchPaths
    {
        get => _searchPaths;
        set
        {
            if (SetField(ref _searchPaths, value))
            {
                ApplyFilters();
            }
        }
    }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>ファイルを持っているものだけに絞る。</summary>
    public bool OwnedOnly
    {
        get => _ownedOnly;
        set
        {
            if (SetField(ref _ownedOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }
}
