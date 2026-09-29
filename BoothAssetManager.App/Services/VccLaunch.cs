using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>
/// VRChat Creator Companion を開く。**起動するだけで、中身（設定・プロジェクト・VPM）には触らない。**
///
/// 裏付けは <c>docs/history/unity-handoff.md</c> §12（2026-09-13 実機で確かめた）。
/// 候補の並びは <see cref="ProjectManagerApps.VccCandidates"/>（試験付き）、起動の作法は <see cref="DesktopAppLaunch"/>。
/// </summary>
public static class VccLaunch
{
    /// <summary>VCC のプロセスの名前（手元の 2.4.5）。</summary>
    private const string ProcessName = "CreatorCompanion";

    /// <summary>
    /// 実行ファイルの場所。**決め打ちせず、Windows に残る記録から引く**（利用者ごとの場所に入る物なので・ユーザ指示）。無ければ null。
    /// </summary>
    public static string? FindExe() => ProjectManagerApps.FirstExisting(
        ProjectManagerApps.VccCandidates(InstalledApps.Uninstall(), InstalledApps.OpenCommand("vcc")),
        DesktopAppLaunch.Exists);

    /// <summary>開く。既に開いていれば手前に出す。</summary>
    public static AppOpenResult Open() => DesktopAppLaunch.Open(FindExe(), ProcessName);

    /// <summary>
    /// 開けるか。入っている（場所が分かる）か、今起動している。「VCCを開く」を押せる状態にするかに使う
    /// （押してから「見つかりませんでした」と言うより、押す前に分かる方がよい・ユーザ判断 2026-09-13）。
    /// </summary>
    public static bool IsAvailable() => FindExe() is not null || DesktopAppLaunch.IsRunning(ProcessName);
}
