using System.IO;

namespace Chmonos.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// 起動したときに、前の起動で途中で止まった操作（IDの変更・タグや属性の名前の変更）の続きを済ませる（ユーザ判断 2026-10-06「A」）。
    ///
    /// 途中で止まると、一部の商品や参照だけが新しい名前（ID）を指したまま残る。人が押した操作の続きで、BOOTHへは問い合わせないので、
    /// 設定「起動したとき、裏で取得を始める」を切っていても行う（その設定で止まる裏の作業の段には入れない）。
    /// 何か済ませたら、検索などが読み込んだ時の名前で組んであるので組み直す。続けられなかった物は通知に出る。
    /// </summary>
    private void ResumePendingOperations()
        => Task.Run(async () =>
        {
            try
            {
                var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ResumePendingOperations());
                if (result is Core.Commands.CommandResult.Counted { Count: > 0 })
                {
                    RunOnUiThread(() =>
                    {
                        RefreshCounts();
                        ReloadLibraryAsync().Forget();
                    });
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：途中で止まった操作の続き", exception);
            }
        }).Forget();
}
