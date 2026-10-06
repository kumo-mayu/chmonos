using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>検索画面：選んだカードへのまとめた操作（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand SendSelectionToEditCommand { get; }

    /// <summary>選んだ物をまとめてお気に入りに追加（#44）。</summary>
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
                OnPropertyChanged(nameof(ShowsSelectionBar));
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

    private UnitySendUi? _sendUi;

    /// <summary>
    /// カードの右クリックから1件送るときも、同じ帯に進み具合を出す（E10）。
    /// 帯は選んでいる間だけ出ていたので、送っている間も出すようにした（<see cref="ShowsSelectionBar"/>）。
    /// </summary>
    internal UnitySendUi SendUi => _sendUi ??= new UnitySendUi(
        sending => IsSendingToUnity = sending,
        text => UnityQueueText = text);

    /// <summary>帯を出すか。選んでいる間と、Unity へ送っている間。</summary>
    public bool ShowsSelectionBar => HasSelection || IsSendingToUnity;

    /// <summary>送るのをやめる（E7）。送信は1本ずつなので、どの画面から押しても同じ物が止まる。</summary>
    public RelayCommand StopUnityCommand => _stopUnity ??= new RelayCommand(Services.UnityImportQueue.Stop);

    private RelayCommand? _stopUnity;

    /// <summary>
    /// 選んだ商品の unitypackage を、選んだ順（表示中の並び）に1件ずつ Unity へ積む（#69・ユーザ追加要望）。
    /// 1件ずつ取り込み画面が出るので、利用者が Import か Cancel を押すと次が出る。
    /// </summary>
    /// <remarks>中身はフォルダビューの右側と共通（<see cref="ItemSelectionActions"/>）。</remarks>
    private Task SendSelectionToUnityAsync()
        => ItemSelectionActions.SendToUnityAsync(
            _services, SelectedCards(), sending => IsSendingToUnity = sending, text => UnityQueueText = text, NoteItemChanged);

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
        // 書けなかった物は1件ずつ窓にしない（ドライブが外れていると選んだ数だけ窓が出る）。最初の失敗を1回だけ出す
        string? firstFailure = null;
        foreach (var card in SelectedCards().Where(card => !card.IsFavorite).ToList())
        {
            var failure = await TryToggleFavoriteAsync(card);
            firstFailure ??= failure;
        }

        if (firstFailure is not null)
        {
            Tell("お気に入り", firstFailure, failed: true);
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

    /// <summary>選んだ物に未読の更新があるか。無ければ帯と右クリックの「既読にする」を出さない（カードの右クリックの「既読にする」と同じ出し方）。</summary>
    public bool HasSelectedUpdates => _cards.Values.Any(card => card.IsSelected && card.HasUpdate);

    /// <summary>選んだ物のうち未読の更新がある商品を、まとめて既読にする（ユーザ判断 2026-10-02「4は入れましょう」）。</summary>
    public RelayCommand MarkSelectionReadCommand => _markSelectionRead ??= new RelayCommand(
        () => MarkUpdatesReadAsync(SelectedCards().Where(card => card.HasUpdate).ToList()).Forget(),
        () => HasSelectedUpdates);

    private RelayCommand? _markSelectionRead;

    private void OnCardSelectionChanged()
    {
        if (_batchingSelection)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSelectedUpdates));
        OnPropertyChanged(nameof(ShowsSelectionBar));
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
    private void SelectAllMatches() => ChangeSelectionTogether(() =>
    {
        foreach (var card in _matches)
        {
            card.IsSelected = true;
        }
    });

    public void ClearSelection() => ChangeSelectionTogether(() =>
    {
        foreach (var card in _cards.Values.Where(card => card.IsSelected).ToList())
        {
            card.IsSelected = false;
        }
    });

    private bool _batchingSelection;

    /// <summary>
    /// まとめて選ぶ・外す間は、1件ごとの知らせを止め、最後に1回だけ知らせる（外部の点検 2026-10-07）。
    /// 1件ごとに知らせると、そのたびに全部のカードへ「選ぶ操作中か」を配り直し、件数も数え直すので、
    /// 2万件を全部選ぶと約4億回になり、その間ずっと画面が止まっていた
    /// </summary>
    private void ChangeSelectionTogether(Action change)
    {
        _batchingSelection = true;
        try
        {
            change();
        }
        finally
        {
            _batchingSelection = false;
        }

        OnCardSelectionChanged();
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
                OnPropertyChanged(nameof(HasQueryText));
                ApplyFilters();
            }
        }
    }

    /// <summary>打った文字が残っているか。残る欄は消す手段を出す（ユーザ指示 2026-09-16）。</summary>
    public bool HasQueryText => _queryText.Length > 0;

    /// <summary>検索の文字だけを消す（絞り込みの条件は触らない。全部消すのは「条件をクリア」）。</summary>
    public RelayCommand ClearQueryCommand => _clearQuery ??= new RelayCommand(() => QueryText = string.Empty);

    private RelayCommand? _clearQuery;

    // 探す対象・区別の切り替え・別表記は SearchViewModel.TextOptions.cs、絞り込みの条件は SearchViewModel.Modules.cs

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }
}
