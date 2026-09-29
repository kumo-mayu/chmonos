using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace AvatarEvalBench;

/// <summary>
/// 本文の「〇〇共通素体」の候補（<see cref="AvatarBaseMentions"/>）を数える（2026-09-29）。
///
/// 候補は人が選んで入れる物なので、適合率・再現率ではなく「何件出て、何件が正しいか」を見る。
/// 正しいかは正解（商品×アバター）から読む：その素体に属するアバター（登録簿の所属・名前の手掛かり）か、
/// 名前にその素体名を含むアバターのうち、正解が「対応」の物が1体でもあれば正しい。
/// 当たるアバターが正解に「違う」「参考」しか無ければ誤り。当たるアバターが無ければ判定できない。
/// 名前は出さない（友人のデータなので、数だけを出す）。
/// </summary>
public static class BaseMentionCount
{
    public static void Print(EvalContext context)
    {
        var groups = context.Registry.BaseGroups;
        var live = groups.Where(group => !group.Rejected).ToList();
        var lookup = AvatarBaseKeys.Lookup(live);
        var headings = context.Settings.AvatarSupportHeadings.Count > 0
            ? context.Settings.AvatarSupportHeadings
            : AppSettings.DefaultAvatarSupportHeadings;

        int items = 0, total = 0, registered = 0, correct = 0, wrong = 0, unknown = 0, positivePairs = 0;

        foreach (var (itemId, label) in context.Labels)
        {
            if (!context.Items.TryGetValue(itemId, out var item))
            {
                continue;
            }

            var parsed = AvatarDetector.Parse(context.HtmlOf(itemId), item.Booth.Description);
            var variations = item.Local.Purchases.Select(purchase => purchase.NameSnapshot)
                .Concat(item.Booth.Variations.Select(variation => variation.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList();

            // 今の検出が宣言する素体（候補からは除かれる）
            var declared = AvatarDetector.ScanBaseTags(item.Booth.Tags)
                .Concat(AvatarDetector.ScanBaseDeclarations(parsed, item.Booth.Tags, variations, live, headings))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new AvatarBaseLink { BaseName = name, Source = AvatarLinkSource.Tag, Confirmed = true })
                .ToList();

            var mentions = AvatarBaseMentions.Find(parsed, groups, declared, context.Settings.AvatarIgnoredHeadings);
            if (mentions.Count == 0)
            {
                continue;
            }

            items++;
            foreach (var mention in mentions)
            {
                total++;
                if (mention.IsRegistered)
                {
                    registered++;
                }

                var key = AvatarBaseKeys.Key(mention.Name);
                var related = context.Registry.Entries
                    .Where(entry => entry.ItemId != itemId && Belongs(entry, mention, key, lookup))
                    .Select(entry => entry.ItemId)
                    .ToList();

                var labels = related
                    .Select(id => label.Avatars.TryGetValue(id, out var value) ? value : null)
                    .Where(value => value is not null)
                    .ToList();

                var positives = labels.Count(value => value == "対応");
                positivePairs += positives;
                if (positives > 0)
                {
                    correct++;
                }
                else if (labels.Any(value => value is "違う" or "参考"))
                {
                    wrong++;
                }
                else
                {
                    unknown++;
                }
            }
        }

        Console.WriteLine($"本文の共通素体の候補: 商品 {items} 件に {total} 件（登録簿にある素体 {registered}・新規 {total - registered}）");
        Console.WriteLine($"  正しい {correct} / 誤り {wrong} / 判定できない {unknown}（正しい候補がつなぐ正解「対応」の組 {positivePairs}）");
    }

    private static bool Belongs(AvatarRegistryEntry entry, AvatarBaseMention mention, string key, IReadOnlyDictionary<string, string> lookup)
    {
        var group = entry.BaseName ?? AvatarBaseKeys.InferBaseOf(entry, lookup);
        if (group is not null && string.Equals(group, mention.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        var texts = new[] { entry.BoothName, AvatarNames.ShownName(entry) }
            .Concat(entry.Aliases.Where(alias => !alias.Rejected).Select(alias => alias.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return texts.Any(text => AvatarBaseKeys.Key(text).Contains(key, StringComparison.Ordinal)
            || AvatarBaseKeys.MentionsIn(text).Contains(key));
    }
}
