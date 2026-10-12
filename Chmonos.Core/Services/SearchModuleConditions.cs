using System.Globalization;
using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 絞り込みの条件の種類ごとの照らし方と、保存した条件（<see cref="SearchModuleState"/>）から判定を作る入口
/// （2026-10-12・外から使う API のために画面から分けた。`docs/spec/local-api.md`）。
///
/// 画面の条件（App の <c>SearchModule</c>）も、ここの照らし方を受け取って作る。画面の条件の値は状態に書いた物と同じなので、
/// 状態からここで作った判定と、画面の判定は同じ商品に当たる（`SearchModuleConditionTests` で確かめる）。
/// </summary>
public static class SearchModuleConditions
{
    /// <summary>何も絞らない選択肢の鍵（「両方」「すべて」）。</summary>
    public const string BothKey = "both";

    /// <summary>条件「壊れたzip」の「ある」の鍵。取り込みの結果から入る口と同じ物を指す。</summary>
    public const string BrokenZipKey = "broken";

    /// <summary>条件「見つからないファイル」の「ある」の鍵。</summary>
    public const string MissingFileKey = "missing";

    /// <summary>条件「BOOTHに無い商品」の「BOOTHに無い商品だけ」の鍵。</summary>
    public const string NotOnBoothKey = "local";

    /// <summary>価格の元：自分が払った額（既定）。</summary>
    public const string PaidSource = "paid";

    /// <summary>価格の元：BOOTH の価格。</summary>
    public const string BoothSource = "booth";

    public const string AllFreeKey = "free";
    public const string FreeAndPaidKey = "mixed";
    public const string PaidOnlyKey = "paid";

    /// <summary>対応アバターの積んだ値の頭（アバターの商品ID）。</summary>
    public const string AvatarKey = "avatar:";

    /// <summary>対応アバターの積んだ値の頭（共通素体の名前）。</summary>
    public const string BaseKey = "base:";

    /// <summary>入手日を最初の購入だけで見る（状態の <c>choice</c> の値。既定）。</summary>
    public const string FirstPurchase = "first";

    /// <summary>入手日を購入ごとの日付の全部で見る（どれか1つが範囲に入れば当たる）。</summary>
    public const string AllPurchases = "all";

    /// <summary>プルダウンで選ぶ条件の照らし方。</summary>
    /// <param name="Keys">選択肢の鍵（先頭が足したときの既定）。</param>
    /// <param name="NeutralKey">何も絞らない選択肢。持たない条件は null。</param>
    public sealed record ChoiceSpec(
        IReadOnlyList<string> Keys,
        string? NeutralKey,
        Func<ItemRecord, SearchModuleContext, string, bool, bool> Matches,
        ChoiceFlagSpec? Flag = null);

    /// <param name="StateKey">状態に書く名前（<see cref="SearchModuleState.Toggles"/>）。</param>
    public sealed record ChoiceFlagSpec(string StateKey, bool Default);

    /// <summary>候補から積む条件の照らし方。</summary>
    /// <param name="AllowsAnd">AND を選べるか（1商品に1つの物は OR だけ）。</param>
    /// <param name="FlagDefault">補助の切り替えの既定（持たない種類は false のまま）。</param>
    public sealed record ListSpec(
        bool AllowsAnd,
        Func<ItemRecord, SearchModuleContext, string, bool, bool> Matches,
        bool HasFlag = false,
        bool FlagDefault = false,
        Func<ItemRecord, SearchModuleContext, bool>? IsUnspecified = null,
        Func<ItemRecord, SearchModuleContext, bool>? IncludeMatches = null,
        Func<string, string>? MatchKey = null);

    /// <summary>数の範囲の照らし方。</summary>
    /// <param name="Sources">数の元の鍵（価格の「払った額」「BOOTH価格」。先頭が既定）。持たない種類は空。</param>
    /// <param name="NoOutlierSource">外れ値を外さない元（払った額は自分で入れた数なので外れ値は無い）。</param>
    public sealed record RangeSpec(
        Func<ItemRecord, string?, IReadOnlyList<long>> Values,
        IReadOnlyList<string> Sources,
        bool SupportsOutliers = false,
        string? NoOutlierSource = null,
        bool SupportsMatchAll = false,
        bool SupportsUnpriced = false);

