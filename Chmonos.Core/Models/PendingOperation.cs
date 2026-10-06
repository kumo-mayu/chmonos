using Chmonos.Core.Services;

namespace Chmonos.Core.Models;

/// <summary>やりかけの記録に書く操作の種類。</summary>
public enum PendingOperationKind
{
    /// <summary>商品まるごとを別のIDへ移す（<c>fromId</c>・<c>toId</c>・<c>skippedPurchases</c>）。</summary>
    ChangeItemId,

    /// <summary>ユーザータグの名前の変更・統合（<c>top</c>・<c>sub</c>・<c>newName</c>。<c>sub</c> が無ければ大分類）。</summary>
    RenameUserTag,

    /// <summary>属性の名前の変更・統合（<c>oldName</c>・<c>newName</c>・<c>keep</c>）。</summary>
    RenameAttribute,
}

/// <summary>
/// 始めたが、まだ終わっていない操作1件（<c>pending-operations.json</c> の1行。ユーザ判断 2026-10-06「A」）。
///
/// IDの変更・タグや属性の名前の変更は、一覧・商品・ほかの参照を順に何か所も書く。途中で落ちると、
/// 一部だけが新しい名前（ID）を指したまま残り、もう一度押しても元が無いので始められなかった。
/// 始める前にここへ書き、全部が終わったら消す。次の起動で残っていれば、同じ操作をもう一度当てて続きを済ませる
/// （どの段も、2回当てても同じ結果になる作り）。
///
/// 欄は種類ごとに使う物だけを書く（使わない欄は書かない）。IDの変更・名前の変更は画面から同時に2つ走り得るので、ファイルは一覧。
/// </summary>
public sealed record PendingOperation
{
    /// <summary>この記録の見分け（終わったときに自分の行だけを消すため）。</summary>
    public string Id { get; init; } = string.Empty;

    public PendingOperationKind Kind { get; init; }

    /// <summary>始めた日時。続きは古い順に当てる。</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>IDの変更：移す元の商品ID。</summary>
    public string? FromId { get; init; }

    /// <summary>IDの変更：移す先の商品ID。</summary>
    public string? ToId { get; init; }

    /// <summary>IDの変更：移さない購入記録の番号（移す元の並びで数える。二重計上と人が判断した物）。</summary>
    public IReadOnlyList<int>? SkippedPurchases { get; init; }

    /// <summary>タグ：大分類の名前（大分類の変更なら元の名前、小分類の変更ならその親）。</summary>
    public string? Top { get; init; }

    /// <summary>タグ：小分類の元の名前。大分類の変更なら書かない。</summary>
    public string? Sub { get; init; }

    /// <summary>属性：元の名前。</summary>
    public string? OldName { get; init; }

    /// <summary>タグ・属性：新しい名前。既にあれば統合になる。</summary>
    public string? NewName { get; init; }

    /// <summary>属性の統合：両方に値が入っていた商品でどちらを残すか。</summary>
    public AttributeMergeValue? Keep { get; init; }
}
