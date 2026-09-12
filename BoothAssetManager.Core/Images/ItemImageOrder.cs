using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Images;

/// <summary>その画像がどこから来たか。**観測と入力を隠さない。**</summary>
public enum ImageOrigin
{
    /// <summary>BOOTHから取ったもので、今も一覧に載っている。</summary>
    Booth,

    /// <summary>ユーザが自分で足したもの。</summary>
    UserAdded,

    /// <summary>BOOTH側の一覧から消えたもの。手元には残してある。</summary>
    Orphaned,
}

/// <summary>並べ替えた画像1枚。</summary>
public readonly record struct OrderedImage(string Path, ImageOrigin Origin)
{
    /// <summary>BOOTH側から消えた画像か。</summary>
    public bool IsOrphaned => Origin == ImageOrigin.Orphaned;

    /// <summary>ユーザが自分で足した画像か。</summary>
    public bool IsUserAdded => Origin == ImageOrigin.UserAdded;
}

/// <summary>
/// 商品画像を並べる。
///
/// 保存名は元URLのハッシュ8桁なので、フォルダを名前順に読むと並びが乱数になる
/// （実測で13件中9件、BOOTHの1枚目と手元の1枚目が食い違っていた）。
/// 1枚目はサムネイルとして全画面に出るので、必ずBOOTHの並びを正とする。
///
/// **並びは「観測 → 自分で足した分 → 消えたもの」。**
/// 自分で足した画像をBOOTHの一覧と突き合わせると「一覧に無い」ので
/// 消えた扱いになってしまう。**自分で足したのに「削除済」と出るのは誤り**なので、
/// 記録（<see cref="LocalBlock.UserImages"/>）を先に見て取り分ける。
/// </summary>
public static class ItemImageOrder
{
    /// <param name="fileTime">ファイルの時刻を引く。試験で差し替えるためだけにある。</param>
    public static IReadOnlyList<OrderedImage> Arrange(
        string directory,
        IReadOnlyList<BoothImage> images,
        IReadOnlyList<string> onDisk,
        IReadOnlyList<UserImage>? userImages = null,
        Func<string, DateTime>? fileTime = null)
    {
        var result = new List<OrderedImage>(onDisk.Count);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ① BOOTHの並びが正。1枚目がサムネイルになる
        foreach (var image in images)
        {
            var path = System.IO.Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl));

