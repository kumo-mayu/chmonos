using System.Globalization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query.Search;

/// <summary>
/// 検索の式の葉の「条件」（画面の絞り込みの条件1つ。docs/spec/local-api-shapes.md「検索の式」）。
/// 種類ごとに使う欄だけを書く。**保存の形（<see cref="SearchModuleState"/>）をそのまま外へ出さない**：中を直しても外の約束が壊れないよう、
/// ここで受けて保存の形へ変換し、判定は画面と同じ <see cref="SearchModuleConditions"/> で作る。
/// 「除く」は式の <c>not</c> で書く（値の分からない商品も外す決まりは、画面の「除く」と同じに効く）。
/// </summary>
public sealed class ConditionRequest
{
    /// <summary>種類（<see cref="ConditionKinds"/> の名前）。</summary>
    public string? Kind { get; init; }

    /// <summary>積む値（カテゴリ・BOOTHタグ・ショップの鍵・フォルダ・Unity プロジェクト）。</summary>
    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>対応アバター：アバターの商品 ID。</summary>
    public IReadOnlyList<string>? Avatars { get; init; }

    /// <summary>対応アバター：共通素体の名前。</summary>
    public IReadOnlyList<string>? Bases { get; init; }

    /// <summary>積んだ値どうしのつなぎ方。<c>any</c>（既定）か <c>all</c>。</summary>
    public string? Match { get; init; }

    /// <summary>選ぶ条件の選択肢（<c>/v1/search/conditions</c> に一覧がある）。</summary>
    public string? Choice { get; init; }

    /// <summary>選ぶ条件の補助の切り替え（名前は <c>/v1/search/conditions</c>）。</summary>
    public IReadOnlyDictionary<string, bool>? Options { get; init; }

    /// <summary>対応アバター：素体経由の対応も含めるか（既定 true）。</summary>
    public bool? ViaBase { get; init; }

    /// <summary>対応アバター：対応している商品を出すか（既定 true）。</summary>
    public bool? Matched { get; init; }

    /// <summary>対応アバター：対応の指定が無い商品を出すか（既定 false）。</summary>
    public bool? Unspecified { get; init; }

    /// <summary>ショップ：お気に入りのショップの商品も当てるか。</summary>
    public bool? IncludeFavoriteShops { get; init; }

    /// <summary>範囲の下限（含む）。送らなければ制限しない。</summary>
    public long? Min { get; init; }

    /// <summary>範囲の上限（含む）。送らなければ制限しない。</summary>
    public long? Max { get; init; }

    /// <summary>価格の元：<c>paid</c>（払った額・既定）か <c>booth</c>（BOOTH の価格）。</summary>
    public string? Source { get; init; }

    /// <summary>価格：種類ごとの価格が全部範囲に入る商品だけ（既定 false：どれか1つが入れば当たる）。</summary>
    public bool? AllVariations { get; init; }

    /// <summary>価格：数の分からない商品も当てる（既定 false）。</summary>
    public bool? IncludeUnknown { get; init; }

    /// <summary>BOOTH の価格：桁違いに高い価格（外れ値）を照らす数から外す（既定 true）。</summary>
    public bool? IgnoreOutliers { get; init; }

    /// <summary>日付の始まり（含む）。送らなければ制限しない。</summary>
    public DateOnly? From { get; init; }

    /// <summary>日付の終わり（含む）。送らなければ制限しない。</summary>
    public DateOnly? To { get; init; }

    /// <summary>入手日：<c>first</c>（最初の購入だけ・既定）か <c>all</c>（購入ごとの日付のどれか）。</summary>
    public string? Scope { get; init; }

    /// <summary>属性：属性ごとの値の幅。</summary>
    public IReadOnlyList<AttributeRangeRequest>? Ranges { get; init; }

    /// <summary>ユーザータグ：大分類ごとの条件。</summary>
    public IReadOnlyList<UserTagRequest>? Tags { get; init; }

    /// <summary>改変：アバターごとの条件。</summary>
    public IReadOnlyList<ModificationRequest>? Modifications { get; init; }

    /// <summary>編集状況の見る項目・更新通知ありの見る種類。</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>最近：<c>used</c>（Unity送信・既定）・<c>viewed</c>（商品閲覧）・<c>added</c>（取り込み）。</summary>
    public string? Recent { get; init; }

    /// <summary>最近：何日前から（0 は今日）。</summary>
    public int? FromDays { get; init; }

    /// <summary>最近：何日前まで。送らなければそれより前もすべて。</summary>
    public int? ToDays { get; init; }
}

/// <summary>属性の範囲。省いた側は端まで（min は 0・max は 100。属性の値は 0〜100）。</summary>
/// <remarks>前は省けない数の欄で、省くと 0 と読んで 0〜0 を探し、黙って0件になった（2026-10-14・使い手の試作で見つかった）。値段の範囲と同じく片側を省ける</remarks>
public sealed record AttributeRangeRequest(string Name, int? Min = null, int? Max = null);