    /// <summary>日付の照らし方。</summary>
    /// <param name="Any">商品の日付のどれかが条件に当たるか。</param>
    /// <param name="First">最初の1つだけで見るときの日付（入手日だけ）。</param>
    /// <param name="AllDates">空欄の境が指す手元の端を求める日付（引数は最初の1つだけで見るか）。</param>
    public sealed record DateSpec(
        Func<ItemRecord, Func<DateOnly, bool>, bool> Any,
        Func<ItemRecord, DateOnly?>? First,
        Func<ItemRecord, bool, IEnumerable<DateOnly>> AllDates);

    public static ChoiceSpec? ChoiceOf(SearchModuleKind kind) => kind switch
    {
        // ユーザ判断 2026-10-06（メモ84・案1）：使う人には非公開も削除も同じなので、販売終了と1つにまとめた。仮IDの商品は「両方」のときだけ出す
        SearchModuleKind.EndOfSale => new(["ended", "selling", BothKey], BothKey, (item, _, key, _) => EndOfSaleMatches(item, key)),

        SearchModuleKind.Adult => new(["adult", "general", BothKey], BothKey, (item, _, key, _) => key switch
        {
            "adult" => item.Booth.IsAdult,
            "general" => !item.Booth.IsAdult,
            _ => true,
        }),

        // 「すべて見つからない商品は未所持とする」（ユーザ判断 2026-10-06・案A。既定は切＝所持の定義のまま）：
        // 記録のファイル・フォルダが全部見つからない商品は手元に何も無いので、使う人には未所持と同じ
        SearchModuleKind.Owned => new(["owned", "unowned", BothKey], BothKey, (item, _, key, allMissingIsUnowned) => key switch
        {
            "owned" => item.IsOwned && !(allMissingIsUnowned && item.HasAllFilesMissing),
            "unowned" => !item.IsOwned || (allMissingIsUnowned && item.HasAllFilesMissing),
            _ => true,
        }, new ChoiceFlagSpec("allMissingIsUnowned", Default: false)),

        // 照合は記録だけ（ItemRecord.HasBrokenArchive。ユーザ判断 2026-09-30）
        SearchModuleKind.BrokenZip => new([BrokenZipKey, "none", BothKey], BothKey, (item, _, key, _) => key switch
        {
            BrokenZipKey => item.HasBrokenArchive,
            "none" => !item.HasBrokenArchive,
            _ => true,
        }),

        // カードの印と同じ式（ItemRecord.HasMissingFile）。「未所持も含める」（ユーザ判断 2026-10-06・メモ83・判断7。既定は含める）：
        // 切ると、一部だけ見つからない商品に絞れる（カードの札「一部見つからない」と同じ分け方）
        SearchModuleKind.MissingFile => new([MissingFileKey, "none", BothKey], BothKey, (item, _, key, includeAllMissing) => key switch
        {
            MissingFileKey => item.HasMissingFile && (includeAllMissing || !item.HasAllFilesMissing),
            "none" => !item.HasMissingFile,
            _ => true,
        }, new ChoiceFlagSpec("includeUnowned", Default: true)),

        // 仮のIDで登録した商品（ユーザ指示 2026-10-04 メモ31）。カードの「BOOTHで開く」を出さない判定と同じ式
        SearchModuleKind.NotOnBooth => new([NotOnBoothKey, "booth", BothKey], BothKey, (item, _, key, _) => key switch
        {
            NotOnBoothKey => item.IsLocalOnly,
            "booth" => !item.IsLocalOnly,
            _ => true,
        }),

        // 純三項：3つで全部の購入記録を覆うので「両方」は持たない（メモ83）。貰って自分でも買った物は両方に出る。
        // 「購入記録の無い商品も含める」（ユーザ判断 2026-10-06・判断1：既定は切・どの項目でも出す）
        SearchModuleKind.Gift => new(["given", "received", "bought"], null, (item, _, key, includeUnrecorded) =>
            (includeUnrecorded && item.Local.Purchases.Count == 0) || key switch
            {
                "given" => Purchases.WasGiven(item),
                "received" => Purchases.WasReceived(item),
                _ => item.Local.Purchases.Any(purchase => purchase.Kind == PurchaseKind.ForSelf),
            }, new ChoiceFlagSpec("includeUnrecorded", Default: false)),

        // BOOTH のバリエーションの価格だけで分ける（ユーザ判断 2026-10-06・判断2）。
        // 価格の無い商品は既定でどれにも入らず、「非公開商品も含む」で足せる（既定は切）
        SearchModuleKind.FreePaid => new([AllFreeKey, FreeAndPaidKey, PaidOnlyKey, BothKey], BothKey,
            (item, _, key, includePrivate) => (includePrivate && IsPrivateUnpriced(item)) || FreePaidMatches(item, key),
            new ChoiceFlagSpec("includePrivate", Default: false)),

        SearchModuleKind.Favorite => new(["favorite", "other", BothKey], BothKey, (item, _, key, _) => key switch
        {
            "favorite" => item.Local.IsFavorite,
            "other" => !item.Local.IsFavorite,
            _ => true,
        }),

        SearchModuleKind.Hidden => new(["hidden", "visible", BothKey], BothKey, (item, _, key, _) => key switch
        {
            "hidden" => item.Local.IsHidden,
            "visible" => !item.Local.IsHidden,
            _ => true,
        }),

        _ => null,
    };

