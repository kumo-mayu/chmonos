using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 画面・Core・外から使う API の3か所が、同じ保存した検索で同じ商品を返すかを見るための作り物（試験の点検 2026-11-28 の高1・高2）。
///
/// 画面（App の SearchFilterPass）・Core（SavedSearchConditions の SavedSearchMatcher）・API（SavedSearchExport から SearchEngine）は、
/// 条件のまとめ方（同じ種類の範囲は「どれか」・除くは全部通る・別の種類とは AND）と文字の照らし方を、それぞれ別に書いている。
/// 同じ判定のクラスを2回呼んで比べるだけでは、3か所がそろってずれても見えないので、期待は**手で数えた商品 ID** にする（出力の写しにしない）。
///
/// App.Tests（画面と Core）と Query.Tests（Core と API）の両方がこのファイルを読む（Query.Tests はリンクで取り込む）。
/// Core の型だけを使い、App の部品を使わない。
/// </summary>
public static class SearchParityFixture
{
    // 素体「作り物の素体」には 7100001・7100002 が属する。「空の素体」は一員がいない（宣言だけが手掛かりになる）
    public const string Base = "作り物の素体";
    public const string EmptyBase = "空の素体";

    /// <summary>手で数えた期待（ID の並びは小さい順）と、それを出す保存した検索。</summary>
    public sealed record Case(SearchHistoryEntry Entry, string[] Expected)
    {
        public string Name => Entry.Name!;

        public override string ToString() => Name;
    }

    /// <summary>
    /// 作り物の6件（どれもファイルを持ち、隠す・成人向けの印は無い）。
    ///
    /// | ID | 名前 | カテゴリ | BOOTHタグ | 払った額 | 対応 |
    /// |---|---|---|---|---|---|
    /// | 9600001 | サマードレス | 3D衣装 | 夏 | 1,000（上限ちょうど） | 7100001 に直接 |
    /// | 9600002 | 冬のコート | 3D衣装 | 冬 | 2,000（上限ちょうど） | 7100002 に直接（7100001 へは素体経由だけ） |
    /// | 9600003 | なつの帽子 | 3D小物 | — | 3,000 | 共通素体「作り物の素体」の宣言だけ（7100001・7100002 へ素体経由） |
    /// | 9600004 | 素材集 abc | テクスチャ | — | 500 | 指定なし |
    /// | 9600005 | 髪飾り ａｂｃ | 3D小物 | — | なし | 7100003 に直接（素体に属さない） |
    /// | 9600006 | 小物 ABC | 3D小物 | — | なし | 共通素体「空の素体」の宣言だけ（一員がいないので、どのアバターにも広がらない） |
    /// </summary>
    public static IReadOnlyList<ItemRecord> Items() =>
    [
        Item("9600001", "サマードレス", "3D衣装", ["夏"], 1000, local => local with { Avatars = [Direct("7100001")] }),
        Item("9600002", "冬のコート", "3D衣装", ["冬"], 2000, local => local with { Avatars = [Direct("7100002")] }),
        Item("9600003", "なつの帽子", "3D小物", [], 3000, local => local with { AvatarBases = [new AvatarBaseLink { BaseName = Base }] }),
        Item("9600004", "素材集 abc", "テクスチャ", [], 500),
        Item("9600005", "髪飾り ａｂｃ", "3D小物", [], null, local => local with { Avatars = [Direct("7100003")] }),
        Item("9600006", "小物 ABC", "3D小物", [], null, local => local with { AvatarBases = [new AvatarBaseLink { BaseName = EmptyBase }] }),
    ];

