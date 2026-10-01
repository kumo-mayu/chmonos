using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 説明の本文に「〇〇共通素体」と書かれた素体の名前（商品ページで素体の候補として見せる）。
/// </summary>
/// <param name="Name">本文に書かれていた名前（登録簿にあれば登録簿の素体の名前）。</param>
/// <param name="IsRegistered">登録簿の素体（削除していない物）に当たったか。当たらなければ、選ぶと素体も足す。</param>
public sealed record AvatarBaseMention(string Name, bool IsRegistered);

/// <summary>
/// 説明の本文の「〇〇共通素体」を、商品の対応素体の**候補**として拾う（ユーザ判断 2026-09-29）。
///
/// 検出（<see cref="AvatarDetector.ScanBaseDeclarations(AvatarDetector.ParsedDescription, IEnumerable{string}, IEnumerable{string}, IEnumerable{AvatarBaseGroup}, IReadOnlyList{string})"/>）は
/// 登録簿にある素体の名前しか採らない。登録簿に無い素体を本文から作ると素体の一覧が勝手に増え、
/// 「共通素体ではありません」「〇〇には対応していません」のような書き方の揺れを規則で吸収しきれない。
/// そこで**入れずに候補として見せ、人が選んだときだけ入れる**（CLAUDE.md「推定した値を勝手に入れない」）。
/// 2つ目の評価データでは、登録簿の素体に無い本文の「〇〇共通素体」が正解の漏れ2組の原因だった。
/// </summary>
public static class AvatarBaseMentions
{
    // 名前は「共通素体」の直前の、区切り（空白・句読点・括弧・記号）を含まない並び。
    // 16字は +Head・まめふれんず・珍飯亭のような素体名に足り、文の頭まで飲み込みすぎない長さ
    private static readonly Regex Mention = new(
        @"(?<name>[^\s、。，,．.：:・/／\\（）()【】「」『』\[\]<>＜＞《》〈〉※*＊#＃!！?？~〜～|｜=＝""'”“’‘]{1,16}?)\s?共通素体",
        RegexOptions.Compiled);

    /// <summary>
    /// 否定の言い回し。行ごと見ない（今の検出が「非対応」「以外」の行を読まないのに倣う）。
    /// 外しきれない物は候補に残る——入れるかは人が決めるので、拾いすぎより見落としを避ける
    /// </summary>
    private static readonly Regex Negation = new(
        "非対応|未対応|ではありません|ではない|ではなく|じゃない|じゃありません|対応して(い|お)りません|対応していません|対応しません|対応外|"
        + "使用できません|使えません|着用できません|できません|不可|以外|(?<![A-Za-z])NG(?![A-Za-z])|(?i:not\\s+(supported|compatible))",
        RegexOptions.Compiled);

    /// <summary>名前の前に付いた助詞で文を切る（「本商品は〇〇共通素体」→「〇〇」）。助詞の前が漢字・カナ・英数のときだけ。</summary>
    private const string Particles = "はがをにでもとのへや";

    /// <summary>素体を特定しない語。「各共通素体」「その他の共通素体」「オリジナル共通素体」で素体を作らない。</summary>
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "各", "他", "全", "同", "その他", "他の", "各種", "一部", "全て", "すべて", "主要", "人気",
        "オリジナル", "独自", "自作", "当", "本", "当ショップ", "弊社", "男性", "女性", "男女",
    };

    /// <summary>
    /// 本文の「〇〇共通素体」のうち、この商品にまだ付いていない物（付けた物も外した物も出さない）。
    ///
    /// 登録簿の素体（名前か消していない別名）に当たれば、その素体の名前で返す（選ぶと付けるだけ）。
    /// 登録簿で**削除した**素体に当たる物は出さない（人が素体ではないと決めた）。
    /// クレジット・サムネの節と、「使用アバター」のような行は読まない（撮影に使った素体の名前が出る）。
    /// </summary>
    public static IReadOnlyList<AvatarBaseMention> Find(
        AvatarDetector.ParsedDescription parsed,
        IEnumerable<AvatarBaseGroup> registryGroups,
        IEnumerable<AvatarBaseLink> itemLinks,
        IReadOnlyList<string> ignoredHeadings)
    {
        var groups = registryGroups.ToList();
        var live = AvatarBaseKeys.Lookup(groups.Where(group => !group.Rejected));
        var registered = groups.Where(group => !group.Rejected)
            .Select(group => group.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var deleted = groups.Where(group => group.Rejected)
            .SelectMany(group => group.Aliases.Select(alias => alias.Text).Prepend(group.Name))
            .Select(AvatarBaseKeys.Key)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var attached = itemLinks.Select(link => AvatarBaseKeys.Key(link.BaseName))
            .ToHashSet(StringComparer.Ordinal);

        var found = new List<AvatarBaseMention>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in parsed.Sections)
        {
            if (AvatarDetector.CreditHeading.IsMatch(section.Heading) || AvatarDetector.Contains(section.Heading, ignoredHeadings))
            {
                continue;
            }

            foreach (var line in section.Lines)
            {
                if (Negation.IsMatch(line) || AvatarDetector.NotAvatarSupport.IsMatch(line) || AvatarDetector.CreditLine.IsMatch(line))
                {
                    continue;
                }

                foreach (Match match in Mention.Matches(line))
                {
                    var name = NameOf(match.Groups["name"].Value);
                    var key = AvatarBaseKeys.Key(name);
                    if (name is null || key.Length < 2 || deleted.Contains(key) || !seen.Add(key))
                    {
                        continue;
                    }

                    // 登録簿の素体に当たれば、その名前で出す（別名で書かれていても同じ素体に付ける）
                    var shown = live.TryGetValue(key, out var groupName) && registered.Contains(groupName) ? groupName : name;
                    var shownKey = AvatarBaseKeys.Key(shown);
                    if (attached.Contains(key) || attached.Contains(shownKey) || deleted.Contains(shownKey))
                    {
                        continue;
                    }

                    found.Add(new AvatarBaseMention(shown, registered.Contains(shown)));
                }
            }
        }

        return found;
    }

    /// <summary>拾った並びから素体の名前を切り出す。名前にならない物は null。</summary>
    internal static string? NameOf(string raw)
    {
        var name = raw.Trim();

        // 文の頭から続いていれば、最後の助詞の後ろだけを採る（「本商品は」「こちらの」）。
        // 助詞の前がひらがななら名前の一部とみなす（「まめふれんず」「りりか」のような名前を割らない）
        for (var index = name.Length - 2; index >= 1; index--)
        {
            if (Particles.Contains(name[index]) && !IsHiragana(name[index - 1]))
            {
                name = name[(index + 1)..];
                break;
            }
        }

        // 「〇〇の共通素体」の「の」
        if (name.Length >= 2 && Particles.Contains(name[^1]) && !IsHiragana(name[^2]))
        {
            name = name[..^1];
        }

        name = name.Trim().TrimStart('-', 'ー', '～', '〜');
        if (name.Length == 0 || NotNames.Contains(name) || name.All(char.IsDigit))
        {
            return null;
        }

        return name;
    }

    private static bool IsHiragana(char ch) => ch is >= 'ぁ' and <= 'ゖ';
}
