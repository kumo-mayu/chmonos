using System.IO;

namespace Chmonos.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// 起動したときに、読めない商品の記録を数えて通知に出す（ユーザ判断 2026-10-05）。
    ///
    /// 読めない記録は全件の読み込みが飛ばすので、その商品は黙って検索から消えていた。
    /// BOOTH に問い合わせないので、設定「起動したとき、裏で取得を始める」を切っていても見る
    /// （その設定で止まる裏の作業の段には入れない）。全件の読み込みは写しを使うので、検索の読み込みと重ねても軽い。
    /// </summary>
    private void CheckUnreadableItems()
        => Task.Run(async () =>
        {
            try
            {
                var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.DetectUnreadableItems());
                if (result is Core.Commands.CommandResult.Counted { Count: > 0 })
                {
                    RunOnUiThread(RefreshCounts);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：読めない商品の記録の確認", exception);
            }
        }).Forget();
}
