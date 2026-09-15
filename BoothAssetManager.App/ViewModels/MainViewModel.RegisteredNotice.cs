namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主画面：登録し終えた商品への入口（ユーザ指示 2026-09-15）。
///
/// 前は、落とした URL の商品を登録し終えた所で商品ページへ移っていた。登録は BOOTH の順番を待つので、
/// 待つ間に検索を進めていると、見ていた画面が勝手に登録した商品に替わる。移らずに下の帯で「開く」を出す。
/// 聞くのは続けて登録したうちの**先頭の1件だけ**（1件ごとに出し直すと、帯が次々に替わって押せない）。
/// 押すか閉じると、次に登録した物からまた聞く。
/// </summary>
public sealed partial class MainViewModel
{
    private string? _registeredItemId;
    private string _registeredName = string.Empty;
    private RelayCommand? _openRegistered;
    private RelayCommand? _dismissRegistered;

    public bool HasRegisteredNotice => _registeredItemId is not null;

    public string RegisteredNoticeText => _registeredItemId is null ? string.Empty : $"「{_registeredName}」を登録しました。";

    public RelayCommand OpenRegisteredCommand => _openRegistered ??= new RelayCommand(() => OpenRegisteredAsync().Forget());

    public RelayCommand DismissRegisteredCommand => _dismissRegistered ??= new RelayCommand(() => SetRegistered(null, string.Empty));

    private void NoteRegistered(Core.Models.ItemRecord item)
    {
        if (_registeredItemId is null)
        {
            SetRegistered(item.Id, item.DisplayName);
        }
    }

    /// <summary>押した時点の中身で開く（帯を出してから取り直し・IDの変更があり得る）。</summary>
    private async Task OpenRegisteredAsync()
    {
        if (_registeredItemId is not { } itemId)
        {
            return;
        }

        SetRegistered(null, string.Empty);
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            ShowItem(item);
        }
    }

    private void SetRegistered(string? itemId, string name)
    {
        _registeredItemId = itemId;
        _registeredName = name;
        OnPropertyChanged(nameof(HasRegisteredNotice));
        OnPropertyChanged(nameof(RegisteredNoticeText));
    }
}
