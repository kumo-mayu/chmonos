using Chmonos.Core.Commands;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページ：BOOTH の商品ページで変わった所の印（メモ7-①・ユーザ判断 2026-10-02）。
///
/// 要確認・ショップの「変更あり」から開いても、どこが変わったのかがページの上で分からなかった。
/// 未読の更新の知らせ（<see cref="Core.Models.NotificationKind.ItemUpdated"/>）の差から、変わった欄の左に色の線と札を付ける。
/// **印は「既読にする」を押すまで残す**（開いただけで既読にすると、読み終える前に別の画面へ移ったときに印が消える）。
/// 既読にするのは要確認の画面と同じ命令（<see cref="UiCommand.MarkNotificationsRead"/>）で、ナビの要確認の数も数え直す
/// </summary>
public sealed partial class ItemViewModel
{
    private ItemChanges _changes = ItemChanges.None;
    private bool _markingChangesRead;
    private RelayCommand? _markChangesRead;

    /// <summary>未読の更新があるか。ページの上の帯と「既読にする」を出す。</summary>
    public bool HasUnreadChanges => _changes.HasAny;

    public ChangeSlot NameChange => _changes.Name;

    public ChangeSlot VariationsChange => _changes.Variations;

    public ChangeSlot GalleryChange => _changes.Gallery;

    public ChangeSlot SaleChange => _changes.Sale;

    public ChangeSlot DescriptionChange => _changes.Description;

    /// <summary>ページに対応する欄の無い変化の1行（消えた見出しなど）。無ければ空。</summary>
    public string OtherChangesText => _changes.OthersText;

    public bool HasOtherChanges => OtherChangesText.Length > 0;

    public RelayCommand MarkChangesReadCommand => _markChangesRead ??= new RelayCommand(
        () => MarkChangesReadAsync().Forget(),
        () => _changes.HasAny && !_markingChangesRead);

    /// <summary>この商品の未読の更新を読む。知らせのファイルを読むだけなので、画面から直に引く（書き込みは命令を通す）。</summary>
    private async Task LoadChangesAsync()
    {
        var itemId = Item.Id;
        var keys = Sections.Select(section => section.Key).ToList();
        var changes = await Task.Run(() => ItemChanges.From(
            _services.Notifications.Load().Where(record => ItemChanges.IsUnreadUpdateOf(record, itemId)),
            keys));

        RunOnUiThread(() => ApplyChanges(changes));
    }

    private void ApplyChanges(ItemChanges changes)
    {
        _changes = changes;
        foreach (var section in Sections)
        {
            section.Change = changes.Sections.TryGetValue(section.Key, out var slot) ? slot : ChangeSlot.Empty;
        }

        OnPropertyChanged(nameof(HasUnreadChanges));
        OnPropertyChanged(nameof(NameChange));
        OnPropertyChanged(nameof(VariationsChange));
        OnPropertyChanged(nameof(GalleryChange));
        OnPropertyChanged(nameof(SaleChange));
        OnPropertyChanged(nameof(DescriptionChange));
        OnPropertyChanged(nameof(OtherChangesText));
        OnPropertyChanged(nameof(HasOtherChanges));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task MarkChangesReadAsync()
    {
        var ids = _changes.NotificationIds;
        if (ids.Count == 0 || _markingChangesRead)
        {
            return;
        }

        _markingChangesRead = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            // 要確認の画面の「この束を既読にする」と同じ命令。保存を待ってから数え直す（待たずに数えるとナビの数が1つ古いまま残った）
            if (await _services.Commands.ExecuteAsync(new UiCommand.MarkNotificationsRead(ids)) is CommandResult.Failed failed)
            {
                Services.Notice.Show(failed.Message, "既読にする",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ApplyChanges(ItemChanges.None);
            _main.RefreshBadges();
        }
        finally
        {
            _markingChangesRead = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
