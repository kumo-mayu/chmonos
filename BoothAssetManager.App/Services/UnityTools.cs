using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>
/// Unity Hub・VCC・ALCOM が手元にあるか。見つからないときの振る舞いを決めるのに使う（ユーザ判断 2026-09-13）。
/// 改変の画面は窓が手前に戻るたびに調べ直す——入れて戻ってきたら、そのまま使えるように。
/// </summary>
public sealed record UnityTools(bool HasHub, bool HasVcc, bool HasAlcom)
{
    /// <summary>
    /// 調べる前。調べ終わる前に「見つかりません」と言い出さないよう、Hub と VCC はあるものとして扱う。
    /// ALCOM は無いものとして扱う——あるものにすると、ALCOM の無い大半の人の画面で、ボタンが2つ出てから1つに減る。
    /// </summary>
    public static UnityTools Unknown { get; } = new(true, true, false);

    /// <summary>「VCCを開く」「ALCOMを開く」のどれを出すか。</summary>
    public ProjectManagerButtons Buttons => ProjectManagerApps.Buttons(HasVcc, HasAlcom);

    public static UnityTools Detect() => new(UnityLaunch.HasHub(), VccLaunch.IsAvailable(), AlcomLaunch.IsAvailable());
}
