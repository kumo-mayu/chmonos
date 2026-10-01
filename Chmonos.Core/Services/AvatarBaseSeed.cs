using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

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
}
