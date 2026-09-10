namespace BoothAssetManager.Core.Models;

/// <summary>
/// <c>local</c> ブロックの項目。
///
/// 「この保存が責任を持つのはどれか」を名指しするために使う。
/// 名指ししなかった項目は、保存の直前に読み直したものが残る。
/// </summary>
public enum LocalField
{
    DisplayName,
    Shop,
    Category,
    UserImages,
    ThumbnailImage,
    ImageRoles,
    UserTags,
    Attributes,
    Memo,
    Avatars,
    AvatarBases,
    AvatarsDetectedAt,
    Purchases,
    LocalFiles,
    LocalFolders,
    AcquiredAt,
    NotifyOnUpdate,
    IsHidden,
    LastFetchedAt,
    NextFetchDueAt,
    ConsecutiveNotFoundCount,
    IsDelisted,
}

/// <summary>
/// 保存が持つ項目だけを重ねる。
///
/// 画面は開いた時点の <c>local</c> を写しで抱えている。そのまま書き戻すと、
/// 開いている間に他（取り込み・検出・再取得）が書いた項目まで
/// 古い写しで上書きしてしまう。**書いてよいのは自分が決めた項目だけ。**
/// </summary>
public static class LocalFields
{
    /// <summary>
    /// <paramref name="current"/>（読み直した今の姿）に、
    /// <paramref name="incoming"/> のうち <paramref name="owns"/> で名指しした項目だけを重ねる。
    /// </summary>
    public static LocalBlock Merge(
        LocalBlock current,
        LocalBlock incoming,
        IReadOnlyCollection<LocalField> owns)
    {
        var result = current;

        foreach (var field in owns)
        {
            result = field switch
            {
                LocalField.DisplayName => result with { DisplayName = incoming.DisplayName },
                LocalField.Shop => result with { Shop = incoming.Shop },
                LocalField.Category => result with { Category = incoming.Category },
                LocalField.UserImages => result with { UserImages = incoming.UserImages },
                LocalField.ThumbnailImage => result with { ThumbnailImage = incoming.ThumbnailImage },
                LocalField.ImageRoles => result with { ImageRoles = incoming.ImageRoles },
                LocalField.UserTags => result with { UserTags = incoming.UserTags },
                LocalField.Attributes => result with { Attributes = incoming.Attributes },
                LocalField.Memo => result with { Memo = incoming.Memo },
                LocalField.Avatars => result with { Avatars = incoming.Avatars },
                LocalField.AvatarBases => result with { AvatarBases = incoming.AvatarBases },
                LocalField.AvatarsDetectedAt => result with { AvatarsDetectedAt = incoming.AvatarsDetectedAt },
                LocalField.Purchases => result with { Purchases = incoming.Purchases },
                LocalField.LocalFiles => result with { LocalFiles = incoming.LocalFiles },
                LocalField.LocalFolders => result with { LocalFolders = incoming.LocalFolders },
                LocalField.AcquiredAt => result with { AcquiredAt = incoming.AcquiredAt },
                LocalField.NotifyOnUpdate => result with { NotifyOnUpdate = incoming.NotifyOnUpdate },
                LocalField.IsHidden => result with { IsHidden = incoming.IsHidden },
                LocalField.LastFetchedAt => result with { LastFetchedAt = incoming.LastFetchedAt },
                LocalField.NextFetchDueAt => result with { NextFetchDueAt = incoming.NextFetchDueAt },
                LocalField.ConsecutiveNotFoundCount =>
                    result with { ConsecutiveNotFoundCount = incoming.ConsecutiveNotFoundCount },
                LocalField.IsDelisted => result with { IsDelisted = incoming.IsDelisted },
                _ => result,
            };
        }

        return result;
    }
}

/// <summary>
/// 誰が何を持つか。
///
/// 一覧にしてここへ集めているのは、**持ち主が2人いる項目を目で見つけられるようにする**ため。
/// 実際 <see cref="LocalField.Avatars"/> は検出と商品ページの両方が持っている
/// （検出側にマージの規則があるので壊れない）。
/// </summary>
public static class LocalOwners
{
    /// <summary>編集画面。人が決めるものだけ。</summary>
    public static readonly IReadOnlyCollection<LocalField> EditScreen =
    [
        LocalField.DisplayName,
        LocalField.Shop,
        LocalField.Category,
        LocalField.UserTags,
        LocalField.Attributes,
        LocalField.Memo,
        LocalField.Purchases,
        LocalField.AcquiredAt,
        LocalField.NotifyOnUpdate,
        LocalField.IsHidden,
    ];

    /// <summary>商品ページの対応アバター操作（手で足す・違うと言う）。</summary>
    public static readonly IReadOnlyCollection<LocalField> SupportedAvatars = [LocalField.Avatars];

    /// <summary>対応アバターの検出。</summary>
    public static readonly IReadOnlyCollection<LocalField> Detection =
    [
        LocalField.Avatars,
        LocalField.AvatarBases,
        LocalField.AvatarsDetectedAt,
    ];

    /// <summary>素体の対応宣言だけを直す操作。</summary>
    public static readonly IReadOnlyCollection<LocalField> AvatarBases = [LocalField.AvatarBases];

    /// <summary>非表示の切り替え。検索の右クリックと設定画面の両方から。</summary>
    public static readonly IReadOnlyCollection<LocalField> Visibility = [LocalField.IsHidden];

    /// <summary>userTagの一括書き換え（名前を変えた・消したとき）。</summary>
    public static readonly IReadOnlyCollection<LocalField> UserTags = [LocalField.UserTags];

    /// <summary>属性の一括書き換え。</summary>
    public static readonly IReadOnlyCollection<LocalField> Attributes = [LocalField.Attributes];

    /// <summary>自分で足した画像の操作（足す・消す・並べ替え・サムネイルの指名・役割）。</summary>
    public static readonly IReadOnlyCollection<LocalField> UserImages =
    [
        LocalField.UserImages,
        LocalField.ThumbnailImage,
        LocalField.ImageRoles,
    ];

    /// <summary>取り込み。手元のファイルとフォルダだけ。</summary>
    public static readonly IReadOnlyCollection<LocalField> Import =
    [
        LocalField.LocalFiles,
        LocalField.LocalFolders,
    ];

    /// <summary>
    /// BOOTHからの再取得。取得の記録だけを持つ。
    /// <see cref="LocalField.Purchases"/> は持たない
    /// （<c>ExistsOnBooth</c> は保存のたびに計算し直されるため）。
    /// </summary>
    public static readonly IReadOnlyCollection<LocalField> Fetch =
    [
        LocalField.LastFetchedAt,
        LocalField.NextFetchDueAt,
        LocalField.ConsecutiveNotFoundCount,
        LocalField.IsDelisted,
    ];
}
