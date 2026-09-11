using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 共通素体の初期辞書。
///
/// 素体の関係はBOOTHのデータから機械的には取れない（本文の「素体」はほぼ
/// 「裸の身体メッシュ」の意味で使われていて、共通素体の意味では出てこない）。
/// 空から始めると育つまで何も出ないので、コミュニティで名前が定着している
/// 少数だけを最初から置いておく。
///
/// ここに入れるのは「BOOTHのタグとして実在し、複数ショップが使っている呼び名」だけ。
/// 所属アバターの一覧は入れない（それは他所の整理を持ち込むことになる）。
/// 名前があれば、あとはタグ照合で勝手に育つ。
/// </summary>
public static class AvatarBaseSeed
{
    /// <summary>
    /// 以前の初期辞書が +Head に付けていたメモ。**これと一致するものだけを直す**目印に使う。
    ///
    /// 以前は +Head を「頭部だけの規格」とみなして衣装の互換を広げない設定で配っていたが、
    /// 体の共通素体だった（2026-09-11 ユーザが調べて確認。実データでもネイルの出品者が
    /// 「+head素体アバター」と体ごと対応を書いていた）。
    /// </summary>
    private const string LegacyPlusHeadMemo = "頭部の共通規格。頭を差し替えられるだけで、衣装が合うとは限りません。";

    /// <summary>
    /// 初期辞書の中身。全部、衣装の互換を広げる（既定）。
    /// 広げない設定（<c>InferClothing = false</c>）は仕組みとして残してあり、利用者が組ごとに切り替えられる。
    /// </summary>
    public static IReadOnlyList<AvatarBaseGroup> Groups { get; } =
    [
        // 体の共通素体。カタログ上は最大の85体
        new AvatarBaseGroup
        {
            Name = "+Head",
            Memo = "体の共通素体。+Head のアバター同士は、同じ衣装を着られることが多いです。",
            Aliases =
            [
                new AvatarAlias { Text = "+Head", Source = "Seed" },
                new AvatarAlias { Text = "PlusHead", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "まめふれんず",
            Memo = "配布されている共通素体。複数のショップのアバターが採用しています。",
            Aliases =
            [
                new AvatarAlias { Text = "まめふれんず共通素体", Source = "Seed" },
                new AvatarAlias { Text = "まめふれんず素体", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "珍飯亭",
            Memo = "男性向けの共通素体（T-BODY）。対応衣装を他ショップも出しています。",
            Aliases =
            [
                new AvatarAlias { Text = "珍飯亭共通素体", Source = "Seed" },
                new AvatarAlias { Text = "珍飯亭素体", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "えも研",
            Aliases =
            [
                new AvatarAlias { Text = "えも研素体", Source = "Seed" },
                new AvatarAlias { Text = "えも研共通素体", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "AVAKIN",
            Aliases = [new AvatarAlias { Text = "AVAKIN共通素体", Source = "Seed" }],
        },

        new AvatarBaseGroup
        {
            Name = "メタクリ",
            Aliases =
            [
                new AvatarAlias { Text = "メタクリ共通素体", Source = "Seed" },
                new AvatarAlias { Text = "メタクリ共通素体対応", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "ちびてぐ",
            Aliases =
            [
                new AvatarAlias { Text = "ちびてぐ素体", Source = "Seed" },
                new AvatarAlias { Text = "ちびてぐ素体対応", Source = "Seed" },
            ],
        },

        new AvatarBaseGroup
        {
            Name = "FLASTORE",
            Aliases = [new AvatarAlias { Text = "FLASTORE共通素体", Source = "Seed" }],
        },

        // 2026-09-11 に足した。所持207件の実データで、複数のショップがタグ「MARUBODY」「まるぼでぃ」を使い、
        // 名前に「#MARUBODY」を持つアバターが8体あった。読みと綴りが別の語なので別名でつなぐ
        new AvatarBaseGroup
        {
            Name = "MARUBODY",
            Memo = "配布されている共通素体。「まるぼでぃ」とも書かれます。",
            Aliases =
            [
                new AvatarAlias { Text = "まるぼでぃ", Source = "Seed" },
                new AvatarAlias { Text = "MARUBODY", Source = "Seed" },
            ],
        },
    ];

    /// <summary>
    /// 登録簿に初期辞書を足す。既にある名前には触らない
    /// （ユーザが直した設定を上書きしない）。
    /// </summary>
    /// <returns>足したグループ数。</returns>
    public static int Merge(List<AvatarBaseGroup> groups)
    {
        RepairLegacy(groups);

        var known = groups.Select(group => group.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var added = 0;

        foreach (var seed in Groups.Where(seed => !known.Contains(seed.Name)))
        {
            groups.Add(seed);
            added++;
        }

        return added;
    }

    /// <summary>
    /// 以前の初期辞書が配った誤った値を直す。直した組の数を返す。
    ///
    /// **初期辞書のまま（広げない＋以前のメモ）のものだけを直す。**利用者が切り替えたり
    /// メモを書き換えたりしたものは、その人の判断なので触らない。
    /// 別名は利用者が足しているかもしれないので残し、足りない初期の別名だけを足す。
    /// </summary>
    public static int RepairLegacy(List<AvatarBaseGroup> groups)
    {
        var repaired = 0;
        var seed = Groups.Single(group => group.Name == "+Head");

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (!string.Equals(group.Name, seed.Name, StringComparison.CurrentCultureIgnoreCase)
                || group.InferClothing
                || !string.Equals(group.Memo, LegacyPlusHeadMemo, StringComparison.Ordinal))
            {
                continue;
            }

            var aliases = group.Aliases.ToList();
            aliases.AddRange(seed.Aliases.Where(alias => !aliases.Any(existing =>
                string.Equals(existing.Text, alias.Text, StringComparison.CurrentCultureIgnoreCase))));

            groups[i] = group with { InferClothing = true, Memo = seed.Memo, Aliases = aliases };
            repaired++;
        }

        return repaired;
    }
}