    public static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            new AvatarRegistryEntry { ItemId = "7100001", BoothName = "作り物のアバターA", Category = "3Dキャラクター", BaseName = Base },
            new AvatarRegistryEntry { ItemId = "7100002", BoothName = "作り物のアバターB", Category = "3Dキャラクター", BaseName = Base },
            new AvatarRegistryEntry { ItemId = "7100003", BoothName = "作り物のアバターC", Category = "3Dキャラクター" },
        ],
        BaseGroups = [new AvatarBaseGroup { Name = Base }, new AvatarBaseGroup { Name = EmptyBase }],
    };

    public static SavedSearchList SavedSearches() => new() { Entries = [.. Cases().Select(c => c.Entry)] };

    public static IReadOnlyList<Case> Cases() =>
    [
        // 同じ種類の範囲2つは「どれか」、除く範囲は全部通る、別の種類（対応アバター・素体経由を含める）とは AND。
        // 払った額：0〜1,000 に 9600001（上限ちょうど）・9600004、1,500〜2,000 に 9600002（上限ちょうど）。400〜600 を除くと 9600004 が落ちる。
        // 対応 7100001（素体経由を含める）は 9600001（直接）・9600002（兄弟の素体経由）・9600003（共通素体の宣言から）
        new(new SearchHistoryEntry
        {
            Name = "範囲のどれかと除くと対応",
            Modules =
            [
                Price("0", "1000"),
                Price("1500", "2000"),
                Price("400", "600") with { Exclude = true },
                Avatar(["avatar:7100001"], viaBase: true),

                // 効かせていない条件は読まない（読むと誰も当たらない）。画面は効いている条件しか保存しないので、
                // この形は手で直した JSON か前の版からしか来ない（試験の点検 2026-11-28 の保存した検索の中2）
                new SearchModuleState { Kind = "Category", Items = ["無いカテゴリ"], Enabled = false },
            ],
        }, ["9600001", "9600002"]),

        // 素体経由を切ると、7100001 に直接対応した物だけ（9600002 と 9600003 は素体経由だけなので外れる）
        new(new SearchHistoryEntry
        {
            Name = "素体経由を切る",
            Modules =
            [
                Avatar(["avatar:7100001"], viaBase: false),
                new SearchModuleState { Kind = "Category", Items = ["3D衣装", "3D小物"] },
            ],
        }, ["9600001"]),

        // 指定なしだけ：宣言が1つも無い物だけ。共通素体だけを宣言した物（9600003・一員のいない素体の 9600006）は入らない
        new(new SearchHistoryEntry
        {
            Name = "指定なしだけ",
            Modules = [new SearchModuleState { Kind = "Avatar", Flag = true, ShowMatched = false, ShowUnspecified = true }],
        }, ["9600004"]),

        // 3D小物（9600003・9600005・9600006）から、共通素体（素体経由を含める）に当たる物を除く。
        // 9600003 は素体を宣言しているので落ち、7100003（素体に属さない）の 9600005 と、別の素体の 9600006 が残る
        new(new SearchHistoryEntry
        {
            Name = "共通素体を除く",
            Modules =
            [
                new SearchModuleState { Kind = "Category", Items = ["3D小物"] },
                Avatar(["base:" + Base], viaBase: true) with { Exclude = true },
            ],
        }, ["9600005", "9600006"]),

        // 共通素体（素体経由を切る）：宣言した物だけ。一員の 7100001・7100002 に対応した物は入らない
        new(new SearchHistoryEntry
        {
            Name = "共通素体の宣言だけ",
            Modules = [Avatar(["base:" + Base], viaBase: false)],
        }, ["9600003"]),

        // ここから文字（高2）。対象：「夏」は 9600001 の BOOTHタグにだけあり、名前には無い。既定の対象（名前・ショップ・メモ）なら何にも当たらない
        new(new SearchHistoryEntry { Name = "対象はBOOTHタグ", Text = "夏", Targets = ["tag"] }, ["9600001"]),
        new(new SearchHistoryEntry { Name = "対象は既定", Text = "夏" }, []),

        // かな：「ナツ」は 9600003 の名前（ひらがなの「なつ」）に、かなを区別しないときだけ当たる
        new(new SearchHistoryEntry { Name = "かなを区別しない", Text = "ナツ", KanaInsensitive = true }, ["9600003"]),
        new(new SearchHistoryEntry { Name = "かなを区別する", Text = "ナツ" }, []),

        // 大文字小文字と全角半角：名前は「abc」（9600004）・全角の「ａｂｃ」（9600005）・「ABC」（9600006）
        new(new SearchHistoryEntry { Name = "どちらも区別しない", Text = "abc" }, ["9600004", "9600005", "9600006"]),
        new(new SearchHistoryEntry { Name = "大文字小文字を区別する", Text = "abc", CaseSensitive = true }, ["9600004", "9600005"]),
        new(new SearchHistoryEntry { Name = "全角半角を区別する", Text = "abc", WidthSensitive = true }, ["9600004", "9600006"]),
        new(new SearchHistoryEntry { Name = "どちらも区別する", Text = "abc", CaseSensitive = true, WidthSensitive = true }, ["9600004"]),

        // 文字と条件の両方：対象をタグにした「冬」と、3D衣装
        new(new SearchHistoryEntry
        {
            Name = "文字と条件",
            Text = "冬",
            Targets = ["tag"],
            Modules = [new SearchModuleState { Kind = "Category", Items = ["3D衣装"] }],
        }, ["9600002"]),
    ];

    private static SearchModuleState Price(string min, string max) => new()
    {
        Kind = "Price",
        Choice = SearchModuleConditions.PaidSource,
        Min = min,
        Max = max,

        // 外れ値の境は手元の全部の価格から決まる。6件しかない作り物で境に掛かると、範囲の意味と関係なく外れるので切る
        IgnoreOutliers = false,
    };

    private static SearchModuleState Avatar(string[] keys, bool viaBase) => new() { Kind = "Avatar", Items = keys, Flag = viaBase };

    private static AvatarLink Direct(string avatarId) => new() { AvatarItemId = avatarId, Source = AvatarLinkSource.SupportSection, Confirmed = true };

    private static ItemRecord Item(string id, string name, string category, string[] tags, int? paid, Func<LocalBlock, LocalBlock>? local = null)
    {
        var block = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = id.PadLeft(64, '0'), Paths = [$@"D:\files\{id}.zip"], SizeBytes = 1 }],
            Purchases = paid is { } price ? [new Purchase { Kind = PurchaseKind.ForSelf, Price = price }] : [],
        };
        return new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = name,
                Url = $"https://parity-shop.booth.pm/items/{id}",
                Shop = new BoothShop { Name = "parity-shop", Subdomain = "parity-shop", Url = "https://parity-shop.booth.pm/" },
                Category = new BoothCategory { Id = 1, Name = category },
                Tags = tags,
            },
            Local = local?.Invoke(block) ?? block,
        };
    }
}
