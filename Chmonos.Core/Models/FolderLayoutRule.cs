namespace Chmonos.Core.Models;

/// <summary>
/// フォルダの置き方の指定（<c>docs/spec/folder-layout.md</c>）。「<see cref="Root"/> の下は、ファイルから見て
/// <see cref="ItemNameLevel"/> 段上が商品名、<see cref="ShopNameLevel"/> 段上がショップ名」。未確定の自動検索がフォルダ名を手掛かりに使う。
/// 使う人が自分の並べ方を教える物で、推し量っては作らない（置き方に合わないファイルにフォルダ名を当てると外す。research §20）。
/// </summary>
public sealed record FolderLayoutRule
{
    /// <summary>置き方が効くフォルダ。この下のファイルに効く。</summary>
    public string Root { get; init; } = string.Empty;

    /// <summary>オフにすると、指定を残したまま使わない（合わないと分かったときに、消さずに止める）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>ファイルから見て何段上が商品名か（1〜<see cref="MaxLevel"/>）。使わないなら null。</summary>
    public int? ItemNameLevel { get; init; } = 1;

    /// <summary>ファイルから見て何段上がショップ名か（1〜<see cref="MaxLevel"/>）。使わないなら null。</summary>
    public int? ShopNameLevel { get; init; } = 2;

    /// <summary>
    /// 選べる段の深さ。商品名とショップ名の2つに、間に1段（「ショップ\版\商品」など）を挟めれば足りる。
    /// 深くするほど、置き方に合わないファイルで関係の無いフォルダ名を拾う
    /// </summary>
    public const int MaxLevel = 3;

    /// <summary>指定として効くか（段が範囲にあり、どちらか1つは使う）。</summary>
    public bool IsUsable
        => Root.Length > 0
            && (ItemNameLevel is >= 1 and <= MaxLevel || ShopNameLevel is >= 1 and <= MaxLevel)
            && ItemNameLevel != ShopNameLevel;
}
