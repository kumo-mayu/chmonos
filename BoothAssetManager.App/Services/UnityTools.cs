namespace BoothAssetManager.App.Services;

/// <summary>
/// Unity Hub と VCC が手元にあるか。見つからないときの振る舞いを決めるのに使う（ユーザ判断 2026-09-13）。
/// 改変の画面は窓が手前に戻るたびに調べ直す——入れて戻ってきたら、そのまま使えるように。
/// </summary>
public sealed record UnityTools(bool HasHub, bool HasVcc)
{
    /// <summary>調べる前。調べ終わる前に「見つかりません」と言い出さないよう、あるものとして扱う。</summary>
    public static UnityTools Unknown { get; } = new(true, true);

    public static UnityTools Detect() => new(UnityLaunch.HasHub(), VccLaunch.IsAvailable());
}
