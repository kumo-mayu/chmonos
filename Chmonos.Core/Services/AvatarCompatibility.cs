using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

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

    /// <summary>
    /// 商品ごとに展開した結果。検索画面は1打鍵ごとに、選択肢の件数を数えるだけでも全商品を2〜3回展開していて、
    /// 2000件で1打鍵 3.4ms・割り当て 1.6MB（辞書と集合を毎回作る。2026-09-24 実測）、対応アバターを選んでいるとその数倍になる。
    /// 商品の記録（<see cref="LocalBlock"/>）は書き換えずに作り直す物なので、同じ記録の答えは変わらない。
    /// 記録が捨てられたら一緒に消えるよう、弱い参照の表に置く
    /// </summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalBlock, IReadOnlyDictionary<string, AvatarMatch>> _resolved = new();

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocalBlock, IReadOnlyDictionary<string, AvatarMatch>>.CreateValueCallback _resolve;

    private AvatarCompatibilityIndex()
    {
        _resolve = ResolveCore;
    }

    public static AvatarCompatibilityIndex Build(AvatarRegistry registry)
    {
        var index = new AvatarCompatibilityIndex();

        // 消した印の付いたグループは無い物として扱う（X1）
        foreach (var group in registry.BaseGroups.Where(group => !group.Rejected))
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

        // 手で決めた所属が無いアバターは、名前・別名の「#MARUBODY」「（えも研素体）」「+Head」から推す。
        // 手で決める道しか無かった頃は、所持207件の実データでも所属しているアバターが0体で、
        // 素体経由の対応が一度も働いていなかった。推した所属は保存しない（ここで毎回計算する）
        var lookup = AvatarBaseKeys.Lookup([.. registry.BaseGroups.Where(group => !group.Rejected)]);

        foreach (var entry in registry.Entries)
        {
            // アバターでないと分かっている物は、手で付けた素体名があっても一員に数えない（ユーザ判断 2026-10-03「1で良い」）。
            // 素体名は「アバターとして扱わない」にしても JSON に残るので、見ずに数えると、素体に対応した衣装の
            // 「素体経由で対応」にアバターでない物が並んだ。素体名は消さずに残し、扱いを戻せばまた一員になる。
            // カテゴリがまだ分からない記録（BOOTH から取っていないアバター）は判定が偽になるが、外すと
            // 用意した素体名や手で付けた所属が効かなくなるので、分かっている物だけを外す
            if (IsKnownNotAvatar(entry))
            {
                continue;
            }

            var baseName = !string.IsNullOrWhiteSpace(entry.BaseName)
                ? entry.BaseName!
                : AvatarService.IsAvatar(entry) ? AvatarBaseKeys.InferBaseOf(entry, lookup) : null;

            if (baseName is null)
            {
                continue;
            }

            index._baseOfAvatar[entry.ItemId] = baseName;

            if (!index._membersOfBase.TryGetValue(baseName, out var members))
            {
                members = [];
                index._membersOfBase[baseName] = members;
            }

            members.Add(entry.ItemId);
        }

        return index;
    }

    /// <summary>「アバターとして扱わない」にした物と、カテゴリが分かっていてアバターと判定されない物。</summary>
    internal static bool IsKnownNotAvatar(AvatarRegistryEntry entry)
        => entry.AvatarOverride == false
            || (!string.IsNullOrWhiteSpace(entry.Category) && !AvatarService.IsAvatar(entry));

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
    public IReadOnlyDictionary<string, AvatarMatch> Resolve(LocalBlock local) => _resolved.GetValue(local, _resolve);

    private IReadOnlyDictionary<string, AvatarMatch> ResolveCore(LocalBlock local)
    {
        var result = new Dictionary<string, AvatarMatch>(StringComparer.Ordinal);
        var basesToExpand = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var link in local.Avatars)
        {
            // 説明文のリンク（要確認）は数えない。中身はサムネに使ったアバター・クレジット・
            // 他の商品の紹介で、対応の宣言ではないことが多い（所持207件の実データで、
            // これを数えていたために絞り込みの約1割が誤りだった）。商品ページには要確認として出る
            if (link.Rejected || link.Source == AvatarLinkSource.H2Link)
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