/// <param name="Subs">小分類（どれか・すべてかは <paramref name="Match"/>）。</param>
/// <param name="NoSub">小分類を付けていない物も当てる。</param>
public sealed record UserTagRequest(string Top, IReadOnlyList<string>? Subs = null, string? Match = null, bool NoSub = false);

/// <param name="Modifications">改変の ID。空ならそのアバターの改変のどれかに使った商品。</param>
public sealed record ModificationRequest(string Avatar, IReadOnlyList<string>? Modifications = null, string? Match = null);

/// <summary>API の条件の種類の名前と、画面の条件の種類の対応（名前は外との約束）。</summary>
public static class ConditionKinds
{
    public static IReadOnlyList<(string Name, SearchModuleKind Kind)> All { get; } =
    [
        ("category", SearchModuleKind.Category),
        ("boothTag", SearchModuleKind.BoothTag),
        ("shop", SearchModuleKind.Shop),
        ("wishCount", SearchModuleKind.WishList),
        ("price", SearchModuleKind.Price),
        ("saleStatus", SearchModuleKind.EndOfSale),
        ("publishedAt", SearchModuleKind.PublishedAt),
        ("adult", SearchModuleKind.Adult),
        ("owned", SearchModuleKind.Owned),
        ("gift", SearchModuleKind.Gift),
        ("freePaid", SearchModuleKind.FreePaid),
        ("userTag", SearchModuleKind.UserTag),
        ("attribute", SearchModuleKind.Attribute),
        ("avatar", SearchModuleKind.Avatar),
        ("favorite", SearchModuleKind.Favorite),
        ("acquiredAt", SearchModuleKind.AcquiredAt),
        ("hidden", SearchModuleKind.Hidden),
        ("editStatus", SearchModuleKind.Unedited),
        ("modification", SearchModuleKind.Modification),
        ("unityProject", SearchModuleKind.UnityProject),
        ("path", SearchModuleKind.Path),
        ("recent", SearchModuleKind.Recent),
        ("brokenZip", SearchModuleKind.BrokenZip),
        ("missingFile", SearchModuleKind.MissingFile),
        ("notOnBooth", SearchModuleKind.NotOnBooth),
        ("updateNotice", SearchModuleKind.Updated),
    ];

    public static SearchModuleKind? Of(string? name)
        => All.FirstOrDefault(pair => string.Equals(pair.Name, name, StringComparison.Ordinal)) is { Name: not null } found ? found.Kind : null;

