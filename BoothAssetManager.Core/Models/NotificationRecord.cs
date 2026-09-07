namespace BoothAssetManager.Core.Models;

/// <summary>「今すぐ困らないが知っておきたい」ことの種別。UIでは種別ごとにグループ表示する。</summary>
public enum NotificationKind
{
    /// <summary>商品ページの内容が変わった。</summary>
    ItemUpdated,

    /// <summary>対応アバターの推定が確認待ち。</summary>
    AvatarNeedsCheck,

    /// <summary>同じ中身のファイルが複数箇所で見つかった。</summary>
    DuplicateFile,

    /// <summary>マスタに存在しないappTag/属性を参照しているitemがある。</summary>
    OrphanTag,

    /// <summary>LocalFileが指すvariationがBOOTH側から消えた。</summary>
    OrphanVariationLink,

    /// <summary>説明文のセクションが取れない商品が急増した（BOOTH側の構造変化の疑い）。</summary>
    PageStructureChanged,
}

/// <summary>
/// 要確認1件（<c>notifications.json</c>）。確認しても即削除せず既読にし、
/// 上限を超えたら古い既読から捨てる。
/// </summary>
public sealed class NotificationRecord
{
    public required string Id { get; init; }

    public required NotificationKind Kind { get; init; }

    /// <summary>関連するBOOTH商品ID。商品に紐付かない通知（構造変化など）では null。</summary>
    public string? ItemId { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    /// <summary>
    /// 更新時の差分要約。旧データのスナップショットは保存しないので、
    /// 取得した瞬間に比較してここへ書き込む。
    /// </summary>
    public IReadOnlyList<NotificationDiff> Diffs { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public bool IsRead { get; init; }

    /// <summary>更新履歴セクションの変化など、注目度の高い通知か。</summary>
    public bool IsStrong { get; init; }
}

/// <summary>変わったセクション1つぶんの差分要約。</summary>
public sealed class NotificationDiff
{
    /// <summary>変わった箇所の名前（セクション見出し、または name / description）。</summary>
    public required string Field { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }
}
