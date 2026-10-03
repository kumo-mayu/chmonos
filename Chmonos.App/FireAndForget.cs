using System.IO;
using System.Runtime.CompilerServices;
using Chmonos.Core.Commands;
using Chmonos.Core.Diagnostics;

namespace Chmonos.App;

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
    private static int s_pending;

    /// <summary>
    /// 投げて、まだ済んでいない作業の数。**数えるだけで、アプリの動きには使わない。**
    /// 試験が「投げっぱなしの読み込み・保存が済んだ」を待つのに使う（2026-09-30）。待たずに試験を終えると、
    /// 前の試験の作業が次の試験の最中に落ちて、次の試験のログに混ざった。
    /// **Core が裏へ投げた作業（<see cref="BackgroundWork"/>）も足す。**足していなかった間は、確定の後の検出が
    /// 試験の保存先を消した後も走り続け、一時ファイルを消されて落ちていた（2026-10-03）
    /// </summary>
    internal static int Pending => Volatile.Read(ref s_pending) + BackgroundWork.Pending;

    public static void Forget(
        this Task task,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var where = Where(file, member);
        Interlocked.Increment(ref s_pending);
        _ = task.ContinueWith(
            finished =>
            {
                try
                {
                    // 取り消しは失敗として残さない（画面を離れて読み込みをやめた、など）
                    if (finished.IsFaulted)
                    {
                        AppLog.Error(where, finished.Exception!.GetBaseException());
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref s_pending);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void Forget(
        this Task<CommandResult> task,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var where = Where(file, member);
        Interlocked.Increment(ref s_pending);
        _ = task.ContinueWith(
            finished =>
            {
                try
                {
                    if (finished.IsFaulted)
                    {
                        AppLog.Error(where, finished.Exception!.GetBaseException());
                    }
                    else if (finished.IsCompletedSuccessfully && finished.Result is CommandResult.Failed failed)
                    {
                        AppLog.Warn(where, failed.Message);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref s_pending);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string Where(string file, string member) => $"{Path.GetFileNameWithoutExtension(file)}.{member}";
}
