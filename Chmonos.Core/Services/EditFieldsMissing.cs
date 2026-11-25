using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 編集画面の項目のうち、検索の「編集状況」で見る物（ユーザ判断 2026-10-01・`docs/research/search-modules-2026-10-01.md` §6）。
///
/// 並べるのは、記録だけで「入力したか」が決まり、入力すると消える物だけ。払った額（空欄も 0円で記録する）・購入の種類（既定が自分用）・
/// バリエーション分け（「指定しない」を選んでも記録の上では選んでいない物と同じ）・商品名など（BOOTH から取れる商品では常に空）は並べない。
/// </summary>
public enum EditField
{
    UserTags,
    Attributes,
    Purchases,

    /// <summary>
    /// 買ったバリエーションの「対応」（どのアバター向けか。ユーザ判断 2026-11-25「増やしましょう」）。
    /// 未入力＝編集画面で「未選択」「未確認」の印が付く行がある（買った行で、確認済みの向き先が無い）
    /// </summary>
    VariationTargets,
    AcquiredAt,
    Memo,

    /// <summary>
    /// 対応アバターの確認（ユーザ判断 2026-10-06。前は別の条件「対応アバターの確認」で、確認待ちあり／なし／両方を選んだ）。
    /// 未入力＝確認待ちあり、入力済み＝確認待ちなし。人が商品ページで確かめると消える物なので、入力と同じ向きに数える
    /// </summary>
    AvatarConfirmation,
}

/// <summary>編集画面の項目が未入力か（検索の「編集状況」・試験あり）。</summary>
public static class EditFieldsMissing
{
    /// <summary>
    /// 画面に並べる順（編集画面の右の欄の上からの順）。対応アバターの確認は最後：編集画面では左の欄（商品ページと同じ部品）にあり、
    /// 右の欄の入力とは別の作業なので、入力の項目の後ろに置く。バリエーションの対応は購入したバリエーションの行の中の欄なので、そのすぐ後。
    /// </summary>
    public static IReadOnlyList<EditField> All { get; } =
        [EditField.UserTags, EditField.Attributes, EditField.Purchases, EditField.VariationTargets, EditField.AcquiredAt, EditField.Memo, EditField.AvatarConfirmation];

    /// <summary>何も選んでいない状態は作らない（全部切ると何も選ばない条件になり、意味が無い）。既定はユーザータグ（前の「未編集」と同じ意味）。</summary>
    public static IReadOnlyList<EditField> Default { get; } = [EditField.UserTags];

    /// <summary>未入力か。</summary>
    public static bool IsMissing(ItemRecord item, EditField field) => field switch
    {
        EditField.UserTags => item.Local.UserTags.Count == 0,

        // 値のある属性だけを見る。選択の帯で足した「値はまだ」だけの商品も未入力に数える（値を付けに編集で探せるように。2026-11-20）
        EditField.Attributes => item.Local.Attributes.Count == 0,

        // 自分用・貰った・贈ったのどれも記録が無い。バリエーションの無い商品でも「バリエーションを選ばない購入」を記録できるので、記録の有無で見る
        EditField.Purchases => item.Local.Purchases.Count == 0,
        EditField.VariationTargets => HasUndecidedVariationTargets(item),

        // 手で入れていない。画面はファイルの日付を出しているが、それは入力ではない
        EditField.AcquiredAt => item.Local.AcquiredAt is null,
        EditField.Memo => string.IsNullOrWhiteSpace(item.Local.Memo),
        EditField.AvatarConfirmation => HasUnconfirmedAvatars(item),
        _ => false,
    };

    /// <summary>
    /// まだ確かめていない対応アバターがあるか。説明文から読み取っただけの対応アバター（`H2Link`）は対応と数えず、
    /// 商品ページで人が確かめるまで `Confirmed` が立たない。消した物（`Rejected`）は数えない（商品ページの「確認待ち n」と同じ）。
    /// </summary>
    public static bool HasUnconfirmedAvatars(ItemRecord item) => item.Local.Avatars.Any(link => !link.Rejected && !link.Confirmed);

    /// <summary>
    /// 買ったバリエーションに、どのアバター向けかを決めていない行があるか（編集画面の「対応」の欄の「未選択」「未確認」の印と同じ向き）。
    ///
    /// 欄が出るのは、バリエーションが2つ以上あり対応アバターがある商品だけ（1つなら全部の対応がその1つの物で、対応が無ければ選ぶ物が無い）。
    /// 買った行は購入の記録のバリエーションごとで、「バリエーションを選ばない購入」（null）とBOOTHから消えた行も含む。
    /// 人が決めた向き先（全部・素体・アバター）が1つも無い行は、名前から見つけた候補だけ（未確認）でも、外した候補だけでも未入力。
    ///
    /// 対応アバターがあるかは、記録の宣言で見る：消した物と説明文から読み取っただけの物（H2Link）を除いたアバター・共通素体が1つでもあるか。
    /// 編集画面は登録簿で素体の仲間まで広げて見るが、ここは登録簿を持たない（項目の判定は商品の記録だけで決め、外から使う API とも同じ式にする）。
    /// そのため、仲間のアバターが登録簿に1体も無い共通素体だけを宣言した商品では、画面に欄が無いのに未入力と数える
    /// </summary>
    public static bool HasUndecidedVariationTargets(ItemRecord item)
    {
        var local = item.Local;
        if (item.Booth.Variations.Count < 2 || local.Purchases.Count == 0)
        {
            return false;
        }

        var hasAvatars = local.Avatars.Any(link => !link.Rejected && link.Source != AvatarLinkSource.H2Link)
            || local.AvatarBases.Any(link => !link.Rejected && !string.IsNullOrWhiteSpace(link.BaseName));
        if (!hasAvatars)
        {
            return false;
        }

        return local.Purchases
            .Select(purchase => purchase.VariationId)
            .Distinct()
            .Any(id => !local.VariationTargets.Any(target => target.VariationId == id && !target.IsEmpty));
    }

    /// <summary>状態（<c>searchModules</c> の <c>fields</c>）に書く名前。<c>LocalBlock</c> の欄の名前（camelCase）に揃える。</summary>
    public static string KeyOf(EditField field) => field switch
    {
        EditField.UserTags => "userTags",
        EditField.Attributes => "attributes",
        EditField.Purchases => "purchases",
        EditField.VariationTargets => "variationTargets",
        EditField.AcquiredAt => "acquiredAt",

        // 欄の名前（avatars）にすると「対応アバターが未入力」と読めるので、確かめたかを名前に入れる
        EditField.AvatarConfirmation => "avatarConfirmation",
        _ => "memo",
    };

    /// <summary>
    /// 状態の名前を読む。知らない名前は黙って飛ばし（種類の名前と同じ扱い）、残りが無ければ既定に戻す
    /// （手で直した JSON で「何も選ばない条件」を作らない）。並びは画面の順に揃える。
    /// </summary>
    public static IReadOnlyList<EditField> Parse(IEnumerable<string> keys)
    {
        var known = keys.Select(key => All.Cast<EditField?>().FirstOrDefault(field => KeyOf(field!.Value) == key))
            .OfType<EditField>()
            .ToHashSet();
        return known.Count == 0 ? Default : All.Where(known.Contains).ToList();
    }
}
