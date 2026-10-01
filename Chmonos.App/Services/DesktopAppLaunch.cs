using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Chmonos.App.Services;

/// <summary>ほかのアプリ（VCC・ALCOM）を開こうとした結果。文言は呼ぶ側で作る。</summary>
public enum AppOpenResult
{
    /// <summary>閉じていたので起動した。</summary>
    Launched,

    /// <summary>既に開いていたので手前に出した。</summary>
    BroughtToFront,

    /// <summary>既に開いているが、Windows に手前へ出すのを断られた。</summary>
    AlreadyOpenNotFront,

    /// <summary>入っていない（どの記録にも場所が無い）。</summary>
    NotInstalled,

    /// <summary>起動そのものに失敗した。</summary>
    Failed,
}

/// <summary>
/// ほかのアプリを起動するか、開いていれば手前に出す。VCC と ALCOM で同じ作法にするため1か所に置く。
/// **起動するだけで、そのアプリの中身（設定・プロジェクト）には触らない。**
/// </summary>
internal static class DesktopAppLaunch
{
    /// <summary><c>SW_RESTORE</c>。最小化されていても出す。</summary>
    private const int Restore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// 開く。既に開いていれば手前に出す。
    ///
    /// **手前に出すのはこちらの仕事。**VCC は2回目の起動で既存の窓へ知らせて自分は終わるだけで、
    /// 最小化した窓は最小化のまま戻らなかった（unity-handoff §12-2）。ALCOM は自分で戻す作りだが（alcom.md §2-1）、
    /// 同じ作法で先に探して出せば、どちらでも押して何も起きないようには見えない。
    /// </summary>
    /// <param name="exe">見つけた実行ファイル。無ければ null（起動中なら手前に出すだけはできる）。</param>
    /// <param name="processName">そのアプリのプロセスの名前。見つけた実行ファイルの名前でも見る（別の名前で入っていることもある）。</param>
    public static AppOpenResult Open(string? exe, string processName)
    {
        foreach (var name in ProcessNames(exe, processName))
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero)
                        {
                            continue;
                        }

                        ShowWindow(window, Restore);
                        return SetForegroundWindow(window)
                            ? AppOpenResult.BroughtToFront
                            : AppOpenResult.AlreadyOpenNotFront;
                    }
                }
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 数えられなくても起動は試せる
            }
        }

        if (exe is null)
        {
            return AppOpenResult.NotInstalled;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return AppOpenResult.Launched;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return AppOpenResult.Failed;
        }
    }

    /// <summary>今起動しているか。入れ先が記録に無くても、動いていれば手前に出せる。</summary>
    public static bool IsRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool Exists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ProcessNames(string? exe, string processName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { processName };
        if (exe is not null)
        {
            names.Add(Path.GetFileNameWithoutExtension(exe));
        }

        return names;
    }
}
