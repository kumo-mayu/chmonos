namespace Chmonos.Core.Diagnostics;

/// <summary>
/// Core が待たずに裏へ投げる作業（確定の後の対応アバターの検出・unitypackage の読み込み・まとめた検出の続き）の入口。
/// 失敗はログに残し、**済んでいない数を数える**。
///
/// 前はそれぞれが <c>_ = Task.Run(...)</c> で投げていて、数えていなかった。App の試験は「投げた作業が全部済んだ」
/// （<c>FireAndForget.Pending</c>）を見てから保存先のフォルダを消すが、これらは数に入らないので、消した後も検出が走り続け、
/// 書きかけの一時ファイルごと消されて「見つからない」で落ちていた（2026-10-03。一式を5回回して2回、次の試験のログに混ざって落ちた）。
/// アプリの中では保存先は消されないので本番の保存は壊れないが、どこで何が走っているか分からない作業を残さないため、1か所に集める。
/// </summary>
public static class BackgroundWork
{
    private static int s_pending;

    /// <summary>投げて、まだ済んでいない作業の数。**数えるだけで、アプリの動きには使わない**（試験が済むのを待つのに使う）。</summary>
    public static int Pending => Volatile.Read(ref s_pending);

    /// <summary>
    /// 裏のスレッドで走らせる。待つ者がいないので、中断は黙って終え、それ以外の失敗は <paramref name="what"/> の名でログに残す。
    /// </summary>
    public static void Run(string what, Func<Task> work)
    {
        Interlocked.Increment(ref s_pending);
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
                // 止めたのは走らせた側の都合（閉じる・保存先を運ぶ）。失敗ではない
            }
            catch (Exception exception)
            {
                // 壊れた zip の読み取りは IO と JSON 以外の例外（InvalidDataException など）も投げるので、種類で絞らない
                AppLog.Error(what, exception);
            }
            finally
            {
                Interlocked.Decrement(ref s_pending);
            }
        });
    }
}
