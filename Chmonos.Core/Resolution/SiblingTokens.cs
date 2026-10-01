namespace Chmonos.Core.Resolution;

/// <summary>
/// 同じ商品を分けて配った zip の間で**変わる語**。自動検索の検索語から外す。
///
/// 衣装やアクセサリーは、1つの商品を対応アバターごとに分けた zip（「商品名_アバター名」）で配ることが多い。
/// アバター名が手元の登録簿に無い（持っていないアバター）と <see cref="AvatarTokens"/> では見分けられず、
/// BOOTH の検索はスペースを AND で読むので、その1語で正解が1ページ目から落ちる
/// （正解の分かる472本で、増えた154本の外れのうち23本がこの形。混ざる語を登録簿と照らしても、アバター名と分かったのは127語中4語。2026-09-29）。
///
/// 同じフォルダに頭の語が同じ zip が並んでいれば、その間で入れ替わる語は商品名ではなく分け方の語（アバター名・色）とみなす。
/// 商品IDは見ない（未確定の一覧で手に入るのはファイルの場所と名前だけ）。
///
/// 効き目は小さい（472本で画面の1位 +1本・減り0）。狙った外れの多くは、外した後に残る語が頭の1語だけで、
/// その1語の検索は引き直しで既に試していて60件に埋もれる（docs/research/id-resolution.md §18）。
/// </summary>
public static class SiblingTokens
{
    /// <summary>
    /// 変わる語とみなすには、頭の語が違うほかのファイル（同じ種類）にも、この本数以上出ること。
    /// アバター名は別の商品の zip にも繰り返し出るが、同じショップの別の商品を並べた兄弟（「ショップ名_帽子」「ショップ名_靴」）の語は出ない。
    /// 入れ替わるだけで外すと、正解の分かる472本で商品名の語まで外し、上位3件が前の318本で10本・増えた154本で3本減った。
    /// 1本以上にすると増えた154本で1本減り、2本以上で減りが無くなった（2026-09-29）。
    /// </summary>
    private const int MinRecurrence = 2;

    /// <summary>
    /// <paramref name="filePath"/> の語のうち、兄弟の zip と入れ替わっている語。無ければ空。
    ///
    /// 兄弟は、一覧の中で同じフォルダ・同じ種類（拡張子）にあり、頭の語が同じもの。
    /// 語の並びを頭と尻から突き合わせ、**両方に**入れ替わる語があるときだけ変わる語とみなす——
    /// 版番号だけが違う兄弟（v1.2 と v1.3）は語が同じなので何も外さない。片方に語が足されただけの兄弟（「商品名」と「商品名_おまけ」）も外さない。
    /// </summary>
    /// <param name="listedPaths">未確定の一覧にあるファイルの場所（選んだファイル自身を含んでよい）。</param>
    /// <param name="isAvatarName">
    /// アバターの名前の語か。残る語を数えるときに、名前として外れる語も除いて見る。
    /// </param>
    /// <returns>
    /// 外した後に特徴のある語（英字3字以上・日本語2字以上）が残らないときは空（外さずに今のまま引く）。
    /// </returns>
    public static IReadOnlySet<string> Varying(
        string filePath, IEnumerable<string> listedPaths, Func<string, bool>? isAvatarName = null)
    {
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = FileNameQuery.Tokens(filePath);
        if (tokens.Count < 2)
        {
            return empty;
        }

        var name = Path.GetFileName(filePath);
        var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        var extension = Path.GetExtension(filePath);
        var varying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var others = new List<IReadOnlyList<string>>();

        foreach (var listed in listedPaths)
        {
            if (string.Equals(Path.GetFileName(listed), name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(listed) ?? string.Empty, folder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 種類が違う物（zip を展開した画像など）は見ない。中の画像の名前には商品の中の部品の名前が繰り返し出る
            if (!string.Equals(Path.GetExtension(listed), extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var other = FileNameQuery.Tokens(listed);
            if (other.Count == 0)
            {
                continue;
            }

            if (!Same(other[0], tokens[0]))
            {
                others.Add(other);
                continue;
            }

            if (!string.Equals(Path.GetDirectoryName(listed) ?? string.Empty, folder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prefix = 0;
            while (prefix < tokens.Count && prefix < other.Count && Same(tokens[prefix], other[prefix]))
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < tokens.Count - prefix && suffix < other.Count - prefix
                && Same(tokens[tokens.Count - 1 - suffix], other[other.Count - 1 - suffix]))
            {
                suffix++;
            }

            var mine = tokens.Count - prefix - suffix;
            var theirs = other.Count - prefix - suffix;
            if (mine == 0 || theirs == 0)
            {
                continue;
            }

            varying.UnionWith(tokens.Skip(prefix).Take(mine));
        }

        varying.RemoveWhere(token => others.Count(other => other.Contains(token, StringComparer.OrdinalIgnoreCase)) < MinRecurrence);

        if (varying.Count == 0)
        {
            return empty;
        }

        var remains = tokens.Any(token => !varying.Contains(token) && !(isAvatarName?.Invoke(token) ?? false) && IsDistinctive(token));
        return remains ? varying : empty;
    }

    /// <summary>
    /// 商品名を指していない語か（アバターの名前、または兄弟の間で変わる語）。<see cref="FileNameQuery.ToSearchQuery"/> に渡す形。
    /// どちらも無ければ null（何も外さない）。
    /// </summary>
    public static Func<string, bool>? NotProductName(AvatarTokens? avatars, IReadOnlySet<string>? varying)
    {
        if (varying is not { Count: > 0 })
        {
            return avatars is null ? null : avatars.IsAvatarName;
        }

        return avatars is null
            ? varying.Contains
            : token => varying.Contains(token) || avatars.IsAvatarName(token);
    }

    /// <summary>
    /// これだけで引いても1ページに埋もれない程度の語。2字の英字（Mo）は商品名の語として弱すぎる。
    /// 日本語は1字の情報量が多いので2字から。
    /// </summary>
    private static bool IsDistinctive(string token)
        => token.All(char.IsAscii) ? token.Length >= 3 : token.Length >= 2;

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
