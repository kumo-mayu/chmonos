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

    /// <summary>帯を出すか。選んでいる間と、Unity へ送っている間と、BOOTH から取得し直している間（止める口を残す）。</summary>
    public bool ShowsSelectionBar => HasSelection || IsSendingToUnity || Bulk.IsRefreshing;

    /// <summary>帯の「お気に入り ▾」「タグ・属性 ▾」「その他 ▾」（ユーザ判断 2026-11-20。3つの画面で共通の中身）。</summary>
    public SelectionBulkActions Bulk => _bulk ??= new SelectionBulkActions(
        _services, _main, () => SelectedCards(), items => NoteItemsChanged(items, keepSelection: true),
        () => OnPropertyChanged(nameof(ShowsSelectionBar)));

    private SelectionBulkActions? _bulk;

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

    /// <summary>ファイルを割り振れるか。1件だけでは割り振る相手がいない（plan 30）。</summary>
    public bool CanOpenFilePool => SelectedCount >= 2;

    /// <summary>選んだ商品の間でファイルを割り振るサブ画面を開く（plan 30）。選択は外さない（割り振りながら一覧も使う）。</summary>
    public RelayCommand OpenFilePoolCommand => _openFilePool ??= new RelayCommand(
        () => _main?.ShowFilePool(SelectedCards().Select(card => card.Item.Id).ToList()));

    private RelayCommand? _openFilePool;

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
        OnPropertyChanged(nameof(CanOpenFilePool));
        _bulk?.NoteSelectionChanged();

        // 1件でも選ぶと「選ぶ操作」が主になる。カード全体が選択の的になり、
        // 中を見るのは専用のボタンへ移る（カードごとに知らせる必要がある）
        var selecting = HasSelection;
        foreach (var card in _cards.Values)
        {
            card.IsSelectionMode = selecting;
        }

        RelayCommand.RaiseCanExecuteChanged();

        // 購読しているソフトへ（plan 44）。ID は流すときに読む
        _services.PluginSelection.Note("search", () => Services.PluginSelectionBatcher.SelectedIds(_matches, _cards.Values));
    }

    /// <summary>帯の「プラグイン ▾」が送る選択（plan 44 の②。選択の出来事と同じ並べ方）。</summary>
    public IReadOnlyList<string> SelectedItemIdsForPlugins() => Services.PluginSelectionBatcher.SelectedIds(_matches, _cards.Values);

    /// <summary>プラグインの項目の係（帯の「プラグイン ▾」を出すか）。</summary>
    public Services.PluginMenuService PluginMenus => _services.PluginMenus;

    /// <summary>今の絞り込み結果を全部選ぶ（帯の「すべて選択」・Ctrl+A）。画面に出ていないものは選ばない。</summary>
    public void SelectAllShown() => CardClickSelection.SelectAll(_matches, ChangeSelectionTogether);

    /// <summary>今の絞り込み結果の中で選び方を反転する（Ctrl+Shift+I。<see cref="CardClickSelection.Invert"/>）</summary>
    public void InvertSelection() => _clickSelection.Invert(_matches, _cards.Values, ChangeSelectionTogether);

    public void ClearSelection() => ChangeSelectionTogether(() =>
    {
        _clickSelection.Forget();
        foreach (var card in _cards.Values.Where(card => card.IsSelected).ToList())
        {
            card.IsSelected = false;
        }
    });

    private readonly CardClickSelection _clickSelection = new();

    /// <summary>Ctrl・Shift+クリックで選ぶ（Explorer に揃える。<see cref="CardClickSelection"/>）。範囲は検索の結果の並び（札は含まない）</summary>
    public bool SelectByClick(ItemCardViewModel card, bool control, bool shift)
        => _clickSelection.Click(card, control, shift, _matches, _cards.Values, ChangeSelectionTogether);

    public void NoteSelectionAnchor(ItemCardViewModel card) => _clickSelection.NoteAnchor(card);

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
            // 途中で例外が出ても、止めた知らせは必ず1回出す（帯や押せる・押せないが古いまま残らないように）
            _batchingSelection = false;
            OnCardSelectionChanged();
        }
    }

    /// <summary>
    /// 「まとめて編集する」で送り、まだ検索へ戻っていない商品。編集の間の読み直し・保存の知らせでカードが替わっても、
    /// この中で選んでいた物は選んだままにする。検索へ戻ったら捨てる（<see cref="NoteShown"/>）——その後の読み直し
    /// （取り込みの後など）は今までどおり選択を外す。捨てる時は入れ物ごと差し替える（読み直しが読み始めに掴んだ組を変えない）
    /// </summary>
    private HashSet<string> _sentToEdit = new(StringComparer.Ordinal);

    /// <summary>読み直し・作り直しの後も選んだままにするか。今選んでいて、編集へ送った商品だけ。</summary>
    private static bool KeepsSelection(ItemCardViewModel? card, HashSet<string> sent) => card is { IsSelected: true } && sent.Contains(card.Item.Id);

    /// <summary>検索が今の画面になった（主画面が呼ぶ）。編集へ送った組は役目を終える。</summary>
    public void NoteShown()
    {
        if (_sentToEdit.Count > 0)
        {
            _sentToEdit = new(StringComparer.Ordinal);
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

        // 選んでいた物は解除しない（ユーザ判断 2026-11-19）。編集から戻った時に、同じ組へ続けて別の操作
        // （まとめてタグを付ける・割り振る）をしたり、選び直して足したりできるように。
        // 検索の画面は1つを持ち回すが、編集を終えると一覧を読み直し、保存した商品はカードを作り直すので、その2つで外さないよう覚える
        _sentToEdit = ids.ToHashSet(StringComparer.Ordinal);
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
