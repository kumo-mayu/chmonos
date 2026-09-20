using System.Diagnostics;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>いま開いているUnityエディタ1つ。</summary>
/// <param name="ProjectName">プロジェクト名。窓のタイトルから読めなければ null。</param>
/// <param name="ProjectPath">
/// プロジェクトのフォルダ。Hub・VCC の一覧で言い当てられたときだけ入る。名前から場所を引き直すと、
/// 同じ名前のフォルダが2つあるときに取り違えるので、分かっているならこちらを使う（2026-09-19）
/// </param>
public sealed record OpenUnityEditor(int ProcessId, string? ProjectName, string? ProjectPath = null);

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

                        // 題の先頭で切るだけだと、名前に " - " を含むプロジェクト（「cleanTest - コピー」）を
                        // 同じ頭の別のプロジェクトと取り違えた。一覧の名前と照らして言い当てる（2026-09-19）
                        known ??= UnityProjects.KnownPaths();
                        var (name, path) = UnityHandoff.ProjectFromWindowTitle(
                            process.MainWindowTitle, known, candidate => UnityProjects.IsProjectOpen(candidate, anyEditorRunning: true));
                        return new OpenUnityEditor(process.Id, name, path);
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
    public static string? PathOf(OpenUnityEditor editor)
        => editor.ProjectPath ?? (editor.ProjectName is { } name
            ? UnityProjects.Discover().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))?.Path
            : null);

    /// <summary>送り先のプロセス番号から場所を引く（取り込みの列は番号しか持っていない）。</summary>
    public static string? PathOf(int processId)
        => Open().FirstOrDefault(editor => editor.ProcessId == processId) is { } found ? PathOf(found) : null;
}
