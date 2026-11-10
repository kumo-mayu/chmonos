namespace Chmonos.Core.Services;

/// <summary>
/// 共通素体の名前どうしが、同じ素体を指すかの比べ方（1つ）。
///
/// 共通素体は名前で参照する（商品の <c>avatarBases</c>・変換のつなぎ先・向き先・更新の向き先。ユーザ判断 2026-11-10：ID には替えない）。
/// 大文字小文字だけ違う名前（手で打った「marubody」と一覧の「MARUBODY」）は同じ素体とみなす。
/// 前は大半が <see cref="StringComparison.CurrentCultureIgnoreCase"/> で、一部（更新の向き先など）が Ordinal で、
/// 同じ素体を所によって別物と見ていた。比べ方を変えるときはここだけを直す。
///
/// 接尾辞や版番号を落として「同じ素体の書き方か」を見る <see cref="AvatarBaseKeys.Key"/> とは問いが違う（こちらは登録した名前どうし）。
/// 並べ替えの順と、変わったかを見る比べ（書き直すかの判断）は、意味が違うのでこれを使わない。
/// </summary>
public static class AvatarBaseNames
{
    /// <summary>素体の名前を鍵にする表・集合の比べ器。</summary>
    public static StringComparer Comparer => StringComparer.CurrentCultureIgnoreCase;

    /// <summary>2つの名前が同じ素体を指すか。</summary>
    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
