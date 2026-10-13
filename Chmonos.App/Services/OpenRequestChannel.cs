using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// 2本目の起動から、動いている方へ「窓を開いて」と頼む（docs/history/headless-core-2026-10-13.md 段4）。
///
/// 前は2本目が「既に起動しています」と出して終わるだけで、画面無しで動いている方の窓は通知領域から開くしかなかった。
/// 合図は保存先ごとの名前付きのイベント（保存先が違えば別のアプリなので、写しで並べて起動しても混ざらない）。
/// 名前は <c>Local\</c>（サインインしている人ごと）に置く。受けた側は「受けた」を返し、返りが無ければ（合図を待たない古い版）2本目は今までどおり知らせる
/// </summary>
internal sealed class OpenRequestChannel : IDisposable
{
    private readonly EventWaitHandle _open;
    private readonly EventWaitHandle _opened;
    private readonly RegisteredWaitHandle _registration;

    private OpenRequestChannel(EventWaitHandle open, EventWaitHandle opened, Action onRequest)
    {
        _open = open;
        _opened = opened;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            open,
            (_, _) =>
            {
                onRequest();
                _opened.Set();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>頼みを待ち始める（多重起動の錠を取った方が呼ぶ）。<paramref name="onRequest"/> は裏のスレッドで呼ばれる。</summary>
    public static OpenRequestChannel Listen(AppPaths paths, Action onRequest)
        => new(
            new EventWaitHandle(false, EventResetMode.AutoReset, NameOf(paths, "open")),
            new EventWaitHandle(false, EventResetMode.AutoReset, NameOf(paths, "opened")),
            onRequest);

    /// <summary>
    /// 動いている方へ頼む。受けたら true。待つ方がいない・返りが無ければ false。
    /// 前に出す許しを先に渡す（Windows は、今前にいるアプリ＝人が起動したこちらからの許しが無いと、ほかのアプリの窓を前に出させない）
    /// </summary>
    public static bool Request(AppPaths paths, TimeSpan wait)
    {
        try
        {
            using var open = EventWaitHandle.OpenExisting(NameOf(paths, "open"));
            using var opened = EventWaitHandle.OpenExisting(NameOf(paths, "opened"));
            AllowSetForegroundWindow(AsfwAny);
            open.Set();
            return opened.WaitOne(wait);
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>保存先の場所から決める名前（大文字小文字と末尾の区切りの違いでは変えない）。</summary>
    internal static string NameOf(AppPaths paths, string suffix)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.Root)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)), 0, 12);
        return $@"Local\Chmonos.{hash}.{suffix}";
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _open.Dispose();
        _opened.Dispose();
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
