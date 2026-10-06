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
    /// 右の欄の入力とは別の作業なので、入力の項目の後ろに置く。
    /// </summary>
    public static IReadOnlyList<EditField> All { get; } =
        [EditField.UserTags, EditField.Attributes, EditField.Purchases, EditField.AcquiredAt, EditField.Memo, EditField.AvatarConfirmation];

    /// <summary>何も選んでいない状態は作らない（全部切ると何も選ばない条件になり、意味が無い）。既定はユーザータグ（前の「未編集」と同じ意味）。</summary>
    public static IReadOnlyList<EditField> Default { get; } = [EditField.UserTags];

    /// <summary>未入力か。</summary>
    public static bool IsMissing(ItemRecord item, EditField field) => field switch
    {
        EditField.UserTags => item.Local.UserTags.Count == 0,

        // 属性は評価した物だけが記録に入る（未評価は記録に無い）
        EditField.Attributes => item.Local.Attributes.Count == 0,

        // 自分用・貰った・贈ったのどれも記録が無い。バリエーションの無い商品でも「バリエーションを選ばない購入」を記録できるので、記録の有無で見る
        EditField.Purchases => item.Local.Purchases.Count == 0,

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

    /// <summary>状態（<c>searchModules</c> の <c>fields</c>）に書く名前。<c>LocalBlock</c> の欄の名前（camelCase）に揃える。</summary>
    public static string KeyOf(EditField field) => field switch
    {
        EditField.UserTags => "userTags",
        EditField.Attributes => "attributes",
        EditField.Purchases => "purchases",
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
