namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変を探す決まり（ユーザ指示 2026-10-05・メモ41。`docs/spec/modifications.md`「今ある改変に追加は、一覧の上の探す欄で絞れる」）。
/// 商品ページの「改変に追加」の窓と、検索の条件「改変」の欄（ユーザ判断 2026-10-06）が同じ探し方になるよう、1か所に置く。
///
/// 語を空白で区切り、**全部の語が**、改変の名前・アバターの名前・Unity プロジェクトの名前のどれかに入っている物を当てる（部分一致。
/// 大文字小文字・かなの種類・全角半角は区別しない）。名前以外で当たったときだけ「アバター：〇〇　プロジェクト：〇〇」と言う。
/// </summary>
internal static class ModificationSearchText
{
    private static readonly System.Globalization.CompareInfo Compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;

    private const System.Globalization.CompareOptions Loose = System.Globalization.CompareOptions.IgnoreCase
        | System.Globalization.CompareOptions.IgnoreKanaType | System.Globalization.CompareOptions.IgnoreWidth;

    public static bool Contains(string text, string word) => text.Length > 0 && Compare.IndexOf(text, word, Loose) >= 0;

    public static string[] Words(string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <param name="avatarAliases">アバターの名前のほかに当てる語（登録簿の呼び方・正式名）。当たったらアバターの名前で言う。</param>
    public static bool Matches(
        string name, string avatarText, string projectName, string[] words, out string note, IReadOnlyList<string>? avatarAliases = null)
    {
        note = string.Empty;
        var avatarHit = false;
        var projectHit = false;
        foreach (var word in words)
        {
            var avatar = Contains(avatarText, word) || (avatarAliases?.Any(alias => Contains(alias, word)) ?? false);
            var project = Contains(projectName, word);
            if (!Contains(name, word) && !avatar && !project)
            {
                return false;
            }

            avatarHit |= avatar;
            projectHit |= project;
        }

        var notes = new List<string>();
        if (avatarHit)
        {
            notes.Add($"アバター：{avatarText}");
        }

        if (projectHit)
        {
            notes.Add($"プロジェクト：{projectName}");
        }

        note = string.Join("　", notes);
        return true;
    }
}
