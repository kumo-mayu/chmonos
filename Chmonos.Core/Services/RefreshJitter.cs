namespace Chmonos.Core.Services;

/// <summary>
/// 次回の取り直し（⑦）の予定日に足すばらつき。全商品が同じ日に期限切れにならないよう、商品IDから決める。
/// </summary>
/// <remarks>
/// 乱数ではなくIDから決めるのは、同じ商品なら何度計算しても同じ日になるようにするため。
/// 前は <c>string.GetHashCode</c> を使っていたが、.NET の文字列のハッシュは**プロセスごとに種が変わる**ので、
/// 起動し直すと同じ商品でも違う日になり、コメントの「何度計算しても同じ日」になっていなかった
/// （file-lifecycle.md 気になった所19）。<c>Math.Abs(int.MinValue)</c> で投げる穴もあった。
/// ここでは種の無い FNV-1a（32ビット）で、起動をまたいで同じ値にする。暗号の強さは要らない（散らばればよい）。
/// </remarks>
public static class RefreshJitter
{
    /// <summary><paramref name="jitterDays"/> 日の幅（±）の中で、商品IDから決まる日数。幅が0以下なら0。</summary>
    public static int Days(string itemId, int jitterDays)
    {
        if (jitterDays <= 0)
        {
            return 0;
        }

        return (int)(StableHash(itemId) % (uint)((jitterDays * 2) + 1)) - jitterDays;
    }

    private static uint StableHash(string text)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var character in text)
        {
            hash = unchecked((hash ^ character) * prime);
        }

        return hash;
    }
}
