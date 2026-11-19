using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>この商品に BOOTH での変化の記録があるか。保管庫は裏で目録を作るまで分からない。</summary>
public enum ChangeHistoryAvailability
{
    /// <summary>保管庫を確かめている（今の知らせには無い）。</summary>
    Checking,

    None,

    Present,
}

/// <summary>
/// 商品ページ：「BOOTHでの変化の履歴」の窓を開く（ユーザ判断 2026-11-20）。履歴が無い商品では押せなくする。
///
/// **押せるかは開くときに同期で決めない**：今の知らせ（<c>notifications.json</c>。上限200件で控えあり）は開くときに読んでいる物で足りるが、
/// 保管庫は月ごとの gzip で、目録を初めて作るときは 50MB の月で約1秒かかる。今の知らせに無く保管庫に月があるときだけ、
/// 裏で目録（<see cref="Core.Storage.NotificationArchive.CatalogAsync"/>。変わっていない月は控えを使い、同時の頼みは1本にまとめる）を作り、
/// 分かった所で押せるようにする。確かめている間は「無い」と言わずに押せないままにし、吹き出しで確かめていることを言う
/// </summary>
public sealed partial class ItemViewModel
{
    private ChangeHistoryAvailability _changeHistory = ChangeHistoryAvailability.None;
    private int _changeHistoryCheck;
    private bool _openingChangeHistory;
    private RelayCommand? _showChangeHistory;

    public ChangeHistoryAvailability ChangeHistory
    {
        get => _changeHistory;
        private set
        {
            if (SetField(ref _changeHistory, value))
            {
                OnPropertyChanged(nameof(ChangeHistoryTip));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ChangeHistoryTip => _changeHistory switch
    {
        ChangeHistoryAvailability.Checking => "保管した通知を確かめています。",
        ChangeHistoryAvailability.None => "この商品には、BOOTHでの変化の記録がありません。",
        _ => "BOOTHで商品ページが変わった記録を、新しい順に表示します。",
    };

    public RelayCommand ShowChangeHistoryCommand => _showChangeHistory ??= new RelayCommand(
        () => ShowChangeHistoryAsync().Forget(),
        () => _changeHistory == ChangeHistoryAvailability.Present && !_openingChangeHistory);

    private void CheckChangeHistory(IReadOnlyList<NotificationRecord> current)
    {
        var itemId = Item.Id;
        var check = ++_changeHistoryCheck;
        if (current.Any(record => ItemChangeHistory.IsChangeOf(record, itemId)))
        {
            ChangeHistory = ChangeHistoryAvailability.Present;
            return;
        }

        // 月が1つも無ければ目録を作るまでもない（保管を使っていない・まだ溜まっていない人がほとんど）
        var archive = _services.Store.NotificationArchive;
        if (archive.Months().Count == 0)
        {
            ChangeHistory = ChangeHistoryAvailability.None;
            return;
        }

        ChangeHistory = ChangeHistoryAvailability.Checking;
        CheckArchiveAsync(itemId, check).Forget();
    }

    private async Task CheckArchiveAsync(string itemId, int check)
    {
        var found = false;
        try
        {
            var catalog = await _services.Store.NotificationArchive.CatalogAsync();
            found = ItemChangeHistory.ArchivedOf(catalog, itemId).Count > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // 読めない保管庫は「無い」として扱う（押せても中身を出せない）。理由はログに残す
            Core.Diagnostics.AppLog.Warn("変化の履歴があるかを確かめる", exception.Message);
        }

        // 確かめている間に商品が替わった・読み直した物は捨てる（後の確かめが正）
        if (check == _changeHistoryCheck)
        {
            ChangeHistory = found ? ChangeHistoryAvailability.Present : ChangeHistoryAvailability.None;
        }
    }

    private async Task ShowChangeHistoryAsync()
    {
        if (_openingChangeHistory)
        {
            return;
        }

        _openingChangeHistory = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            var itemId = Item.Id;
            var current = _services.Notifications.Load();
            var archive = _services.Store.NotificationArchive;
            ItemChangeHistoryRead read;
            try
            {
                read = await Task.Run(() => ItemChangeHistory.ReadAsync(current, archive, itemId));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // 保管庫を読めなくても、今の知らせの分は出す。欠けていることは窓の上で言う
                Core.Diagnostics.AppLog.Warn("変化の履歴を読む", exception.Message);
                read = new ItemChangeHistoryRead(ItemChangeHistory.Merge(current, [], itemId), HasDamage: true);
            }

            await ChangeHistoryWindow.ShowAsync(new ChangeHistoryViewModel(Name, read));
        }
        finally
        {
            _openingChangeHistory = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
