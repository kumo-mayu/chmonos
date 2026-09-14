using System.Diagnostics;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>いま開いているUnityエディタ1つ。</summary>
/// <param name="ProjectName">プロジェクト名。窓のタイトルから読めなければ null。</param>
public sealed record OpenUnityEditor(int ProcessId, string? ProjectName);

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
            return Process.GetProcessesByName(EditorProcessName)
                .Select(process =>
                {
                    using (process)
                    {
                        return process.MainWindowHandle == IntPtr.Zero
                            ? null
                            : new OpenUnityEditor(
                                process.Id,
                                UnityHandoff.ProjectNameFromWindowTitle(process.MainWindowTitle));
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
}
