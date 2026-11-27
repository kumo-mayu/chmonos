using System.Text.Json.Serialization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query.Search;

/// <summary>保存した検索の中身を、<c>POST /v1/search</c> にそのまま渡せる形にした物（<c>where</c>・<c>textOptions</c>）。
/// 中の型（式・条件・照らし方）は値のある欄だけを書く（ユーザ判断 2026-11-27・B：条件1つに null の欄が約40並び、読みにくい。本文としては省いても同じ意味）。</summary>
/// <param name="Where">検索文字列と条件を AND でつないだ式。何も絞っていなければ null。</param>
/// <param name="TextOptions">検索文字列の照らし方（対象・区別）。文字が無ければ null。</param>
public sealed record SavedSearchQuery([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SearchExpression? Where, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TextOptionsRequest? TextOptions);

/// <summary>式に入れられなかった物1つ。</summary>
/// <param name="Kind">保存の形の種類の名前（API に無い種類はそのまま。別表記は <c>searchAlternates</c>）。</param>
/// <param name="Reason">
/// <c>unsupportedKind</c>（API の条件に無い種類）・<c>unsupportedValue</c>（API の形で書けない値）・
/// <c>recordlessRecent</c>（条件「最近」の「記録の無い商品も表示」）・<c>searchAlternates</c>（別表記。API は広げない）。
/// </param>
public sealed record UnconvertedPart(string Kind, string Reason);

/// <summary>
/// 保存した検索（<see cref="SearchHistoryEntry"/>）を API の検索の式へ戻す（docs/spec/local-api-internal.md ⑤）。
/// <see cref="ConditionKinds.ToState"/> の逆向き。照らす単位は条件「保存した検索」（Core の <c>SavedSearchMatcher</c>）と同じにする：
/// 効かせていない・絞らない条件は入れず、範囲・日付の同じ種類は「どれかに当たる」の or にまとめ、除くは <c>not</c> で書く。
/// 書けない物は式から落として <see cref="UnconvertedPart"/> で言う（黙って広げない）。
/// 試験で、戻した式の答えが条件 <c>savedSearch</c> の答えと同じことを確かめる
/// </summary>
public static class SavedSearchExport
{
    public static (SavedSearchQuery Query, IReadOnlyList<UnconvertedPart> Unconverted) Of(SearchHistoryEntry entry, SearchModuleContext context)
    {
        var unconverted = new List<UnconvertedPart>();
        var leaves = new List<SearchExpression>();
        TextOptionsRequest? textOptions = null;

        var text = entry.Text.Trim();
        if (text.Length > 0)
        {
            leaves.Add(new SearchExpression { Text = text });
            var targets = entry.Targets.Where(name => SearchQuery.FieldNames.ContainsKey(name)).Distinct(StringComparer.Ordinal).ToList();

            // かなは画面の既定（区別する）を明かに書く（textOptions を送ると、省いた kanaSensitive は false で読まれる）
            textOptions = new TextOptionsRequest(targets.Count > 0 ? targets : null, entry.CaseSensitive, entry.WidthSensitive, KanaSensitive: !entry.KanaInsensitive);
            if (entry.SearchAlternates)
            {
                unconverted.Add(new UnconvertedPart("searchAlternates", "searchAlternates"));
            }
        }

        // 画面の絞り込みの1回と同じ単位（SavedSearchMatcher と同じ並べ方）
        var units = new List<List<(SearchExpression Leaf, bool Excluded)>>();
        var byKind = new Dictionary<SearchModuleKind, List<(SearchExpression, bool)>>();
        foreach (var state in entry.Modules)
        {
            if (state is null || !state.Enabled || !Enum.TryParse<SearchModuleKind>(state.Kind, out var kind) || !Enum.IsDefined(kind))
            {
                if (state is { Enabled: true })
                {
                    unconverted.Add(new UnconvertedPart(state.Kind, "unsupportedKind"));
                }

                continue;
            }

            var condition = SearchModuleConditions.From(state);
            if (condition is null)
            {
                continue;
            }

            condition.Prepare(context);
            if (!condition.IsActive)
            {
                continue;
            }

            var (request, reason) = Request(kind, state, DateOnly.FromDateTime(context.Now.LocalDateTime));
            if (request is null)
            {
                unconverted.Add(new UnconvertedPart(ApiName(kind) ?? state.Kind, reason!));
                continue;
            }

            var member = (new SearchExpression { Condition = request }, SearchModuleConditions.SupportsExclude(kind) && state.Exclude);
            if (SearchModuleConditions.OrSameKind(kind) && byKind.TryGetValue(kind, out var unit))
            {
                unit.Add(member);
                continue;
            }

            unit = [member];
            if (SearchModuleConditions.OrSameKind(kind))
            {
                byKind[kind] = unit;
            }

            units.Add(unit);
        }

        foreach (var unit in units)
        {
            var included = unit.Where(member => !member.Excluded).Select(member => member.Leaf).ToList();
            if (included.Count == 1)
            {
                leaves.Add(included[0]);
            }
            else if (included.Count > 1)
            {
                leaves.Add(new SearchExpression { Or = included });
            }

            leaves.AddRange(unit.Where(member => member.Excluded).Select(member => new SearchExpression { Not = member.Leaf }));
        }

        var where = leaves.Count switch
        {
            0 => null,
            1 => leaves[0],
            _ => new SearchExpression { And = leaves },
        };
        return (new SavedSearchQuery(where, textOptions), unconverted);
    }

    private static string? ApiName(SearchModuleKind kind)
        => ConditionKinds.All.FirstOrDefault(pair => pair.Kind == kind).Name;

    /// <summary>条件1つを API の形へ。書けなければ理由を返す。</summary>
    private static (ConditionRequest? Request, string? Reason) Request(SearchModuleKind kind, SearchModuleState state, DateOnly today)
    {
        if (ApiName(kind) is not { } name)
        {
            return (null, "unsupportedKind");
        }

        string? Match(bool all) => all ? "all" : null;

        if (SearchModuleConditions.ChoiceOf(kind) is { } choice)
        {
            var key = choice.Keys.Contains(state.Choice ?? string.Empty) ? state.Choice! : choice.Keys[0];
            IReadOnlyDictionary<string, bool>? options = choice.Flag is { } spec && state.Toggles is { } toggles && toggles.TryGetValue(spec.StateKey, out var on)
                ? new Dictionary<string, bool> { [spec.StateKey] = on }
                : null;
            return (new ConditionRequest { Kind = name, Choice = key, Options = options }, null);
        }

        switch (kind)
        {
            case SearchModuleKind.Avatar:
                if (!SplitTargets(state.Items, string.Empty, out var avatars, out var bases))
                {
                    return (null, "unsupportedValue");
                }

                // 両方切った状態は作らない（Core の From と同じ読み方）
                var showUnspecified = state.ShowUnspecified;
                return (new ConditionRequest
                {
                    Kind = name,
                    Avatars = avatars,
                    Bases = bases,
                    Match = Match(state.MatchAll),
                    ViaBase = state.Flag,
                    Matched = state.ShowMatched || !showUnspecified,
                    Unspecified = showUnspecified,
                    OwnedVariationsOnly = Toggle(state, SearchModuleConditions.OwnedVariationsToggle, false),
                    ViaConversion = Toggle(state, SearchModuleConditions.ViaConversionToggle, false),
                }, null);

            case SearchModuleKind.Profile:
                var sources = state.Items.Where(key => key.StartsWith(SearchModuleConditions.SourceKey, StringComparison.Ordinal)).ToList();
                var destinations = state.Items.Where(key => key.StartsWith(SearchModuleConditions.DestinationKey, StringComparison.Ordinal)).ToList();
                if (sources.Count + destinations.Count != state.Items.Count
                    || !SplitTargets(sources, SearchModuleConditions.SourceKey, out var sourceAvatars, out var sourceBases)
                    || !SplitTargets(destinations, SearchModuleConditions.DestinationKey, out var destinationAvatars, out var destinationBases))
                {
                    return (null, "unsupportedValue");
                }

                return (new ConditionRequest
                {
                    Kind = name,
                    SourceAvatars = sourceAvatars,
                    SourceBases = sourceBases,
                    DestinationAvatars = destinationAvatars,
                    DestinationBases = destinationBases,
                    ViaBase = state.Flag,
                    IncludeUnconfirmed = Toggle(state, SearchModuleConditions.IncludeUnconfirmedToggle, true),
                    ShowCounterpart = Toggle(state, SearchModuleConditions.CounterpartToggle, false),
                    RequireBoth = Toggle(state, SearchModuleConditions.RequireBothToggle, false),
                }, null);

            case SearchModuleKind.Category or SearchModuleKind.BoothTag or SearchModuleKind.Shop or SearchModuleKind.UnityProject or SearchModuleKind.Path:
                return (new ConditionRequest
                {
                    Kind = name,
                    Values = state.Items,
                    Match = Match(state.MatchAll),
                    IncludeFavoriteShops = kind == SearchModuleKind.Shop && state.IncludeFavorites ? true : null,
                }, null);

            case SearchModuleKind.WishList or SearchModuleKind.Price:
                var min = state.MinEnabled ? SearchModuleConditions.ParseAmount(state.Min) : null;
                var max = state.MaxEnabled ? SearchModuleConditions.ParseAmount(state.Max) : null;

                // 境を効かせたまま空欄なら手元の端（値の無い商品だけが外れる）。API は境が無いと絞らないので、下の端 0 で同じにする（数は 0 以上）
                if (min is null && max is null)
                {
                    min = 0;
                }

                return (new ConditionRequest
                {
                    Kind = name,
                    Min = min,
                    Max = max,
                    Source = kind == SearchModuleKind.Price ? (state.Choice == SearchModuleConditions.BoothSource ? SearchModuleConditions.BoothSource : SearchModuleConditions.PaidSource) : null,
                    AllVariations = state.MatchAll ? true : null,
                    IncludeUnknown = state.Flag ? true : null,
                    IgnoreOutliers = state.IgnoreOutliers,
                }, null);

            case SearchModuleKind.PublishedAt or SearchModuleKind.AcquiredAt:
                var from = state.MinEnabled ? DateText.Parse(state.Min ?? string.Empty, isEnd: false, today) : null;
                var to = state.MaxEnabled ? DateText.Parse(state.Max ?? string.Empty, isEnd: true, today) : null;

                // 空欄・読めない境は手元の端（日付の無い商品だけが外れる）。値と同じく、いちばん前の日で効かせたままにする
                if (from is null && to is null)
                {
                    from = DateOnly.MinValue;
                }

                return (new ConditionRequest
                {
                    Kind = name,
                    From = from,
                    To = to,
                    Scope = state.Choice == SearchModuleConditions.AllPurchases ? SearchModuleConditions.AllPurchases : null,
                }, null);

            case SearchModuleKind.Attribute:
                return (new ConditionRequest
                {
                    Kind = name,
                    Ranges = state.Ranges.Select(range => new AttributeRangeRequest(range.Name, range.Min, range.Max)).ToList(),
                    Match = Match(state.MatchAll),
                }, null);

            case SearchModuleKind.UserTag:
                return (new ConditionRequest
                {
                    Kind = name,
                    Tags = state.UserTags.Select(tag => new UserTagRequest(tag.Top, tag.Subs, Match(tag.MatchAll), tag.NoSub)).ToList(),
                    Match = Match(state.MatchAll),
                }, null);

            case SearchModuleKind.Modification:
                return (new ConditionRequest
                {
                    Kind = name,
                    Modifications = state.Modifications.Select(group => new ModificationRequest(group.Avatar, group.Modifications, Match(group.MatchAll))).ToList(),
                    Match = Match(state.MatchAll),
                }, null);

            // 知らない項目の名前は Core も読み飛ばす。API はそれを間違いとして返すので、知っている物だけを書く
            case SearchModuleKind.Unedited:
                return (new ConditionRequest
                {
                    Kind = name,
                    Choice = state.Choice is EditStatusCondition.MissingKey or EditStatusCondition.FilledKey or EditStatusCondition.NeutralKey ? state.Choice : EditStatusCondition.MissingKey,
                    Fields = state.Fields.Where(field => EditFieldsMissing.All.Any(known => EditFieldsMissing.KeyOf(known) == field)).ToList(),
                    Match = Match(state.MatchAll),
                }, null);

            case SearchModuleKind.Updated:
                return (new ConditionRequest
                {
                    Kind = name,
                    Choice = state.Choice is UpdateNoticeCondition.UpdatedKey or UpdateNoticeCondition.OtherKey or UpdateNoticeCondition.NeutralKey ? state.Choice : UpdateNoticeCondition.UpdatedKey,
                    Fields = state.Fields.Where(field => UpdateNoticeCondition.AllKinds.Any(known => UpdateNoticeCondition.KeyOf(known) == field)).ToList(),
                    Match = Match(state.MatchAll),
                }, null);

            case SearchModuleKind.Recent:
                // 「記録の無い商品も表示」は API の recent に無い（記録のある商品だけを日数で絞る）
                if (state.ShowUnspecified)
                {
                    return (null, "recordlessRecent");
                }

                var low = SearchModuleConditions.ParseNumber(state.Min) is >= 0 and var fromDays ? fromDays : 0;
                int? high = SearchModuleConditions.ParseNumber(state.Max) is >= 0 and var toDays ? Math.Max(toDays, low) : null;
                return (new ConditionRequest
                {
                    Kind = name,
                    Recent = state.Choice is "viewed" or "added" ? state.Choice : "used",
                    FromDays = low,
                    ToDays = high,
                }, null);

            // 入れ子は名前のまま渡す（中身を開かない。同じ名前の条件 savedSearch で同じ答え）
            case SearchModuleKind.SavedSearch:
                return (new ConditionRequest { Kind = name, Values = [state.Items[0].Trim()], UseText = state.IgnoreText ? false : null }, null);

            default:
                return (null, "unsupportedKind");
        }
    }

    private static bool? Toggle(SearchModuleState state, string key, bool fallback)
        => state.Toggles is { } toggles && toggles.TryGetValue(key, out var on) ? on : fallback;

    /// <summary>「avatar:ID」「base:名前」（頭に <paramref name="side"/>）を分ける。どちらでもない値があれば書けない</summary>
    private static bool SplitTargets(IReadOnlyList<string> keys, string side, out IReadOnlyList<string>? avatars, out IReadOnlyList<string>? bases)
    {
        var avatarList = new List<string>();
        var baseList = new List<string>();
        foreach (var key in keys)
        {
            var rest = key[side.Length..];
            if (rest.StartsWith(SearchModuleConditions.AvatarKey, StringComparison.Ordinal))
            {
                avatarList.Add(rest[SearchModuleConditions.AvatarKey.Length..]);
            }
            else if (rest.StartsWith(SearchModuleConditions.BaseKey, StringComparison.Ordinal))
            {
                baseList.Add(rest[SearchModuleConditions.BaseKey.Length..]);
            }
            else
            {
                avatars = bases = null;
                return false;
            }
        }

        avatars = avatarList.Count > 0 ? avatarList : null;
        bases = baseList.Count > 0 ? baseList : null;
        return true;
    }
}