    public static ListSpec? ListOf(SearchModuleKind kind) => kind switch
    {
        // カテゴリと BOOTHタグは OR だけ（ユーザ判断 2026-10-06・メモ82・メモ84「AND できる必要が無い」）。
        // 「A と B の両方」が要るときは、同じ種類の条件をもう1つ置けば AND になる（条件どうしは AND）
        SearchModuleKind.Category => new(false, (item, _, key, _) => string.Equals(item.CategoryName, key, StringComparison.CurrentCulture)
            || string.Equals(item.Booth.Category?.ParentName, key, StringComparison.CurrentCulture)),

        SearchModuleKind.BoothTag => new(false, (item, _, key, _) =>
            item.Booth.Tags.Any(tag => string.Equals(tag, key, StringComparison.CurrentCultureIgnoreCase))),

        // 1商品に1つなので AND は意味が無い。お気に入りのショップ（ショップ画面の星）は、別の条件にせずここに持つ（ユーザ指示 2026-09-16）
        SearchModuleKind.Shop => new(false, (item, _, key, _) => string.Equals(item.ShopSubdomain, key, StringComparison.OrdinalIgnoreCase),
            IncludeMatches: (item, context) => item.ShopSubdomain is { } key && context.FavoriteShops.Contains(key)),

        // 素体経由は推定なので含めるかを選べるようにする。既定で含めるのは「対応が確認できていないものを既定で隠さない」方針（メモ82）
        SearchModuleKind.Avatar => new(true, AvatarMatches, HasFlag: true, FlagDefault: true, IsUnspecified: IsUnspecifiedAvatar),

        // 改変を通してそのプロジェクトに紐付いた商品（ユーザ判断 Q7）。プロジェクトの中身は見ない（開くたびに照らすと重い）
        SearchModuleKind.UnityProject => new(true, (item, context, key, _) => context.Modifications.InProject(key, item.Id)),

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる。フォルダは比べる形に1回だけ畳む
        SearchModuleKind.Path => new(true, (item, context, prefix, _) => FolderTree.IsUnderPrefix(item, prefix, context.PathMap),
            MatchKey: FolderTree.UnderPrefix),

        _ => null,
    };

    public static RangeSpec? RangeOf(SearchModuleKind kind) => kind switch
    {
        SearchModuleKind.WishList => new((item, _) => [item.Booth.WishListsCount], []),

        // 支援用のバリエーションや、販売を止めるためのあり得ない高値を外せるようにする（ユーザ判断 2026-09-16）。
        // 払った額は自分で入れた数なので外れ値は無い（ユーザ判断 2026-10-03・メモ16-③）。
        // 種類ごとの価格が全部範囲に入る商品だけを探せる（メモ82-6）。価格の分からない商品も足せる（ユーザ判断 2026-10-06）
        SearchModuleKind.Price => new(PriceValues, [PaidSource, BoothSource],
            SupportsOutliers: true, NoOutlierSource: PaidSource, SupportsMatchAll: true, SupportsUnpriced: true),

        _ => null,
    };

