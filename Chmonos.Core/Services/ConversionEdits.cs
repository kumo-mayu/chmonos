using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品ページの「変換扱い」の欄で、人が変換扱いを変える変え方（<see cref="Commands.UiCommand.ChangeConversion"/> に渡す。<c>docs/spec/conversion.md</c>「画面」）。
///
/// どれも**錠の中で今の値に当てる**関数を返す。画面の写しの値を書き戻すと、開いている間に見回り（<see cref="ConversionScan"/>）が書き直した推した値を戻してしまう。
/// 人が触ったつなぎ先は確認済み（<see cref="ConversionTarget.Confirmed"/>）にする（推定は人が選んだときだけ入れる決まり）。
/// 何も決めていない形（自動に任せて、つなぎ先も無い）になったら null にする（書き出さない）。
/// </summary>
public static class ConversionEdits
{
    /// <summary>
    /// 扱い（自動の判定に任せる・プロファイルとして扱う・扱わない）を変える。つなぎ先は残す
    /// （「扱わない」から戻したときに、前に決めたつなぎ先が消えていると決め直しになる）。
    /// </summary>
    public static Func<ConversionSetting?, ConversionSetting?> SetMode(ConversionMode mode)
        => current => Normalize((current ?? new ConversionSetting()) with { Mode = mode });

    /// <summary>
    /// プロファイル1つ（<paramref name="slug"/>）のつなぎ先を決める。その slug の行（確認待ちも）を、確認済みの1行に置き換える。
    /// ほかのつなぎ先に付けた外した印は残す（選んだ物を後で外したときに、前に外した物をまた推さないように）。
    /// slug が分からない（null）ときは、同じつなぎ先の行を置き換えて足す。
    /// </summary>
    public static Func<ConversionSetting?, ConversionSetting?> Choose(
        string? slug,
        bool forward,
        bool reverse,
        string? avatarItemId,
        string? baseName)
    {
        var chosen = new ConversionTarget
        {
            AvatarItemId = avatarItemId,
            BaseName = baseName,
            Forward = forward,
            Reverse = reverse,
            Slug = slug,
            Confirmed = true,
        };

        return current =>
        {
            var targets = (current?.Targets ?? [])
                .Where(target => !IsSameRow(target, chosen)
                    && (slug is null || !SameText(target.Slug, slug) || target.Rejected))
                .Append(chosen)
                .ToList();
            return Normalize((current ?? new ConversionSetting()) with { Targets = targets });
        };
    }

    /// <summary>推した（確認待ちの）つなぎ先を、そのまま確認済みにする。今の値に同じ行が無ければ（見回りが書き直した）何もしない。</summary>
    public static Func<ConversionSetting?, ConversionSetting?> Confirm(ConversionTarget target)
        => current => Map(current, target, found => found with { Confirmed = true });

    /// <summary>
    /// 名指しした確認待ちのつなぎ先を、まとめて確認済みにする（アバターの管理の「変換」の「推した通りに決めて次へ」と「推した通りにまとめて決める」）。
    /// 名指しは人が見た行だけ：押す前に見せた後で見回りが推し直した行は、見ていないので確かめない（推定を勝手に入れない）。
    /// </summary>
    public static Func<ConversionSetting?, ConversionSetting?> ConfirmMany(IReadOnlyCollection<ConversionTarget> targets)
        => current =>
        {
            if (current is null || targets.Count == 0)
            {
                return current;
            }

            var changed = false;
            var next = current.Targets
                .Select(candidate =>
                {
                    if (candidate.Confirmed || candidate.Rejected || !targets.Any(target => IsSameRow(candidate, target)))
                    {
                        return candidate;
                    }

                    changed = true;
                    return candidate with { Confirmed = true };
                })
                .ToList();
            return changed ? current with { Targets = next } : current;
        };

    /// <summary>つなぎ先の向き（順方向・逆方向）を変える。人が決めた値なので確認済みにする。</summary>
    public static Func<ConversionSetting?, ConversionSetting?> SetDirection(ConversionTarget target, bool forward, bool reverse)
        => current => Map(current, target, found => found with { Forward = forward, Reverse = reverse, Confirmed = true });

    /// <summary>
    /// つなぎ先を外す。行は消さずに外した印を付け、次の見回りでも同じ物を推さない（ユーザ判断 2026-11-09）。
    /// そのプロファイルには、選ぶ欄からほかのつなぎ先を選べる（同じ物を選び直せば印は消える）。
    /// </summary>
    public static Func<ConversionSetting?, ConversionSetting?> Reject(ConversionTarget target)
        => current => Map(current, target, found => found with { Rejected = true, Confirmed = true });

    private static ConversionSetting? Map(ConversionSetting? current, ConversionTarget target, Func<ConversionTarget, ConversionTarget> change)
    {
        if (current is null || !current.Targets.Any(candidate => IsSameRow(candidate, target)))
        {
            return current;
        }

        return current with
        {
            Targets = current.Targets.Select(candidate => IsSameRow(candidate, target) ? change(candidate) : candidate).ToList(),
        };
    }

    /// <summary>
    /// 画面に出した行と、今の値の行が同じ物か。確認済みかと向きは比べない（確かめる・向きを変える操作は、その2つを変えるため）。
    /// </summary>
    public static bool IsSameRow(ConversionTarget a, ConversionTarget b)
        => SamePlace(a, b) && SameText(a.Slug, b.Slug);

    private static bool SamePlace(ConversionTarget a, ConversionTarget b)
        => string.Equals(a.AvatarItemId, b.AvatarItemId, StringComparison.Ordinal)
            && SameText(a.BaseName, b.BaseName);

    private static bool SameText(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static ConversionSetting? Normalize(ConversionSetting setting)
        => setting.IsDefault ? null : setting;
}
