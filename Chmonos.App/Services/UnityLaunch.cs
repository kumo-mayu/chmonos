using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

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

    /// <summary>バージョンが読めなかったので、バージョンを添えずに Unity Hub を開いた。</summary>
    HandedToHubWithoutVersion,

    /// <summary>そのバージョンが入っておらず、渡す先の Unity Hub も入っていない。</summary>
    NoEditorNoHub,

    /// <summary>フォルダが見つからない。</summary>
    Missing,

    /// <summary>起動そのものに失敗した。</summary>
    Failed,
}

/// <summary>
/// Unityプロジェクトを開く。
///
/// **3通りに言い分ける**（<c>docs/history/modifications.md</c> の4-5）。
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

    /// <summary>そのバージョンのエディタの実行ファイル。無ければ null（そのときは Hub に渡す）。</summary>
    public static string? FindEditor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        foreach (var (_, exe) in EditorCandidates(version))
        {
            if (Exists(exe))
            {
                return exe;
            }
        }

        return null;
    }

    /// <summary>
    /// そのバージョンのエディタがありそうな場所を、記録ごとに順に出す（どの記録から来たかも添える）。
    ///
    /// **利用者の PC の置き場所に頼らない**（ユーザ指示 2026-09-13：配布するので）。エディタは Hub の既定の場所に入っているとは
    /// 限らない——Hub で置き場所を変えた人、Hub を使わずに入れた人、Hub の「場所を指定」で足した人がいる。どれも Windows か
    /// Hub のどこかに場所が残るので、全部を見る：
    ///
    /// 1. Hub の置き場所（既定と <c>secondaryInstallPath.json</c>）
    /// 2. Hub の「場所を指定」で足した一覧（<c>editors-v2.json</c>・古い版は <c>editors.json</c>）
    /// 3. Unity の登録（<c>Software\Unity Technologies\Installer\Unity &lt;版&gt;</c> の <c>Location x64</c>）
    /// 4. アンインストール情報の「Unity &lt;版&gt;」（アイコンの欄・場所の欄）
    /// 5. 起動中のエディタ、6. <c>.unitypackage</c> の関連付け——この2つは場所だけで版が分からないので、実行ファイルの版の欄で見分ける
    /// </summary>
    internal static IEnumerable<(string Source, string Path)> EditorCandidates(string version)
    {
        foreach (var root in EditorRoots())
        {
            yield return ("Hubの置き場所", Path.Combine(root, version, "Editor", "Unity.exe"));
        }

        foreach (var file in new[] { "editors-v2.json", "editors.json" })
        {
            if (ReadText(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", file)) is not { } json)
            {
                continue;
            }

            foreach (var entry in UnityEditorLocator.EditorsFromHubJson(json)
                .Where(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase)))
            {
                yield return ($"Hubの一覧（{file}）", entry.ExePath);
            }
        }

        foreach (var location in InstalledApps.UnityInstallerLocations(version))
        {
            yield return ("Unityの登録", UnityEditorLocator.ExeFromLocation(location));
        }

        foreach (var entry in InstalledApps.Uninstall()
            .Where(entry => UnityEditorLocator.VersionFromUninstallName(entry.DisplayName) == version))
        {
            if (UnityEditorLocator.ExeFromCommand(entry.DisplayIcon) is { } icon)
            {
                yield return ("アンインストール情報", icon);
            }

            if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                yield return ("アンインストール情報", UnityEditorLocator.ExeFromLocation(entry.InstallLocation));
            }
        }

        foreach (var running in RunningEditorPaths().Where(path => HasVersion(path, version)))
        {
            yield return ("起動中のエディタ", running);
        }

        if (UnityEditorLocator.ExeFromCommand(InstalledApps.FileAssociationCommand(".unitypackage")) is { } associated
            && HasVersion(associated, version))
        {
            yield return (".unitypackageの関連付け", associated);
        }
    }

    /// <summary>起動中のエディタの実行ファイル。窓を持たない裏の Unity.exe（取り込みの作業用）も同じ実行ファイルなので区別しない。</summary>
    private static IReadOnlyList<string> RunningEditorPaths()
    {
        var paths = new List<string>();
        try
        {
            foreach (var process in Process.GetProcessesByName("Unity"))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } path)
                        {
                            paths.Add(path);
                        }
                    }
                    catch (Exception exception)
                        when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // 権限の違うプロセスは覗けない。ほかの記録から探せる
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>実行ファイルの版の欄（<c>2022.3.22f1_887be4894c44</c>）が、その版か。</summary>
    private static bool HasVersion(string exe, string version)
    {
        try
        {
            return File.Exists(exe)
                && UnityEditorLocator.IsVersion(FileVersionInfo.GetVersionInfo(exe).ProductVersion, version);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
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

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

        // 場所が分かるエディタは場所で照らす。題の名前だけで見ると、「cleanTest - コピー」を開いているのに
        // 「cleanTest」と読んで、開いていないと思い込んでいた（2026-09-19）
        // 同じ名前のプロジェクトが2つ開いていて見分けられないエディタは、名前では選ばない（別の方を手前に出しかねない）
        var editor = UnityEditors.FindByProject(UnityEditors.Open(), projectPath, name);

        if (editor is null)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById(editor.ProcessId);
        }
        catch (ArgumentException)
        {
            // 数えた後に閉じられた
            return null;
        }
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
                // 浮いた窓ではなく主の窓を手前に出す
                var window = UnityEditors.MainWindowOf(running.Id);
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
                // 引用符は .NET に任せる。手で「"パス"」と組むと、パスが \ で終わるとき（D:\proj\）に \" が引用符の逃がしと読まれ、
                // 引数が閉じずに後ろまで1つにつながっていた。ArgumentList は末尾の \ を倍にして正しく閉じる
                ArgumentList = { "-projectPath", projectPath },
                UseShellExecute = true,
            })
                ? UnityOpenResult.Launched
                : UnityOpenResult.Failed;
        }

        // 入れる面倒はHubに渡す。バージョンを添えると、そのバージョンの話として開く。
        // **Hub が入っていなければ渡さない。**受け手の無いリンクを開くと、Windows が「このリンクを開くアプリを探す」を出すだけで、
        // こちらは「Hubに渡しました」と言ってしまう
        if (!HasHub())
        {
            return UnityOpenResult.NoEditorNoHub;
        }

        var hasVersion = !string.IsNullOrWhiteSpace(wanted);
        var link = hasVersion ? $"unityhub://{wanted}" : "unityhub://";
        return !Start(new ProcessStartInfo(link) { UseShellExecute = true })
            ? UnityOpenResult.Failed
            : hasVersion ? UnityOpenResult.HandedToHub : UnityOpenResult.HandedToHubWithoutVersion;
    }

    /// <summary>
    /// Unity Hub が入っているか。<c>unityhub://</c> の受け手（Hub が入るときに登録する）か、アンインストール情報の「Unity Hub」で見る。
    /// </summary>
    public static bool HasHub()
        => InstalledApps.OpenCommand("unityhub") is not null
            || InstalledApps.Uninstall().Any(entry => entry.DisplayName.StartsWith("Unity Hub", StringComparison.OrdinalIgnoreCase));

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
