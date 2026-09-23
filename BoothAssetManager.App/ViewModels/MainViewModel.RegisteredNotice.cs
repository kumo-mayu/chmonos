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
/// </summary>
public sealed partial class MainViewModel
{
    private string? _registeredItemId;
    private string _registeredText = string.Empty;
    private string _registeredAction = string.Empty;
    private RelayCommand? _openRegistered;
    private RelayCommand? _dismissRegistered;

    public bool HasRegisteredNotice => _registeredItemId is not null;

    public string RegisteredNoticeText => _registeredItemId is null ? string.Empty : _registeredText;

    /// <summary>帯のボタンの名前。登録と取り直しで、開く物の言い方を変える。</summary>
    public string RegisteredNoticeAction => _registeredAction;

    public RelayCommand OpenRegisteredCommand => _openRegistered ??= new RelayCommand(() => OpenRegisteredAsync().Forget());

    public RelayCommand DismissRegisteredCommand => _dismissRegistered ??= new RelayCommand(() => SetRegistered(null, string.Empty, string.Empty));

    private void NoteRegistered(Core.Models.ItemRecord item)
    {
        if (_registeredItemId is null)
        {
            SetRegistered(item.Id, $"「{item.DisplayName}」を登録しました。", "登録した商品を開く");
        }
    }

    /// <summary>
    /// 商品ページを離れた後に、そのページで押した操作（取り直し・ファイルを外す）が済んだ。
    /// 移った先の画面は置き換えず、帯で知らせて開くかを選ばせる。
    /// </summary>
    public void NoteItemChangedAway(Core.Models.ItemRecord item, string text)
    {
        if (_registeredItemId is null)
        {
            SetRegistered(item.Id, text, "商品ページを開く");
        }
    }

    /// <summary>押した時点の中身で開く（帯を出してから取り直し・IDの変更があり得る）。</summary>
    private async Task OpenRegisteredAsync()
    {
        if (_registeredItemId is not { } itemId)
        {
            return;
        }

        SetRegistered(null, string.Empty, string.Empty);
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            ShowItem(item);
        }
    }

    private void SetRegistered(string? itemId, string text, string action)
    {
        _registeredItemId = itemId;
        _registeredText = text;
        _registeredAction = action;
        OnPropertyChanged(nameof(HasRegisteredNotice));
        OnPropertyChanged(nameof(RegisteredNoticeText));
        OnPropertyChanged(nameof(RegisteredNoticeAction));
    }
}
