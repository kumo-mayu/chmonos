using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>いま開いているUnityエディタ1つ。</summary>
/// <param name="ProjectName">プロジェクト名。窓のタイトルから読めなければ null。</param>
/// <param name="ProjectPath">
/// プロジェクトのフォルダ。Hub・VCC の一覧で言い当てられたときだけ入る。名前から場所を引き直すと、
/// 同じ名前のフォルダが2つあるときに取り違えるので、分かっているならこちらを使う（2026-09-19）
/// </param>
/// <param name="IsAmbiguous">
/// 同じ名前のプロジェクトが複数開いていて、題からどれの窓か見分けられない。名前で照らして決め打ちしない。
/// </param>
public sealed record OpenUnityEditor(int ProcessId, string? ProjectName, string? ProjectPath = null, bool IsAmbiguous = false);

/// <summary>
/// 開いているUnityエディタを数える。
///
/// **押す前に見る必要がある。**送り先はこちらで選べない
/// （Windowsが起動中のエディタへ転送する）ので、
/// 開いていなければ何も起きず、Unity Hubの窓だけが出る。
/// 押してから「何も起きなかった」と気付くのは最悪なので、先に見て言い分ける。
///
/// 裏付けは <c>docs/history/unity-handoff.md</c>。
/// </summary>
public static class UnityEditors
{
    /// <summary>
    /// プロセス名。Unity Hub は <c>Unity Hub</c> なので、これで拾い分かれる。
    /// </summary>
    private const string EditorProcessName = "Unity";

    /// <summary>
    /// 開いているエディタを返す。1つも無ければ空。
    ///
    /// **窓を持たない Unity.exe は数えない。**Unity 2021.2 以降は取り込みを並列にするため、
    /// 窓の無い Unity.exe（<c>-batchMode -name AssetImportWorkerN</c>）を裏で起こし、取り込みの後もしばらく残す。
    /// これを数えて「Unityが3つ開いています」と言い、送れなくしていた
    /// （2026-09-11 実機で確認。本体1＋AssetImportWorker4・5）。
    ///
    /// 窓があって題が読めない（起動中・コンパイル中）エディタは数に入れ、名前を null にする。
    /// 「開いていない」と誤って言う方が害が大きい。
    /// </summary>
    public static IReadOnlyList<OpenUnityEditor> Open()
    {
        try
        {
            var processes = Process.GetProcessesByName(EditorProcessName);

            // 一覧は窓を持つエディタがあるときだけ読む（何も開いていないときに毎回ファイルを読まない）
            IReadOnlyList<string>? known = null;

            return processes
                .Select(process =>
                {
                    using (process)
                    {
                        if (process.MainWindowHandle == IntPtr.Zero)
                        {
                            return null;
                        }

                        // 題はメニューを持つ主の窓から読む（MainWindowHandle は浮いた窓に当たることがある。MainWindowOf）
                        var main = MainWindowOf(process.Id);
                        var title = main != IntPtr.Zero ? TitleOf(main) : process.MainWindowTitle;

                        // 題の先頭で切るだけだと、名前に " - " を含むプロジェクト（「cleanTest - コピー」）を
                        // 同じ頭の別のプロジェクトと取り違えた。一覧の名前と照らして言い当てる（2026-09-19）
                        known ??= UnityProjects.KnownPaths();
                        var found = UnityHandoff.IdentifyProject(
                            title, known, candidate => UnityProjects.IsProjectOpen(candidate, anyEditorRunning: true));
                        return new OpenUnityEditor(process.Id, found.Name, found.Path, found.IsAmbiguous);
                    }
                })
                .OfType<OpenUnityEditor>()
                .ToList();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 数えられなくてもアプリは動き続ける。数えられない＝送れないとは限らないので空を返す
            return [];
        }
    }

    /// <summary>
    /// そのエディタのプロジェクトの場所。**窓の題から一覧で言い当てた場所を先に使う**——
    /// 名前から引き直すと、同じ名前のフォルダが2つあるときに取り違える（2026-09-19）。
    /// Hub にも VCC にも載っていないプロジェクトは引けない（null）。
    /// </summary>
    /// <remarks>
    /// 見分けられない（同じ名前のプロジェクトが複数開いている）エディタは名前から引き直さない——引けば先の方に決め打ちになる。
    /// 名前から引くときも、同じ名前の場所が2つ以上あれば決めない。
    /// </remarks>
    public static string? PathOf(OpenUnityEditor editor)
    {
        if (editor.ProjectPath is { } known)
        {
            return known;
        }

        if (editor.IsAmbiguous || editor.ProjectName is not { } name)
        {
            return null;
        }

        var matches = UnityProjects.Discover()
            .Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>送り先のプロセス番号から場所を引く（取り込みの列は番号しか持っていない）。</summary>
    public static string? PathOf(int processId)
        => Open().FirstOrDefault(editor => editor.ProcessId == processId) is { } found ? PathOf(found) : null;

    /// <summary>
    /// 名前か場所でエディタを探す。場所が分かっているエディタは場所で、分からないものは名前で照らす。
    /// **見分けられないエディタは名前では選ばない**（同じ名前の別のプロジェクトかもしれない）。
    /// </summary>
    public static OpenUnityEditor? FindByProject(IEnumerable<OpenUnityEditor> editors, string projectPath, string projectName)
    {
        var trimmed = projectPath.TrimEnd('\\', '/');
        return editors.FirstOrDefault(candidate => candidate.ProjectPath is { } path
            ? PathText.Same(path.TrimEnd('\\', '/'), trimmed)
            : !candidate.IsAmbiguous && string.Equals(candidate.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
    }

    // ---- 主の窓 ----

    private const string ContainerClass = "UnityContainerWndClass";

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    /// <summary>
    /// エディタの主の窓（メニューを持つ窓）。無ければ <see cref="Process.MainWindowHandle"/>、それも無ければ 0。
    /// </summary>
    /// <remarks>
    /// <see cref="Process.MainWindowHandle"/> は「Z 順で先頭の、持ち主の無い見えている窓」で、主の窓とは限らない。
    /// Unity は浮かせたタブ（Inspector を外に出した物など）も同じ種類（<c>UnityContainerWndClass</c>）の窓で作り、
    /// それが手前にあると MainWindowHandle がそちらを指す。浮いた窓にはメニューが無いので「Custom Package...」が見つからず送れない、
    /// 題はタブの名前なのでプロジェクト名が読めない、ということが起きていた。主の窓だけがメニューを持つので、それで見分ける。
    /// 起動中などでメニューがまだ無いときは、今までどおり MainWindowHandle に任せる（数えないよりよい）。
    /// </remarks>
    public static IntPtr MainWindowOf(int processId)
    {
        var found = IntPtr.Zero;
        var hidden = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId || ClassOf(window) != ContainerClass || GetMenu(window) == IntPtr.Zero)
            {
                return true;
            }

            if (IsWindowVisible(window))
            {
                found = window;
                return false;
            }

            if (hidden == IntPtr.Zero)
            {
                hidden = window;
            }

            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            return found;
        }

        if (hidden != IntPtr.Zero)
        {
            return hidden;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
}
