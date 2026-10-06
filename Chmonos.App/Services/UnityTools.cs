using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// Unity Hub・VCC・ALCOM が手元にあるか。見つからないときの振る舞いを決めるのに使う（ユーザ判断 2026-09-13）。
/// 改変の画面は窓が手前に戻るたびに調べ直す——入れて戻ってきたら、そのまま使えるように。
/// </summary>
/// <param name="LinkOpensAlcom"><c>vcc://</c> を ALCOM が引き受けているか。両方あるときの既定の開く方を決める。</param>
public sealed record UnityTools(bool HasHub, bool HasVcc, bool HasAlcom, bool LinkOpensAlcom)
{
    /// <summary>
    /// 調べる前。調べ終わる前に「見つかりません」と言い出さないよう、Hub と VCC はあるものとして扱う。
    /// ALCOM は無いものとして扱う——あるものにすると、ALCOM の無い大半の人の画面で、ボタンが「ALCOMを開く」から「VCCを開く」に替わる。
    /// </summary>
    public static UnityTools Unknown { get; } = new(true, true, false, false);

    /// <summary>「VCCを開く」「ALCOMを開く」のどちらを出すか。両方あるときは設定の <paramref name="choice"/> に従う。</summary>
    public ProjectManagerButtons Buttons(ProjectManagerChoice choice)
        => ProjectManagerApps.Buttons(HasVcc, HasAlcom, LinkOpensAlcom, choice);

    /// <summary>この PC の記録から見分ける。見分け方そのものは <see cref="UnityToolPresence.Detect"/>（試験付き）。</summary>
    public static UnityTools Detect()
    {
        var presence = UnityToolPresence.Detect(InstalledApps.Records(), DesktopAppLaunch.Exists, DesktopAppLaunch.IsRunning);
        return new(presence.HasHub, presence.HasVcc, presence.HasAlcom, presence.LinkOpensAlcom);
    }
}
