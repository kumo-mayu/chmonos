using System.Text;
using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 共通素体の名前をそろえる。
///
/// 同じ素体が「MARUBODY 2.0」「まるぼでぃ素体アバター」「まるぼでぃ対応」「#MARUBODY」、
/// 「+Head」「PlusHead」「+head素体アバター」のように書かれる（所持207件の実データ）。
/// 比べるときは接尾辞と版番号を落とし、読み（まるぼでぃ）と綴り（MARUBODY）は素体グループの別名でつなぐ。
///
/// **見つけた名前は、既にある素体グループ（初期辞書か登録簿）の名前・別名に当たったものだけを採る。**
/// 「オリジナル素体」「共通男性素体」のような、グループを特定できない語で組を作らないため。
/// </summary>
public static partial class AvatarBaseKeys
{
    [GeneratedRegex(@"#([A-Za-z0-9_]*BODY)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HashBody { get; }

    [GeneratedRegex(@"(?<name>[^\s、。：:・/／（）()【】「」『』\[\]]{2,12}?)(共通素体|素体)")]
    private static partial Regex NamedBase { get; }

    [GeneratedRegex(@"\+\s?Head|PlusHead|ぷらすへっど", RegexOptions.IgnoreCase)]
    private static partial Regex PlusHead { get; }

    private static readonly string[] Suffixes = ["共通素体", "素体アバター", "素体", "対応", "用", "アバター", "専用"];

    /// <summary>
    /// 比べるための形。「まるぼでぃ素体アバター」→ まるぼでぃ、「MARUBODY 2.0」→ marubody、「+head対応」→ +head。
    /// 名前の正規化は「+」を落とすので、+Head だけは先に見分ける（落とすと汎用語の head になる）。
    /// </summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (PlusHead.IsMatch(Nfkc.Fold(text)))
        {
            return "+head";
        }

        var key = AvatarText.Normalize(text);
        for (var round = 0; round < 3; round++)
        {
            var before = key;
            foreach (var suffix in Suffixes)
            {
                if (key.Length > suffix.Length && key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    key = key[..^suffix.Length];
                }
            }

            // 版番号（MARUBODY 2.0 → marubody20）
            key = Regex.Replace(key, "[0-9]+$", string.Empty);
            if (key == before)
            {
                break;
            }
        }

        return key;
    }

    /// <summary>文の中で素体を名指ししている箇所の候補（#〇〇BODY・〇〇素体・+Head）。</summary>
    public static IEnumerable<string> MentionsIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in HashBody.Matches(text))
        {
            yield return Key(match.Groups[1].Value);
        }

        foreach (Match match in NamedBase.Matches(text))
        {
            yield return Key(match.Groups["name"].Value);
        }

        if (PlusHead.IsMatch(Nfkc.Fold(text)))
        {
            yield return "+head";
        }
    }

    /// <summary>
    /// 比べる形 → 素体グループの名前。グループの名前と、消していない別名から作る。
    /// 登録簿にまだ入っていない初期辞書のグループも含める（検出を一度も走らせていない登録簿でも引けるように）。
    /// </summary>
    public static Dictionary<string, string> Lookup(IEnumerable<AvatarBaseGroup> groups)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        var all = groups.ToList();
        all.AddRange(AvatarBaseSeed.Groups.Where(seed =>
            !all.Any(group => string.Equals(group.Name, seed.Name, StringComparison.CurrentCultureIgnoreCase))));

        foreach (var group in all.Where(group => !string.IsNullOrWhiteSpace(group.Name)))
        {
            foreach (var text in group.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text).Prepend(group.Name))
            {
                var key = Key(text);
                if (key.Length >= 2)
                {
                    lookup.TryAdd(key, group.Name);
                }
            }
        }

        return lookup;
    }

    /// <summary>
    /// この文が名指ししている素体グループ。語そのものが素体名（タグ「まるぼでぃ」）か、
    /// 文の中に「〇〇素体」「#〇〇BODY」「+Head」があるもの。
    /// </summary>
    public static IEnumerable<string> GroupsIn(string? text, IReadOnlyDictionary<string, string> lookup)
    {
        if (lookup.TryGetValue(Key(text), out var whole))
        {
            yield return whole;
        }

        foreach (var key in MentionsIn(text))
        {
            if (lookup.TryGetValue(key, out var group))
            {
                yield return group;
            }
        }
    }

    /// <summary>
    /// アバターの名前・別名から所属する素体を推す。手で決めた所属（<see cref="AvatarRegistryEntry.BaseName"/>）が無いときに使う。
    ///
    /// 名前に「#MARUBODY」「【+Head】」、対応節の呼び名に「コロネ（えも研素体）」のように
    /// 所属が書かれていることがある。**推した所属は保存しない**（規則を直せば計算し直せるように）。
    /// </summary>
    public static string? InferBaseOf(AvatarRegistryEntry entry, IReadOnlyDictionary<string, string> lookup)
    {
        var texts = new[] { entry.BoothName, AvatarNames.ShownName(entry) }
            .Concat(entry.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text));

        foreach (var text in texts)
        {
            foreach (var key in MentionsIn(text))
            {
                if (lookup.TryGetValue(key, out var group))
                {
                    return group;
                }
            }
        }

        return null;
    }
}
