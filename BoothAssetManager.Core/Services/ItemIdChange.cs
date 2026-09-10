using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>移し替えで捨てるものの理由。名指しで出すために種類を分ける。</summary>
public enum DroppedReason
{
    /// <summary>バリエーションへの紐付け。移した先のバリエーションIDは別物なので持って行けない。</summary>
    VariationLink,

    /// <summary>この商品の説明文から検出したもの。移さず、新しい商品で検出し直す。</summary>
    Detected,

    /// <summary>取得の状態。新しい商品のものが正しい。</summary>
    FetchState,
}

/// <summary>移せなかったもの1件。**名指しで出す**ためにあり、件数だけにしない。</summary>
public sealed record DroppedThing
{
    public required DroppedReason Reason { get; init; }

    /// <summary>画面にそのまま出す説明。</summary>
    public required string Text { get; init; }
}

/// <summary>
/// 移すと二重計上に見えるかもしれない購入記録。
///
/// **金額が同じものを疑う。**仮IDの商品は「バリエーションを指定しない購入」しか
/// 持てないので、移した先の記録とはバリエーションで突き合わせられない。
/// 残る手掛かりが金額しかない。
/// </summary>
public sealed record DuplicatePurchase
{
    /// <summary>移す側の何番目の記録か。ユーザが「これは移さない」と選ぶための番号。</summary>
    public required int Index { get; init; }

    public required int Price { get; init; }

    public required PurchaseKind Kind { get; init; }

    /// <summary>移した先に既にある、同じ金額の記録の数。</summary>
    public required int MatchingAtTarget { get; init; }
}

/// <summary>
/// IDを変更したら何が起きるかの下見。**書き込む前に全部見せる。**
///
/// 「移せないものを名指しで出す」ためにあり、押したあとで気付く形にしない。
/// </summary>
public sealed record ItemIdChangePlan
{
    public required string FromId { get; init; }

    public required string ToId { get; init; }

    /// <summary>移した先の名前。BOOTHから取れていなければ null。</summary>
    public string? TargetName { get; init; }

    /// <summary>移した先が既に手元にあるか。無ければ新しく作る。</summary>
    public required bool TargetExistsLocally { get; init; }

    /// <summary>移した先がBOOTHで見つかったか。**見つからなくても止めない。**</summary>
    public required bool TargetFoundOnBooth { get; init; }

    public required int FileCount { get; init; }

    public required int FolderCount { get; init; }

    public required int PurchaseCount { get; init; }

    /// <summary>移せないもの。名指しで出す。</summary>
    public IReadOnlyList<DroppedThing> Dropped { get; init; } = [];

    /// <summary>二重計上に見えるかもしれない購入記録。ユーザに聞く唯一の点。</summary>
    public IReadOnlyList<DuplicatePurchase> Duplicates { get; init; } = [];

    /// <summary>移せるものが1つも無い（元の商品が空）。</summary>
    public bool IsEmpty => FileCount == 0 && FolderCount == 0 && PurchaseCount == 0;
}

/// <summary>移し替えの結果。</summary>
public enum ItemIdChangeOutcome
{
    Moved,

    /// <summary>元の商品が手元に無い。</summary>
    SourceMissing,

    /// <summary>同じIDへ移そうとした。</summary>
    SameId,

    /// <summary>移した先を用意できなかった。</summary>
    TargetUnavailable,
}

/// <summary>
/// 商品まるごとを別のIDへ移すときの、移せるもの／移せないものの決まり。
///
/// **「この商品から外す」とは別の操作。**あちらは*ファイル*を動かすもので、
/// こちらは*商品ごと*を動かすもの。同じボタンに畳むと、押した結果が
/// 「メモが残る／残らない」で変わることになる。
///
/// **IDは書き換えない。**新しいIDの商品へ中身を移し、元の商品を消す。
/// 商品IDはファイル名にもフォルダ名にもなっていて、他の商品からも名前で
/// 参照されているので、IDだけ書き換えると参照が全部迷子になる。
/// </summary>
public static class ItemIdChange
{
    /// <summary>
    /// 何が起きるかを先に組み立てる。**ここでは何も書かない。**
    /// </summary>
    public static ItemIdChangePlan Plan(ItemRecord source, ItemRecord? target, string toId, bool foundOnBooth)
    {
        var dropped = new List<DroppedThing>();

        // バリエーションへの紐付け。**見落とすと危険**——移した先で別のバリエーションの
        // ファイルとして表示され、数字が偶然一致すると間違ったまま黙って通る
        var linkedFiles = source.Local.LocalFiles.Count(file => file.VariationId is not null);
        if (linkedFiles > 0)
        {
            dropped.Add(new DroppedThing
            {
                Reason = DroppedReason.VariationLink,
                Text = $"ファイル {linkedFiles} 件の「どのバリエーションか」の指定",
            });
        }

        var linkedPurchases = source.Local.Purchases.Count(purchase => purchase.VariationId is not null);
        if (linkedPurchases > 0)
        {
            dropped.Add(new DroppedThing
            {
                Reason = DroppedReason.VariationLink,
                Text = $"購入記録 {linkedPurchases} 件の「どのバリエーションか」の指定（金額は残ります）",
            });
        }

        // 検出したもの。この商品の説明文から取ったので、新しい商品では取り直す
        var avatarCount = source.Local.Avatars.Count + source.Local.AvatarBases.Count;
        if (avatarCount > 0)
        {
            var manual = source.Local.Avatars.Count(link => link.Source == AvatarLinkSource.Manual);
            var text = $"対応アバターの記録 {avatarCount} 件";
            if (manual > 0)
            {
                text += $"（うち手で指定した {manual} 件も消えます）";
            }

            dropped.Add(new DroppedThing
            {
                Reason = DroppedReason.Detected,
                Text = text + "。移した先で検出し直せます",
            });
        }

        // 取得の状態。新しい商品のものが正しい
        if (source.Booth.WasEverFetched || source.Local.LastFetchedAt is not null)
        {
            dropped.Add(new DroppedThing
            {
                Reason = DroppedReason.FetchState,
                Text = "BOOTHから取った情報と、取得の記録（最終取得・次回予定・販売終了の印）",
            });
        }

        return new ItemIdChangePlan
        {
            FromId = source.Id,
            ToId = toId,
            TargetName = target?.DisplayName,
            TargetExistsLocally = target is not null,
            TargetFoundOnBooth = foundOnBooth,
            FileCount = source.Local.LocalFiles.Count,
            FolderCount = source.Local.LocalFolders.Count,
            PurchaseCount = source.Local.Purchases.Count,
            Dropped = dropped,
            Duplicates = FindDuplicates(source, target),
        };
    }