    /// <summary>
    /// API の条件を保存の形へ変える。読めない値は黙って既定にせず <see cref="QueryException"/> にする
    /// （画面の状態は手で壊れた物を既定で読むが、API はプログラムが書くので、間違いを返した方が早く気付ける）。
    /// </summary>
    public static SearchModuleState ToState(ConditionRequest request)
    {
        var kind = Of(request.Kind) ?? throw QueryException.Invalid("unknownCondition", $"知らない条件の種類です: {request.Kind}");
        var state = new SearchModuleState { Kind = kind.ToString() };
        var matchAll = MatchAll(request.Match);

        if (SearchModuleConditions.ChoiceOf(kind) is { } choice)
        {
            var key = request.Choice ?? choice.Keys[0];
            if (!choice.Keys.Contains(key))
            {
                throw QueryException.Invalid("invalidChoice", $"{request.Kind} の選択肢にありません: {key}（{string.Join("・", choice.Keys)}）");
            }

            Dictionary<string, bool>? toggles = null;
            foreach (var (name, on) in request.Options ?? new Dictionary<string, bool>())
            {
                if (choice.Flag?.StateKey != name)
                {
                    throw QueryException.Invalid("invalidOption", $"{request.Kind} の切り替えにありません: {name}");
                }

                toggles = new Dictionary<string, bool> { [name] = on };
            }

            return state with { Choice = key, Toggles = toggles };
        }

        switch (kind)
        {
            case SearchModuleKind.Avatar:
                var showMatched = request.Matched ?? true;
                var showUnspecified = request.Unspecified ?? false;
                if (!showMatched && !showUnspecified)
                {
                    throw QueryException.Invalid("invalidCondition", "avatar の matched と unspecified を両方 false にはできません。");
                }

                return state with
                {
                    Items = [.. (request.Avatars ?? []).Select(id => SearchModuleConditions.AvatarKey + id), .. (request.Bases ?? []).Select(name => SearchModuleConditions.BaseKey + name)],
                    MatchAll = matchAll,
                    Flag = request.ViaBase ?? true,
                    ShowMatched = showMatched,
                    ShowUnspecified = showUnspecified,
                };

            case SearchModuleKind.Category or SearchModuleKind.BoothTag or SearchModuleKind.Shop or SearchModuleKind.UnityProject or SearchModuleKind.Path:
                return state with
                {
                    Items = request.Values ?? [],
                    MatchAll = matchAll,
                    IncludeFavorites = kind == SearchModuleKind.Shop && request.IncludeFavoriteShops == true,
                };

            case SearchModuleKind.WishList or SearchModuleKind.Price:
                var source = request.Source ?? SearchModuleConditions.PaidSource;
                if (kind == SearchModuleKind.Price && source is not (SearchModuleConditions.PaidSource or SearchModuleConditions.BoothSource))
                {
                    throw QueryException.Invalid("invalidCondition", $"price の source は paid か booth です: {source}");
                }

                return state with
                {
                    Choice = kind == SearchModuleKind.Price ? source : null,
                    Min = request.Min?.ToString(CultureInfo.InvariantCulture),
                    Max = request.Max?.ToString(CultureInfo.InvariantCulture),
                    MinEnabled = request.Min is not null,
                    MaxEnabled = request.Max is not null,
                    MatchAll = request.AllVariations == true,
                    Flag = request.IncludeUnknown == true,
                    IgnoreOutliers = request.IgnoreOutliers ?? true,
                };

            case SearchModuleKind.PublishedAt or SearchModuleKind.AcquiredAt:
                var scope = request.Scope ?? SearchModuleConditions.FirstPurchase;
                if (scope is not (SearchModuleConditions.FirstPurchase or SearchModuleConditions.AllPurchases))
                {
                    throw QueryException.Invalid("invalidCondition", $"scope は first か all です: {scope}");
                }

                return state with
                {
                    Choice = scope,
                    Min = request.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Max = request.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    MinEnabled = request.From is not null,
                    MaxEnabled = request.To is not null,
                };

            case SearchModuleKind.Attribute:
                return state with
                {
                    Ranges = (request.Ranges ?? []).Select(range => new AttributeRange(range.Name, range.Min ?? 0, range.Max ?? 100)).ToList(),
                    MatchAll = matchAll,
                };

            case SearchModuleKind.UserTag:
                return state with
                {
                    UserTags = (request.Tags ?? []).Select(tag => new UserTagCondition
                    {
                        Top = tag.Top,
                        Subs = tag.Subs ?? [],
                        NoSub = tag.NoSub,
                        MatchAll = MatchAll(tag.Match),
                    }).ToList(),
                    MatchAll = matchAll,
                };

            case SearchModuleKind.Modification:
                return state with
                {
                    Modifications = (request.Modifications ?? []).Select(group => new ModificationCondition
                    {
                        Avatar = group.Avatar,
                        Modifications = group.Modifications ?? [],
                        MatchAll = MatchAll(group.Match),
                    }).ToList(),
                    MatchAll = matchAll,
                };

            case SearchModuleKind.Unedited:
                var fields = request.Fields ?? [];
                foreach (var field in fields)
                {
                    if (!EditFieldsMissing.All.Any(known => EditFieldsMissing.KeyOf(known) == field))
                    {
                        throw QueryException.Invalid("invalidCondition", $"editStatus の項目にありません: {field}");
                    }
                }

                return state with
                {
                    Choice = Pick(request.Choice ?? EditStatusCondition.MissingKey, [EditStatusCondition.MissingKey, EditStatusCondition.FilledKey, EditStatusCondition.NeutralKey]),
                    Fields = fields,
                    MatchAll = matchAll,
                };

            case SearchModuleKind.Updated:
                var kinds = request.Fields ?? [];
                foreach (var name in kinds)
                {
                    if (!UpdateNoticeCondition.AllKinds.Any(known => UpdateNoticeCondition.KeyOf(known) == name))
                    {
                        throw QueryException.Invalid("invalidCondition", $"updateNotice の種類にありません: {name}");
                    }
                }

                return state with
                {
                    Choice = Pick(request.Choice ?? UpdateNoticeCondition.UpdatedKey, [UpdateNoticeCondition.UpdatedKey, UpdateNoticeCondition.OtherKey, UpdateNoticeCondition.NeutralKey]),
                    Fields = kinds,
                    MatchAll = matchAll,
                };

            case SearchModuleKind.Recent:
                if (request.FromDays is < 0 || request.ToDays is < 0)
                {
                    throw QueryException.Invalid("invalidCondition", "recent の日数は 0 以上です。");
                }

                return state with
                {
                    Choice = Pick(request.Recent ?? "used", ["used", "viewed", "added"]),
                    Min = (request.FromDays ?? 0).ToString(CultureInfo.InvariantCulture),
                    Max = request.ToDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                };

            default:
                throw QueryException.Invalid("unknownCondition", $"知らない条件の種類です: {request.Kind}");
        }
    }

    private static bool MatchAll(string? match) => match switch
    {
        null or "any" => false,
        "all" => true,
        _ => throw QueryException.Invalid("invalidCondition", $"match は any か all です: {match}"),
    };

    private static string Pick(string value, string[] allowed)
        => allowed.Contains(value)
            ? value
            : throw QueryException.Invalid("invalidChoice", $"選択肢にありません: {value}（{string.Join("・", allowed)}）");
}
