using Chmonos.App.Services;
using Chmonos.Core.Commands;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 主画面：ファイルを持たない「BOOTHに無い商品」を名前だけで作る（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 前は未確定のファイルを選び「BOOTHに無い商品として登録する」を押す道しか無く、ファイルが無いと作れなかった。
/// 入口は検索の件数の帯。作ったらその商品ページを開く（ファイルの欄の「追加…」でそのまま付けられる）
/// </summary>
public sealed partial class MainViewModel
{
    private RelayCommand? _createLocalItemCommand;
    private bool _isCreatingLocalItem;

    public RelayCommand CreateLocalItemCommand
        => _createLocalItemCommand ??= new RelayCommand(() => CreateLocalItemAsync().Forget(), () => !_isCreatingLocalItem);

    /// <summary>名前を窓で聞いて作り、検索の写しへ1件足して、その商品ページを開く。窓を閉じたら何もしない。</summary>
    internal async Task CreateLocalItemAsync()
    {
        var model = new LocalItemNameDialogViewModel();
        if (!LocalItemNameDialogViewModel.Ask(model))
        {
            return;
        }

        _isCreatingLocalItem = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            CommandResult result;
            try
            {
                result = await _services.Commands.ExecuteAsync(new UiCommand.CreateLocalItem(model.Name.Trim()));
            }
            catch (Exception exception)
            {
                // 押した操作の結果が出ないまま黙るのを避ける（書き込めなかった等。落とした URL の登録と同じ扱い）
                Core.Diagnostics.AppLog.Error("BOOTHに無い商品の登録", exception);
                Notice.Show(Core.Services.FailureText.Cause(exception), "登録できませんでした", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (result is CommandResult.Failed failure)
            {
                Notice.Show(failure.Message, "登録できませんでした", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 自分が押して、作った物を見に行く操作なので、その場で商品ページへ移る（落とした URL の登録と違い、待ち時間が無い）。
            // 全件を読み直さず、検索の写しへ1件だけ足す（未確定の登録・落とした URL と同じ足し方）
            if (result is CommandResult.ItemSaved saved && await _services.Store.Items.LoadAsync(saved.ItemId) is { } added)
            {
                Search.NoteItemSaved(added);
                RefreshCounts();
                ShowItem(added);
            }
        }
        finally
        {
            _isCreatingLocalItem = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
