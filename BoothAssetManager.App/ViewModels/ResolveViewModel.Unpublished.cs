using BoothAssetManager.Core.Commands;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面：BOOTHで見つからなかった商品IDのまま登録する（ユーザ判断 2026-09-29）。
///
/// 商品IDは分かっている（zipの名前・購入履歴・ショップのページから）のにBOOTHで見つからない商品は、今までは仮ID（BOOTHに無い商品）で
/// 登録するしかなく、再び公開されても情報を取れなかった。確かめた結果が「BOOTHに無い」だったときだけ（一時的に届かないときは出さない）、
/// そのIDのまま「BOOTHで公開されていない」商品として登録する道を出す。名前はファイル名から作る（編集画面で変えられる）。
/// ⑦（期限の来た商品の取り直し）で確かめ直し、公開されたら情報を取って要確認に知らせる（Core の <c>AssignUnpublishedItemIdAsync</c>）。
/// </summary>
public sealed partial class ResolveViewModel
{
    private string? _notOnBoothItemId;

    /// <summary>確かめた結果、BOOTHに無かった商品ID。確かめ直す・選び直すと消える。</summary>
    public string? NotOnBoothItemId
    {
        get => _notOnBoothItemId;
        private set
        {
            if (SetField(ref _notOnBoothItemId, value))
            {
                OnPropertyChanged(nameof(HasNotOnBoothItemId));
                OnPropertyChanged(nameof(UnpublishedButtonText));
                OnPropertyChanged(nameof(UnpublishedNameDraft));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasNotOnBoothItemId => NotOnBoothItemId is not null;

    public string UnpublishedButtonText => $"ID {NotOnBoothItemId} のまま登録する";

    /// <summary>登録するときの名前。元zipが分かれば、中の1ファイルの名前より商品名に近い（「BOOTHに無い商品」の下書きと同じ作り方）。</summary>
    public string UnpublishedNameDraft => Selected is null
        ? string.Empty
        : Core.Resolution.FileNameQuery.ToNameDraft(Selected.Origin?.ArchiveName ?? Selected.FileName);

    public RelayCommand AssignUnpublishedCommand => _assignUnpublishedCommand ??= new RelayCommand(
        () => AssignUnpublishedAsync().Forget(),
        () => HasNotOnBoothItemId && HasSelection && !IsBusy && !IsBlockedByListedZip);

    private RelayCommand? _assignUnpublishedCommand;

    private async Task AssignUnpublishedAsync()
    {
        if (Selected is null || NotOnBoothItemId is not { } itemId)
        {
            return;
        }

        // 束を選んでいればその全件（1zip＝1商品）。1件目で商品を作り、残りはその商品へ加える（BOOTHへは行かない）
        var targets = ActiveRows;
        var name = UnpublishedNameDraft;
        var what = targets.Count == 1 ? Selected.FileName : GroupSubject;
        var answer = Services.Notice.Show(
            $"{what} を商品ID {itemId} として登録します。\n\n"
            + $"BOOTHで公開されていない商品として、名前「{name}」で登録します。名前は編集画面で変えられます。\n\n"
            + "BOOTHで公開されたら情報を取得し、要確認でお知らせします。",
            "このIDのまま登録する",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        StartRegistering(RegisteringArea.Decision, targets.Count);
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.AssignUnpublishedItemId(targets[0].File.Hash, itemId, name));
            StepRegistering(1);

            if (result is not CommandResult.ItemSaved)
            {
                StatusText = result is CommandResult.Failed failed ? failed.Message : string.Empty;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            // 確定と同じ扱いで溜める（まとめて編集へ送れる・離れるときに検索へ反映する）
            if (!_settledItemIds.Contains(itemId))
            {
                _settledItemIds.Add(itemId);
            }

            if (targets.Count == 1)
            {
                AfterSettled();
                return;
            }

            var settled = new List<UnresolvedRow> { targets[0] };
            var done = 1;
            foreach (var row in targets.Skip(1))
            {
                var assigned = await _services.Commands.ExecuteAsync(new UiCommand.AssignItemId(row.File.Hash, itemId));
                StepRegistering(++done);
                if (assigned is not CommandResult.Failed)
                {
                    settled.Add(row);
                }
            }

            RemoveRows(settled);
            StatusText = settled.Count == targets.Count
                ? $"{settled.Count} 件を登録しました。"
                : $"{settled.Count} / {targets.Count} 件を登録しました。残りは失敗しました。";
            OnPropertyChanged(nameof(HasStatus));
            HideCoveredContents(settled);
        }
        finally
        {
            EndRegistering();
            IsBusy = false;
        }
    }
}
