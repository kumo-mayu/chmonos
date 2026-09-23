namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主画面：登録し終えた商品への入口（ユーザ指示 2026-09-15）。
///
/// 前は、落とした URL の商品を登録し終えた所で商品ページへ移っていた。登録は BOOTH の順番を待つので、
/// 待つ間に検索を進めていると、見ていた画面が勝手に登録した商品に替わる。移らずに下の帯で「開く」を出す。
/// 聞くのは続けて登録したうちの**先頭の1件だけ**（1件ごとに出し直すと、帯が次々に替わって押せない）。
/// 押すか閉じると、次に登録した物からまた聞く。
///
/// 商品ページで「商品情報を取り直す」を押して別の画面へ移った後に取り直しが済んだときも、同じ帯で知らせる
/// （同じ理由：待つ間に移った先の画面を、取り直した商品ページで勝手に置き換えない）。
///
/// **帯は1本のまま、出ている間に来た別の知らせは順番に待たせる**（ユーザ判断 2026-09-23）。
/// 前は出ている間に済んだ取り直しを捨てていたので、済んだことにも開く道にも気付けなかった。
/// 帯を知らせの種類ごとに増やすと画面の下が何段も積み上がるので、今のを押すか閉じたら次を出し、
/// 待っている数を帯の端に出す。続けて登録した分は今までどおり先頭の1件だけ（待たせない）。
/// </summary>
public sealed partial class MainViewModel
{
    private enum ItemNoticeKind
    {
        Registered,
        ChangedAway,
    }

    private sealed record ItemNotice(string ItemId, string Text, string Action, ItemNoticeKind Kind);

    private ItemNotice? _itemNotice;
    private readonly Queue<ItemNotice> _waitingItemNotices = new();
    private RelayCommand? _openRegistered;
    private RelayCommand? _dismissRegistered;

    public bool HasRegisteredNotice => _itemNotice is not null;

    public string RegisteredNoticeText => _itemNotice?.Text ?? string.Empty;

    /// <summary>帯のボタンの名前。登録と取り直しで、開く物の言い方を変える。</summary>
    public string RegisteredNoticeAction => _itemNotice?.Action ?? string.Empty;

    /// <summary>後に待っている知らせの数。閉じても次が出ると分かるように。</summary>
    public string RegisteredNoticeWaitingText => _waitingItemNotices.Count > 0 ? $"ほか {_waitingItemNotices.Count} 件" : string.Empty;

    public bool HasRegisteredNoticeWaiting => _waitingItemNotices.Count > 0;

    public RelayCommand OpenRegisteredCommand => _openRegistered ??= new RelayCommand(() => OpenRegisteredAsync().Forget());

    public RelayCommand DismissRegisteredCommand => _dismissRegistered ??= new RelayCommand(ShowNextItemNotice);

    private void NoteRegistered(Core.Models.ItemRecord item)
    {
        // 続けて登録した分は先頭の1件だけ聞く（上の説明）。出ているか待っている登録の知らせがあれば足さない
        if (_itemNotice?.Kind == ItemNoticeKind.Registered
            || _waitingItemNotices.Any(notice => notice.Kind == ItemNoticeKind.Registered))
        {
            return;
        }

        Enqueue(new ItemNotice(item.Id, $"「{item.DisplayName}」を登録しました。", "登録した商品を開く", ItemNoticeKind.Registered));
    }

    /// <summary>
    /// 商品ページを離れた後に、そのページで押した操作（取り直し・ファイルを外す）が済んだ。
    /// 移った先の画面は置き換えず、帯で知らせて開くかを選ばせる。
    /// </summary>
    public void NoteItemChangedAway(Core.Models.ItemRecord item, string text)
    {
        // 同じ商品の知らせが出ているか待っていれば、新しい文に差し替えるだけにする（開く先は同じ）
        if (_itemNotice is { Kind: ItemNoticeKind.ChangedAway } shown && shown.ItemId == item.Id)
        {
            SetItemNotice(shown with { Text = text });
            return;
        }

        if (_waitingItemNotices.Any(notice => notice.Kind == ItemNoticeKind.ChangedAway && notice.ItemId == item.Id))
        {
            var kept = _waitingItemNotices
                .Select(notice => notice.Kind == ItemNoticeKind.ChangedAway && notice.ItemId == item.Id ? notice with { Text = text } : notice)
                .ToList();
            _waitingItemNotices.Clear();
            kept.ForEach(_waitingItemNotices.Enqueue);
            return;
        }

        Enqueue(new ItemNotice(item.Id, text, "商品ページを開く", ItemNoticeKind.ChangedAway));
    }

    private void Enqueue(ItemNotice notice)
    {
        if (_itemNotice is null)
        {
            SetItemNotice(notice);
            return;
        }

        _waitingItemNotices.Enqueue(notice);
        RaiseItemNoticeWaiting();
    }

    /// <summary>押した時点の中身で開く（帯を出してから取り直し・IDの変更があり得る）。</summary>
    private async Task OpenRegisteredAsync()
    {
        if (_itemNotice?.ItemId is not { } itemId)
        {
            return;
        }

        ShowNextItemNotice();
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            ShowItem(item);
        }
    }

    /// <summary>今の知らせを下ろし、待っていた次を出す（無ければ帯を畳む）。</summary>
    private void ShowNextItemNotice()
    {
        SetItemNotice(_waitingItemNotices.Count > 0 ? _waitingItemNotices.Dequeue() : null);
        RaiseItemNoticeWaiting();
    }

    private void SetItemNotice(ItemNotice? notice)
    {
        _itemNotice = notice;
        OnPropertyChanged(nameof(HasRegisteredNotice));
        OnPropertyChanged(nameof(RegisteredNoticeText));
        OnPropertyChanged(nameof(RegisteredNoticeAction));
    }

    private void RaiseItemNoticeWaiting()
    {
        OnPropertyChanged(nameof(RegisteredNoticeWaitingText));
        OnPropertyChanged(nameof(HasRegisteredNoticeWaiting));
    }
}
