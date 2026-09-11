using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>プロジェクトを開こうとした結果。文言は呼ぶ側で作る。</summary>
public enum UnityOpenResult
{
    /// <summary>既に開いていたので手前に出した。</summary>
    BroughtToFront,

    /// <summary>
    /// 既に開いているが、手前に出せなかった。Windows は前面を持っていないプロセスからの
    /// 切り替えを断ることがある（最小化中など）。
    /// </summary>
    AlreadyOpenNotFront,

    /// <summary>
    /// プロジェクトは開いている印があるが、どの窓か分からない（起動中・コンパイル中で題が読めない）。
    /// 起動し直すと弾かれる（exit 21）ので、何もしない。
    /// </summary>
    AlreadyOpenUnknownWindow,

    /// <summary>そのバージョンのエディタで開いた。</summary>
    Launched,

    /// <summary>そのバージョンが入っていないのでUnity Hubに渡した。</summary>
    HandedToHub,

    /// <summary>フォルダが見つからない。</summary>
    Missing,

    /// <summary>起動そのものに失敗した。</summary>
    Failed,
}

/// <summary>
/// Unityプロジェクトを開く。
///
/// **3通りに言い分ける**（<c>設計詳細_改変の記録.md</c> の4-5）。
/// 既に開いているものに <c>-projectPath</c> を投げると弾かれる（exit 21）ので、
/// そのときは窓を手前に出すのが正しい応答になる。
/// 入っていないバージョンをこちらで入れにはいかない——Hubの仕事に渡す。
/// </summary>
public static class UnityLaunch
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary><c>SW_RESTORE</c>。最小化されていても出す。</summary>
    private const int Restore = 9;

    /// <summary>
    /// Unity Hub が入れたエディタの置き場所。
    ///
    /// 既定は <c>%PROGRAMFILES%\Unity\Hub\Editor</c>。移した人は
    /// <c>secondaryInstallPath.json</c> に書かれている（空文字なら既定のまま）。
    /// </summary>
    private static IEnumerable<string> EditorRoots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity",
            "Hub",
            "Editor");

        var secondary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UnityHub",
            "secondaryInstallPath.json");

        string? moved = null;
        try
        {
            if (File.Exists(secondary))
            {
                moved = File.ReadAllText(secondary).Trim().Trim('"');
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (!string.IsNullOrWhiteSpace(moved))
        {
            yield return moved;
        }
    }

    /// <summary>そのバージョンのエディタの実行ファイル。無ければ null。</summary>
    public static string? FindEditor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        foreach (var root in EditorRoots())
        {
            var exe = Path.Combine(root, version, "Editor", "Unity.exe");
            try
            {
                if (File.Exists(exe))
                {
                    return exe;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// そのプロジェクトを開いているエディタを探す。
    ///
    /// 窓のタイトルの先頭がプロジェクト名になる。**フォルダ名で照合する**——
    /// タイトルに出るのはフォルダ名で、Hubの表示名ではない。
    /// </summary>
    private static Process? FindOpenEditor(string projectPath)
    {
        var name = Path.GetFileName(projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("Unity"))
            {
                var title = UnityHandoff.ProjectNameFromWindowTitle(process.MainWindowTitle);
                if (string.Equals(title, name, StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }

                process.Dispose();
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        return null;
    }

    /// <summary>
    /// プロジェクトを開く。既に開いていれば手前に出す。
    /// </summary>
    public static UnityOpenResult OpenProject(string? projectPath, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return UnityOpenResult.Missing;
        }

        if (FindOpenEditor(projectPath) is { } running)
        {
            using (running)
            {
                var window = running.MainWindowHandle;
                if (window != IntPtr.Zero)
                {
                    ShowWindow(window, Restore);

                    // 断られても「手前に出しました」と言っていた。戻り値を見て言い分ける
                    return SetForegroundWindow(window)
                        ? UnityOpenResult.BroughtToFront
                        : UnityOpenResult.AlreadyOpenNotFront;
                }
            }
        }

        // 窓の題から見つからなくても、開いている印（Temp/UnityLockfile）があれば開いている。
        // 起動中・コンパイル中は題が作業の名前になっていて読めない。ここで起動し直すと
        // 同じプロジェクトは開けずに弾かれる（exit 21）のに「開いています」と言ってしまう
        if (UnityProjects.IsProjectOpen(projectPath))
        {
            return UnityOpenResult.AlreadyOpenUnknownWindow;
        }

        // 一覧の値は古くなることがあるので、プロジェクト自身の記録を優先する
        var wanted = version;
        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        try
        {
            if (File.Exists(versionFile))
            {
                wanted = UnityProjects.VersionFromProjectVersionText(File.ReadAllText(versionFile))
                    ?? version;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (FindEditor(wanted) is { } exe)
        {
            return Start(new ProcessStartInfo(exe)
            {
                // 引用符を付けるのは、パスに空白が入るのが普通だから
                Arguments = $"-projectPath \"{projectPath}\"",
                UseShellExecute = true,
            })
                ? UnityOpenResult.Launched
                : UnityOpenResult.Failed;
        }

        // 入れる面倒はHubに渡す。バージョンを添えると、そのバージョンの話として開く
        var link = string.IsNullOrWhiteSpace(wanted) ? "unityhub://" : $"unityhub://{wanted}";
        return Start(new ProcessStartInfo(link) { UseShellExecute = true })
            ? UnityOpenResult.HandedToHub
            : UnityOpenResult.Failed;
    }

    private static bool Start(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
