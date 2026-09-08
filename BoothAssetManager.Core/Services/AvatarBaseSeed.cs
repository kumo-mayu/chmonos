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
    /// 初期辞書の中身。
    /// <c>InferClothing = false</c> は部位規格で、一致しても衣装が合うとは限らないもの。
    /// </summary>
    public static IReadOnlyList<AvatarBaseGroup> Groups { get; } =
    [
        // 頭部の規格。カタログ上は最大の85体だが、体は揃わないので衣装は広げない
        new AvatarBaseGroup
        {
            Name = "+Head",
            InferClothing = false,
            Memo = "頭部の共通規格。頭を差し替えられるだけで、衣装が合うとは限りません。",
            Aliases = [new AvatarAlias { Text = "+Head", Source = "Seed" }],
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
