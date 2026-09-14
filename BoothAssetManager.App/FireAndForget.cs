using System.IO;
using System.Runtime.CompilerServices;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Diagnostics;

namespace BoothAssetManager.App;

/// <summary>
/// 待たない作業（押した後に画面を止めない保存・裏の読み込みなど）の失敗を、その場でログに残す（技術的負債 2-2、2026-09-14）。
///
/// 前は <c>_ = 〜Async()</c> と書いて投げっぱなしにしていて、中で落ちても誰も気付かなかった
/// （アプリ全体の受け口 <c>UnobservedTaskException</c> は、作業が片付けられるときにしか呼ばれず、遅れて出る）。
/// **どこで投げたか**は呼んだファイルとメソッドの名前で自動で残す。
/// コマンドが「できなかった」（<see cref="CommandResult.Failed"/>）を返したときも、画面が受け取らないので残す。
/// </summary>
public static class FireAndForget
{
    public static void Forget(
        this Task task,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var where = Where(file, member);
        _ = task.ContinueWith(
            finished => AppLog.Error(where, finished.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void Forget(
        this Task<CommandResult> task,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var where = Where(file, member);
        _ = task.ContinueWith(
            finished =>
            {
                if (finished.IsFaulted)
                {
                    AppLog.Error(where, finished.Exception!.GetBaseException());
                }
                else if (finished.IsCompletedSuccessfully && finished.Result is CommandResult.Failed failed)
                {
                    AppLog.Warn(where, failed.Message);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string Where(string file, string member) => $"{Path.GetFileNameWithoutExtension(file)}.{member}";
}