    /// <summary>
    /// 二重計上に見えるかもしれない購入記録を探す。
    ///
    /// **金額が一致するものだけを疑う。**バリエーションでは突き合わせられないので、
    /// 残る手掛かりが金額しかない。種類（自分用・贈った・貰った）が違えば
    /// 別の買い物なので疑わない。金額が未入力のものも突き合わせようがないので疑わない。
    /// </summary>
    private static List<DuplicatePurchase> FindDuplicates(ItemRecord source, ItemRecord? target)
    {
        if (target is null || target.Local.Purchases.Count == 0)
        {
            return [];
        }

        var found = new List<DuplicatePurchase>();

        for (var index = 0; index < source.Local.Purchases.Count; index++)
        {
            var purchase = source.Local.Purchases[index];
            if (purchase.Price is not { } price)
            {
                continue;
            }

            var matching = target.Local.Purchases.Count(
                other => other.Price == price && other.Kind == purchase.Kind);

            if (matching > 0)
            {
                found.Add(new DuplicatePurchase
                {
                    Index = index,
                    Price = price,
                    Kind = purchase.Kind,
                    MatchingAtTarget = matching,
                });
            }
        }

        return found;
    }

    /// <summary>
    /// 移したあとの <c>Local</c> を組み立てる。
    ///
    /// **移した先が持っている入力は潰さない。**そちらもユーザが入れたものだから。
    /// 持っていない項目だけ、移す側から引き継ぐ。
    /// </summary>
    /// <param name="skipped">移さない購入記録の番号（二重計上と判断したもの）。</param>
    public static LocalBlock Merge(LocalBlock source, LocalBlock target, IReadOnlySet<int> skipped)
    {
        var purchases = source.Purchases
            .Where((_, index) => !skipped.Contains(index))

            // バリエーションIDは持って行けない。移した先のIDは別物なので、
            // 指したまま移すと**間違ったまま黙って通る**
            .Select(purchase => purchase with { VariationId = null })
            .Concat(target.Purchases)
            .ToList();

        var files = LocalFileMerger.Merge(
            target.LocalFiles,
            source.LocalFiles.Select(file => file with { VariationId = null }).ToList());

        var folders = target.LocalFolders
            .Concat(source.LocalFolders.Where(folder => !target.LocalFolders.Any(existing =>
                string.Equals(existing.Path, folder.Path, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        return target with
        {
            DisplayName = target.DisplayName ?? source.DisplayName,
            Shop = target.Shop ?? source.Shop,
            Category = target.Category ?? source.Category,

            UserTags = target.UserTags.Count > 0 ? target.UserTags : source.UserTags,
            Attributes = target.Attributes.Count > 0 ? target.Attributes : source.Attributes,
            Memo = JoinMemo(target.Memo, source.Memo),
            AcquiredAt = target.AcquiredAt ?? source.AcquiredAt,

            // 着せた記録は検出が触らないユーザの記録なので、そのまま持って行く
            UsedOn = target.UsedOn
                .Concat(source.UsedOn.Where(usage => !target.UsedOn.Any(existing =>
                    string.Equals(existing.AvatarItemId, usage.AvatarItemId, StringComparison.Ordinal))))
                .ToList(),

            // 知らせるかは「片方が切っていたら切る」。勝手に通知を復活させない
            NotifyOnUpdate = target.NotifyOnUpdate && source.NotifyOnUpdate,

            // 非表示は「片方が隠していたら隠す」。勝手に表に出さない
            IsHidden = target.IsHidden || source.IsHidden,

            Purchases = purchases,
            LocalFiles = files,
            LocalFolders = folders,
        };
    }

    /// <summary>
    /// メモは**どちらも捨てない**。片方だけ残すと、書いたことが黙って消える。
    /// </summary>
    private static string? JoinMemo(string? target, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return target;
        }

        return string.IsNullOrWhiteSpace(target) ? source : $"{target}\n\n{source}";
    }
}