            if (onDisk.Contains(path, StringComparer.OrdinalIgnoreCase) && taken.Add(path))
            {
                result.Add(new OrderedImage(path, ImageOrigin.Booth));
            }
        }

        // ② 自分で足した分。**消えたものより前**に置く（観測 → 入力 → 消えたもの）。
        //
        // 記録を失った自分の画像（名前は user- なのに記録に無い物。記録だけ古い写しで戻した、など）を先に、
        // ファイルの時刻の順で置く。記録にある分はその後に記録の順（足した順・手で並べ替えた順）で続くので、
        // **新しく足した絵は必ず末尾に付く**（ユーザ判断 2026-09-12）。以前は記録に無い分が後ろに回り、
        // 今足した絵が古い絵より前に「先頭・その次・その次」と入って見えた
        var recorded = new HashSet<string>(
            (userImages ?? []).Select(userImage => System.IO.Path.Combine(directory, userImage.FileName)),
            StringComparer.OrdinalIgnoreCase);

        var strays = onDisk
            .Where(path => !taken.Contains(path) && !recorded.Contains(path) && UserImageName.IsUserAdded(path))
            .OrderBy(fileTime ?? WrittenAt)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in strays)
        {
            if (taken.Add(path))
            {
                result.Add(new OrderedImage(path, ImageOrigin.UserAdded));
            }
        }

        foreach (var userImage in userImages ?? [])
        {
            var path = System.IO.Path.Combine(directory, userImage.FileName);

            if (onDisk.Contains(path, StringComparer.OrdinalIgnoreCase) && taken.Add(path))
            {
                result.Add(new OrderedImage(path, ImageOrigin.UserAdded));
            }
        }

        // ③ 残り。BOOTHの一覧にも記録にも無いもの
        foreach (var path in onDisk)
        {
            if (!taken.Add(path))
            {
                continue;
            }

            // 記録が失われても、名前で自分の分と分かる。
            // 「削除済」と誤って出すよりは、名前を信じる方が害が小さい
            result.Add(new OrderedImage(
                path,
                UserImageName.IsUserAdded(path) ? ImageOrigin.UserAdded : ImageOrigin.Orphaned));
        }

        return result;
    }

    /// <summary>ファイルの時刻。読めなければ最小値（先頭に寄る）。</summary>
    private static DateTime WrittenAt(string path)
    {
        try
        {
            return System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>並びだけが要る場合。</summary>
    public static IReadOnlyList<string> Paths(
        string directory,
        IReadOnlyList<BoothImage> images,
        IReadOnlyList<string> onDisk,
        IReadOnlyList<UserImage>? userImages = null)
        => Arrange(directory, images, onDisk, userImages).Select(entry => entry.Path).ToList();

    /// <summary>
    /// サムネイルに使う1枚を選ぶ。
    ///
    /// **指名があればそれ。**無ければ並びの1枚目。
    /// 指名した先が消えていたら黙って1枚目に戻す——
    /// 画像を取り直せば戻ってくるので、指名そのものは消さない。
    /// </summary>
    public static string? Thumbnail(IReadOnlyList<OrderedImage> ordered, string? pinnedFileName)
    {
        if (!string.IsNullOrWhiteSpace(pinnedFileName))
        {
            var pinned = ordered.FirstOrDefault(entry =>
                string.Equals(
                    System.IO.Path.GetFileName(entry.Path),
                    pinnedFileName,
                    StringComparison.OrdinalIgnoreCase));

            if (pinned.Path is not null)
            {
                return pinned.Path;
            }
        }

        return ordered.Count > 0 ? ordered[0].Path : null;
    }

    /// <summary>
    /// その画像の役割。
    ///
    /// **付いていなければ出どころから決める。**BOOTHの画像は「BOOTH」、
    /// 自分で足した画像は「その他」。<see cref="ImageRole.Modified"/> は
    /// 自動では付かない——どれが改変後の姿かは人にしか分からない。
    ///
    /// BOOTHから消えた画像も出どころは BOOTH のままにする。
    /// 消えたことは並びと札で示していて、役割の話ではない。
    /// </summary>
    public static ImageRole RoleOf(
        OrderedImage image,
        IReadOnlyDictionary<string, ImageRole>? roles)
    {
        var fileName = System.IO.Path.GetFileName(image.Path);
        if (roles is not null && TryGet(roles, fileName, out var assigned))
        {
            return assigned;
        }

        return image.Origin == ImageOrigin.UserAdded ? ImageRole.Other : ImageRole.Booth;
    }

    /// <summary>
    /// サムネイルに使う1枚を、役割の指定も見て選ぶ。
    ///
    /// **<see cref="ThumbnailRole.Default"/> のときだけ★の指名が効く**（ユーザ判断）。
    /// 特定の役割を選んでいるときは役割が勝つ——「改変例を出す」と決めたのに、
    /// ★を付けた商品だけ別の絵になるのは筋が通らない。
    ///
    /// **その役割の画像が1枚も無い商品は、普通のサムネイルに戻す。**
    /// カードが空欄になると、絵が無いのか役割が付いていないのか読めない。
    /// </summary>
    public static string? Thumbnail(
        IReadOnlyList<OrderedImage> ordered,
        string? pinnedFileName,
        ThumbnailRole thumbnailRole,
        IReadOnlyDictionary<string, ImageRole>? roles)
    {
        if (ImageRoles.AsImageRole(thumbnailRole) is { } wanted)
        {
            foreach (var image in ordered)
            {
                if (RoleOf(image, roles) == wanted)
                {
                    return image.Path;
                }
            }
        }

        return Thumbnail(ordered, pinnedFileName);
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, ImageRole> roles,
        string fileName,
        out ImageRole role)
    {
        if (roles.TryGetValue(fileName, out role))
        {
            return true;
        }

        // 大文字小文字を無視して引き直す。JSONの辞書は比較の仕方を持たないので、
        // 手で直したファイルから読むと素の比較になる
        foreach (var pair in roles)
        {
            if (string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase))
            {
                role = pair.Value;
                return true;
            }
        }

        return false;
    }
}
