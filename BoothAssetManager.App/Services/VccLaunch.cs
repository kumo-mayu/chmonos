using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BoothAssetManager.App.Services;

/// <summary>VCC を開こうとした結果。文言は呼ぶ側で作る。</summary>
public enum VccOpenResult
{
    /// <summary>閉じていたので起動した。</summary>
    Launched,

    /// <summary>既に開いていたので手前に出した。</summary>
    BroughtToFront,

    /// <summary>既に開いているが、Windows に手前へ出すのを断られた。</summary>
    AlreadyOpenNotFront,

    /// <summary>入っていない（アンインストール情報に無い）。</summary>
    NotInstalled,

    /// <summary>起動そのものに失敗した。</summary>
    Failed,
}

/// <summary>
/// VRChat Creator Companion を開く。**起動するだけで、中身（設定・プロジェクト・VPM）には触らない。**
///
/// 裏付けは <c>設計詳細_Unityへの受け渡し.md</c> §12（2026-09-13 実機で確かめた）。
/// </summary>
public static class VccLaunch
{
    private const string ProcessName = "CreatorCompanion";

    private const string ExeName = "CreatorCompanion.exe";

    /// <summary><c>SW_RESTORE</c>。最小化されていても出す。</summary>
    private const int Restore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// 実行ファイルの場所。**決め打ちせず、アンインストール情報の <c>InstallLocation</c> から引く**
    /// （利用者ごとの場所 <c>%LOCALAPPDATA%\Programs\…</c> に入る）。無ければ null。
    /// </summary>
    public static string? FindExe()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in new[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            })
            {
                using var root = hive.OpenSubKey(path);
                if (root is null)
                {
                    continue;
                }

                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is string display
                        && display.StartsWith("VRChat Creator Companion", StringComparison.OrdinalIgnoreCase)
                        && key.GetValue("InstallLocation") is string location
                        && location.Length > 0)
                    {
                        var exe = Path.Combine(location, ExeName);
                        if (File.Exists(exe))
                        {
                            return exe;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 開く。既に開いていれば手前に出す。
    ///
    /// **手前に出すのはこちらの仕事。**2回目の起動は既存の窓へ知らせて自分は終わるだけで、
    /// 最小化した窓は最小化のまま戻らなかった（§12-2）。押しても何も起きなかったように見えるので、先に探して出す。
    /// </summary>
    public static VccOpenResult Open()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(ProcessName))
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
                        ? VccOpenResult.BroughtToFront
                        : VccOpenResult.AlreadyOpenNotFront;
                }
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 数えられなくても起動は試せる
        }

        if (FindExe() is not { } exe)
        {
            return VccOpenResult.NotInstalled;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return VccOpenResult.Launched;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return VccOpenResult.Failed;
        }
    }
}
