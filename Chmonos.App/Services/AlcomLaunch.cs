using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// ALCOM（VCC の代わりに使われるコミュニティのアプリ）を開く。**起動するだけで、中身には触らない**
/// （ユーザ指示 2026-09-29「ALCOMからも開けるようにしましょう。」・<c>docs/research/alcom.md</c> §6 案 A）。
///
/// ALCOM に「このプロジェクトを開いて」と頼む口は無い（alcom.md §3-2）ので、開くのは ALCOM の画面まで。
/// 候補の並びは <see cref="ProjectManagerApps.AlcomCandidates"/>（試験付き）、起動の作法は VCC と同じ <see cref="DesktopAppLaunch"/>。
/// </summary>
public static class AlcomLaunch
{
    /// <summary>ALCOM のプロセスの名前（1.1.8 の <c>ALCOM.exe</c>）。</summary>
    private const string ProcessName = "ALCOM";

    /// <summary>実行ファイルの場所。無ければ null。</summary>
    public static string? FindExe() => ProjectManagerApps.FirstExisting(
        ProjectManagerApps.AlcomCandidates(
            InstalledApps.Uninstall(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            InstalledApps.OpenCommand("vcc")),
        DesktopAppLaunch.Exists);

    /// <summary>開く。既に開いていれば手前に出す。</summary>
    public static AppOpenResult Open() => DesktopAppLaunch.Open(FindExe(), ProcessName);

    /// <summary>開けるか。入っている（場所が分かる）か、今起動している。「ALCOMを開く」を出すかに使う。</summary>
    public static bool IsAvailable() => FindExe() is not null || DesktopAppLaunch.IsRunning(ProcessName);
}
