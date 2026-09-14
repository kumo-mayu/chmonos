using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BoothAssetManager.Core.Services;

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

    /// <summary>入っていない（どの記録にも場所が無い）。</summary>
    NotInstalled,

    /// <summary>起動そのものに失敗した。</summary>
    Failed,
}

/// <summary>
/// VRChat Creator Companion を開く。**起動するだけで、中身（設定・プロジェクト・VPM）には触らない。**
///
/// 裏付けは <c>docs/history/unity-handoff.md</c> §12（2026-09-13 実機で確かめた）。
/// </summary>
public static class VccLaunch
{
    /// <summary>VCC の実行ファイルとプロセスの名前（手元の 2.4.5）。</summary>
    private const string ProcessName = "CreatorCompanion";

    private const string ExeName = "CreatorCompanion.exe";

    /// <summary><c>SW_RESTORE</c>。最小化されていても出す。</summary>
    private const int Restore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// 実行ファイルの場所。**決め打ちせず、Windows に残る記録から引く**（利用者ごとの場所に入る物なので・ユーザ指示）。無ければ null。
    ///
    /// 1. アンインストール情報の <c>InstallLocation</c>（手元ではここにある）
    /// 2. 同じ項目のアイコンの欄
    /// 3. <c>vcc://</c> の関連付け（VCC はリポジトリを足すリンクのために登録する）。VCC の代わりのツールがこのリンクを
    ///    引き受けていればそちらが開く——利用者が VCC の代わりに選んだものなので、それでよい
    /// </summary>
    public static string? FindExe()
    {
        foreach (var entry in InstalledApps.Uninstall()
            .Where(entry => entry.DisplayName.StartsWith("VRChat Creator Companion", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(entry.InstallLocation)
                && Path.Combine(entry.InstallLocation, ExeName) is var inFolder && Exists(inFolder))
            {
                return inFolder;
            }

            if (UnityEditorLocator.ExeFromCommand(entry.DisplayIcon) is { } icon && Exists(icon))
            {
                return icon;
            }
        }

        return UnityEditorLocator.ExeFromCommand(InstalledApps.OpenCommand("vcc")) is { } linked && Exists(linked)
            ? linked
            : null;
    }

    /// <summary>
    /// 開く。既に開いていれば手前に出す。
    ///
    /// **手前に出すのはこちらの仕事。**2回目の起動は既存の窓へ知らせて自分は終わるだけで、
    /// 最小化した窓は最小化のまま戻らなかった（§12-2）。押しても何も起きなかったように見えるので、先に探して出す。
    /// </summary>
    public static VccOpenResult Open()
    {
        var exe = FindExe();

        // 起動中かは、見つけた実行ファイルの名前でも見る（リンクの引き受け手が別の名前のこともある）
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ProcessName };
        if (exe is not null)
        {
            names.Add(Path.GetFileNameWithoutExtension(exe));
        }

        foreach (var name in names)
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
        }

        if (exe is null)
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

    /// <summary>
    /// 開けるか。入っている（場所が分かる）か、今起動している。「VCCを開く」を押せる状態にするかに使う
    /// （押してから「見つかりませんでした」と言うより、押す前に分かる方がよい・ユーザ判断 2026-09-13）。
    /// </summary>
    public static bool IsAvailable() => FindExe() is not null || IsRunning();

    private static bool IsRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
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

    private static bool Exists(string path)
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
}
