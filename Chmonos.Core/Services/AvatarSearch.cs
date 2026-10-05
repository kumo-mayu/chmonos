using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>名前のほかに、そのアバターを指す呼び方1つ。<see cref="Label"/> は当たったときに出す「何で当たったか」。</summary>
public sealed record AvatarHint(string Text, string Label);

/// <summary>
/// アバターを探す欄の照らし方（メモ48・メモ58）。**アバターを語で探す欄はすべてここを通す。**
///
/// 表示名だけで引けると思うと引けない場面がある。表示名は短くしてあるので、略称（「kip」）やBOOTHの正式名の一部で探しても
/// 当たらなかった。登録簿が覚えている呼び方（<see cref="AvatarRegistryEntry.Aliases"/>）とBOOTHの正式名も、名前と同じ重さで照らす
/// （商品IDは候補の文字に入れてある欄が多いので、欄ごとの扱いに任せる）。
/// </summary>
public static class AvatarSearch
{
    /// <summary>
    /// 名前のほかに、そのアバターを引ける語。表示名と同じ字になる物は入れない（当たっても何も増えない）。
    /// </summary>
    public static IReadOnlyList<AvatarHint> Hints(AvatarRegistryEntry entry, string shownName)
    {
        var hints = new List<AvatarHint>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { shownName };

        if (entry.BoothName is { Length: > 0 } booth && seen.Add(booth.Trim()))
        {
            hints.Add(new AvatarHint(booth.Trim(), $"正式名「{booth.Trim()}」"));
        }

        foreach (var alias in entry.Aliases)
        {
            var text = alias.Text.Trim();
            if (text.Length > 0 && seen.Add(text))
            {
                hints.Add(new AvatarHint(text, $"呼び方「{text}」"));
            }
        }

        return hints;
    }

    /// <summary>
    /// 語がこのアバターに当たるか。当たったのが名前でも商品IDでもなく呼び方などのときは、その札を <paramref name="hint"/> に返す。
    /// 名前で当たれば札は付けない（名前は行に出ている）。
    /// </summary>
    public static bool Matches(AvatarRegistryEntry entry, string shownName, string query, out AvatarHint? hint)
    {
        hint = null;
        if (shownName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.ItemId.Contains(query, StringComparison.Ordinal))
        {
            return true;
        }

        hint = Hints(entry, shownName).FirstOrDefault(candidate => candidate.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        return hint is not null;
    }
}
