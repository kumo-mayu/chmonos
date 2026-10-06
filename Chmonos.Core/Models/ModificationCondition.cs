namespace Chmonos.Core.Models;

/// <summary>
/// 検索の絞り込みの「改変」で、アバター1体ぶんの条件（ユーザ判断 2026-10-06）。ユーザータグの <see cref="UserTagCondition"/> と同じ2段の形：
/// 1段目にアバター、2段目にそのアバターの改変。改変を1つも足していなければ「そのアバターの改変のどれかに使った商品」で絞る。
/// 前は「アバター（この改変すべて）」と「アバター：改変」を1本の候補に混ぜていて、同じアバターの改変どうしを AND で結べなかった。
/// </summary>
public sealed record ModificationCondition
{
    /// <summary>アバターの商品ID。</summary>
    public required string Avatar { get; init; }

    /// <summary>足した改変のID（名前は変えられるので ID で持つ）。</summary>
    public IReadOnlyList<string> Modifications { get; init; } = [];

    /// <summary>足した改変を全部満たす（AND）か。false はどれか（OR）。アバターごとに選ぶ。</summary>
    public bool MatchAll { get; init; }
}
