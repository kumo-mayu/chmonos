using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 衣装とアバターの相性。
///
/// 3段目を「非対応」と呼ばないのは、検出の取りこぼしが2割以上あるため
/// （実測で再現率78%）。「対応情報が無い」と「非対応と確認済み」は別物で、
/// 前者を後者として見せると、着られるものを隠してしまう。
/// </summary>
public enum AvatarMatch
{
    /// <summary>その商品がこのアバターを名指ししている。</summary>
    Direct,

    /// <summary>同じ共通素体。素体を名指ししている場合と、同じ素体の別アバターを名指ししている場合。</summary>
    ViaBase,

    /// <summary>対応しているか分からない。着られないという意味ではない。</summary>
    Unknown,
}

/// <summary>
/// 素体経由の互換を展開するための索引。
///
/// item側の「対応アバター」は出品者が宣言したとおりのまま触らず、
/// 素体をまたぐ互換はここで毎回展開する（決定事項）。保存はしない。
/// </summary>
public sealed class AvatarCompatibilityIndex
{
    private readonly Dictionary<string, string> _baseOfAvatar = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _membersOfBase = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _baseOfItemId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _noInfer = new(StringComparer.CurrentCultureIgnoreCase);

    private AvatarCompatibilityIndex()
    {
    }

    public static AvatarCompatibilityIndex Build(AvatarRegistry registry)
    {
        var index = new AvatarCompatibilityIndex();

        foreach (var group in registry.BaseGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                continue;
            }

            index._membersOfBase.TryAdd(group.Name, []);

            if (!group.InferClothing)
            {
                index._noInfer.Add(group.Name);
            }

            // 素体そのものが商品として配布されている場合、その商品を名指しした衣装も
            // グループ全体に届くようにする
            if (!string.IsNullOrWhiteSpace(group.ItemId))
            {
                index._baseOfItemId[group.ItemId!] = group.Name;
            }
        }

        foreach (var entry in registry.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.BaseName))
            {
                continue;
            }

            index._baseOfAvatar[entry.ItemId] = entry.BaseName!;

            if (!index._membersOfBase.TryGetValue(entry.BaseName!, out var members))
            {
                members = [];
                index._membersOfBase[entry.BaseName!] = members;
            }

            members.Add(entry.ItemId);
        }

        return index;
    }

    /// <summary>この素体グループに属するアバター。</summary>
    public IReadOnlyList<string> MembersOf(string baseName)
        => _membersOfBase.TryGetValue(baseName, out var members) ? members : [];

    public string? BaseNameOf(string avatarItemId)
        => _baseOfAvatar.TryGetValue(avatarItemId, out var name) ? name : null;

    /// <summary>
    /// この商品が対応しているアバターを、直接対応と素体経由に分けて返す。
    ///
    /// 同じアバターに両方の経路があるときは直接対応を採る（強い方を残す）。
    /// ユーザが消した宣言（<see cref="AvatarLink.Rejected"/>）は最初から数えない。
    /// </summary>
    public IReadOnlyDictionary<string, AvatarMatch> Resolve(LocalBlock local)
    {
        var result = new Dictionary<string, AvatarMatch>(StringComparer.Ordinal);
        var basesToExpand = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var link in local.Avatars)
        {
            if (link.Rejected)
            {
                continue;
            }

            result[link.AvatarItemId] = AvatarMatch.Direct;

            // 名指しされたのが素体そのものの商品なら、そのグループ全体へ広げる
            if (_baseOfItemId.TryGetValue(link.AvatarItemId, out var groupOfItem))
            {
                basesToExpand.Add(groupOfItem);
            }

            // 名指しされたアバターが素体に属するなら、同じ素体の兄弟へ広げる
            if (_baseOfAvatar.TryGetValue(link.AvatarItemId, out var groupOfAvatar))
            {
                basesToExpand.Add(groupOfAvatar);
            }
        }

        foreach (var declared in local.AvatarBases)
        {
            if (declared.Rejected || string.IsNullOrWhiteSpace(declared.BaseName))
            {
                continue;
            }

            basesToExpand.Add(declared.BaseName);
        }

        foreach (var baseName in basesToExpand)
        {
            // 「衣装の互換を広げない」にした組は、素体が一致しても広げない
            if (_noInfer.Contains(baseName))
            {
                continue;
            }

            foreach (var member in MembersOf(baseName))
            {
                if (!result.ContainsKey(member))
                {
                    result[member] = AvatarMatch.ViaBase;
                }
            }
        }

        return result;
    }

    /// <summary>1体のアバターについての相性。</summary>
    public AvatarMatch MatchFor(LocalBlock local, string avatarItemId)
        => Resolve(local).TryGetValue(avatarItemId, out var match) ? match : AvatarMatch.Unknown;
}