    public static DateSpec? DateOf(SearchModuleKind kind) => kind switch
    {
        SearchModuleKind.PublishedAt => new(
            (item, predicate) => PublishedDate(item) is { } date && predicate(date),
            null,
            (item, _) => PublishedDate(item) is { } date ? [date] : []),

        // 既定は最初の購入（並べ替え・区切りの札と同じ代表の日付）だけで見る（メモ45・2-A。ユーザ判断 2026-10-06・判断8）。
        // ファイルの日付では代えない（並べ替えと同じく手で入れた値だけ）
        SearchModuleKind.AcquiredAt => new(
            PurchaseDates.AnyEntered,
            PurchaseDates.EnteredEarliest,
            (item, firstOnly) => firstOnly
                ? PurchaseDates.EnteredEarliest(item) is { } first ? [first] : []
                : PurchaseDates.AllEntered(item)),

        _ => null,
    };

    /// <summary>
    /// 保存した条件から判定を作る。知らない種類は null。
    /// 「効かせているか」（<see cref="SearchModuleState.Enabled"/>）と「除く」（<see cref="SearchModuleState.Exclude"/>）は見ない（使う側が見る）。
    /// </summary>
    public static ItemCondition? From(SearchModuleState state)
    {
        if (!Enum.TryParse<SearchModuleKind>(state.Kind, out var kind) || !Enum.IsDefined(kind))
        {
            return null;
        }

        if (ChoiceOf(kind) is { } choice)
        {
            var key = choice.Keys.Contains(state.Choice ?? string.Empty) ? state.Choice! : choice.Keys[0];

            // 名前で持つ切り替えだけを読む。欠けていれば既定（前の版の使い回しの Flag は読まない）
            var flag = choice.Flag is { } spec
                && (state.Toggles is { } toggles && toggles.TryGetValue(spec.StateKey, out var on) ? on : spec.Default);
            return new ChoiceCondition(key, flag, choice.NeutralKey, choice.Matches);
        }

        if (ListOf(kind) is { } list)
        {
            // 両方切った状態は作らない（手で書き換えた状態でも、対応している商品を出す側に戻す）
            var groups = list.IsUnspecified is not null;
            var showUnspecified = groups && state.ShowUnspecified;
            var showMatched = !groups || state.ShowMatched || !showUnspecified;
            return new ListCondition(
                state.Items,
                list.AllowsAnd && state.MatchAll,
                list.HasFlag ? state.Flag : list.FlagDefault,
                list.Matches,
                list.IsUnspecified,
                showMatched,
                showUnspecified,
                list.IncludeMatches,
                list.IncludeMatches is not null && state.IncludeFavorites,
                list.MatchKey);
        }

        if (RangeOf(kind) is { } range)
        {
            var source = range.Sources.Contains(state.Choice ?? string.Empty) ? state.Choice : range.Sources.FirstOrDefault();
            var outliers = range.SupportsOutliers && state.IgnoreOutliers && source != range.NoOutlierSource;
            return new RangeCondition(
                item => range.Values(item, source),
                state.MinEnabled || state.MaxEnabled,
                state.MinEnabled ? ParseAmount(state.Min) : null,
                state.MaxEnabled ? ParseAmount(state.Max) : null,
                range.SupportsMatchAll && state.MatchAll,
                range.SupportsUnpriced && state.Flag,
                outliers ? context => Outliers.UpperFence(context.Items.SelectMany(item => range.Values(item, source))) : null);
        }

        if (DateOf(kind) is { } date)
        {
            var firstOnly = date.First is not null && state.Choice != AllPurchases;
            return new DateCondition(date.Any, firstOnly ? date.First : null, context =>
            {
                // 空欄・読めない境は手元の端（画面の欄の空欄と同じ）。端は効かせた側があるときだけ求める
                var today = DateOnly.FromDateTime(context.Now.LocalDateTime);
                var since = state.MinEnabled ? DateText.Parse(state.Min ?? string.Empty, isEnd: false, today) : null;
                var till = state.MaxEnabled ? DateText.Parse(state.Max ?? string.Empty, isEnd: true, today) : null;
                if ((state.MinEnabled && since is null) || (state.MaxEnabled && till is null))
                {
                    var dates = context.Items.SelectMany(item => date.AllDates(item, firstOnly)).ToList();
                    if (dates.Count > 0)
                    {
                        since ??= state.MinEnabled ? dates.Min() : null;
                        till ??= state.MaxEnabled ? dates.Max() : null;
                    }
                }

                return (since, till);
            });
        }

        return kind switch
        {
            SearchModuleKind.UserTag => new UserTagsCondition(state.UserTags, state.MatchAll),
            SearchModuleKind.Attribute => new AttributeCondition(state.Ranges, state.MatchAll || state.Ranges.Count == 0),
            SearchModuleKind.Modification => new ModificationsCondition(state.Modifications, state.MatchAll),
            SearchModuleKind.Unedited => new EditStatusCondition(
                state.Choice is EditStatusCondition.MissingKey or EditStatusCondition.FilledKey or EditStatusCondition.NeutralKey
                    ? state.Choice
                    : EditStatusCondition.MissingKey,
                EditFieldsMissing.Parse(state.Fields),
                state.MatchAll,
                (item, context) => context.IsAwaitingDetection(item.Id)),
            SearchModuleKind.Updated => new UpdateNoticeCondition(
                state.Choice is UpdateNoticeCondition.UpdatedKey or UpdateNoticeCondition.OtherKey or UpdateNoticeCondition.NeutralKey
                    ? state.Choice
                    : UpdateNoticeCondition.UpdatedKey,
                UpdateNoticeCondition.Parse(state.Fields),
                state.MatchAll,
                (itemId, context) => context.UnreadUpdateKinds(itemId)),
            SearchModuleKind.Recent => RecentFrom(state),
            _ => null,
        };
    }

    private static RecentCondition RecentFrom(SearchModuleState state)
    {
        var kind = state.Choice switch
        {
            "viewed" => RecentKind.Viewed,
            "added" => RecentKind.Added,
            _ => RecentKind.Used,
        };

        var low = ParseNumber(state.Min) is >= 0 and var from ? from : 0;
        int? high = ParseNumber(state.Max) is >= 0 and var to ? Math.Max(to, low) : null;
        return new RecentCondition(kind, low, high);
    }

    /// <summary>全角の数字・カンマ・円記号が混ざっていても数として読む。読めなければ null。</summary>
    public static int? ParseNumber(string? text)
        => Fold(text) is { } folded && int.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// <see cref="ParseNumber"/> の 64bit 版。数の範囲（払った額の合計など）が使う。
    /// 合計は 32bit を超え得る（外部の点検 2026-10-06）ので、欄に打てる数も同じ幅にそろえる
    /// </summary>
    public static long? ParseAmount(string? text)
        => Fold(text) is { } folded && long.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? Fold(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : text.Normalize(NormalizationForm.FormKC).Replace(",", string.Empty).Replace("¥", string.Empty).Trim();

    private static DateOnly? PublishedDate(ItemRecord item)
        => item.Booth.PublishedAt is { } at ? DateOnly.FromDateTime(at.LocalDateTime) : null;

    /// <summary>
    /// 価格の条件で照らす数。既定は自分が払った額（ユーザ判断 Q2）。BOOTH の価格は種類ごとにあり、どれか1つでも範囲に入れば当たり。
    /// 払った額を入れていない商品は「0円」ではなく「分からない」ので、範囲のどこにも入らない。
    /// </summary>
    public static IReadOnlyList<long> PriceValues(ItemRecord item, string? source)
    {
        if (source == BoothSource)
        {
            return item.Booth.Variations.Select(variation => (long)variation.Price).ToList();
        }

        // 「払った額」の並べ替えと同じ数え方（Purchases.SelfPaidOrNull）
        return Purchases.SelfPaidOrNull(item) is { } paid ? [paid] : [];
    }

    /// <summary>BOOTH の価格が無い本物のIDの商品（IDのまま登録した非公開の商品）。仮IDの商品は有料・無料の外なので入れない。</summary>
    public static bool IsPrivateUnpriced(ItemRecord item) => !item.IsLocalOnly && item.Booth.Variations.Count == 0;

    /// <summary>
    /// 有料・無料（ユーザ判断 2026-10-06・判断2）。BOOTH のバリエーションの価格だけで、すべて0円・0円と有料の両方・すべて有料に分ける。
    /// 払った額は見ない（支援版を買っても、無料版があるかは変わらない）。バリエーションの無い商品（BOOTHに無い商品・取れていない商品）はどれにも入らない。
    /// </summary>
    public static bool FreePaidMatches(ItemRecord item, string key)
    {
        // 「両方」は絞らない（価格の分からない商品も含めて全部。件数もここから数える）
        if (key == BothKey)
        {
            return true;
        }

        var variations = item.Booth.Variations;
        if (variations.Count == 0)
        {
            return false;
        }

        var hasFree = variations.Any(variation => variation.Price == 0);
        var hasPaid = variations.Any(variation => variation.Price > 0);
        return key switch
        {
            AllFreeKey => hasFree && !hasPaid,
            FreeAndPaidKey => hasFree && hasPaid,
            PaidOnlyKey => hasPaid && !hasFree,
            _ => false,
        };
    }

    /// <summary>
    /// 販売終了（ユーザ判断 2026-10-06・メモ84・案1）。**本物のIDの商品だけに効かせる**：
    /// 「販売終了・非公開」＝ページはあるが販売終了・一度取れた後に見つからないのが続いた（非公開と確定した）・IDのまま登録した物。
    /// 「公開中」＝それ以外。見つからない回数が非公開と確定する回数に届く前（確かめ中）は、まだ印が付いていないので公開中（判断4）。
    /// 仮IDの商品は BOOTH の状態を持たないので、「両方」のときだけ出す。売り切れは含めない（在庫の話で、販売が終わったわけではない）。
    /// </summary>
    public static bool EndOfSaleMatches(ItemRecord item, string key)
    {
        if (key == BothKey)
        {
            return true;
        }

        if (item.IsLocalOnly)
        {
            return false;
        }

        var ended = SearchConditions.IsDelisted(item);
        return key == "ended" ? ended : !ended;
    }

    /// <summary>
    /// 対応アバターの**指定が無い**商品か（どのアバターにも使える扱いにするかたまり・ユーザ判断 2026-09-16）。
    ///
    /// 出品者の宣言（消していない分）と共通素体の宣言が両方とも無いこと。1つでも書いてあるものは書いてある内容どおりに照らす。
    /// 要確認（説明文のリンク）は宣言に数えていない（絞り込みでも数えない）ので、それだけの商品は「指定が無い」側に入る。
    /// </summary>
    public static bool IsUnspecifiedAvatar(ItemRecord item, SearchModuleContext context)
        => context.Compatibility.Resolve(item.Local).Count == 0
            && item.Local.AvatarBases.All(link => link.Rejected);

    public static bool AvatarMatches(ItemRecord item, SearchModuleContext context, string key, bool viaBase)
    {
        var resolved = context.Compatibility.Resolve(item.Local);

        if (key.StartsWith(AvatarKey, StringComparison.Ordinal))
        {
            return resolved.TryGetValue(key[AvatarKey.Length..], out var match)
                && (match == AvatarMatch.Direct || (viaBase && match == AvatarMatch.ViaBase));
        }

        if (!key.StartsWith(BaseKey, StringComparison.Ordinal))
        {
            return false;
        }

        // 共通素体：素体への対応を宣言している商品。素体経由を含めるなら、素体に属するアバターに対応している商品も
        var baseName = key[BaseKey.Length..];
        if (item.Local.AvatarBases.Any(link => !link.Rejected
            && string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)))
        {
            return true;
        }

        if (!viaBase)
        {
            return false;
        }

        var members = context.Compatibility.MembersOf(baseName);
        return members.Count > 0 && members.Any(resolved.ContainsKey);
    }
}
