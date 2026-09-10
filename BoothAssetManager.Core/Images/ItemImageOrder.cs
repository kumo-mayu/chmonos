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
    public static IReadOnlyList<OrderedImage> Arrange(
        string directory,
        IReadOnlyList<BoothImage> images,
        IReadOnlyList<string> onDisk,
        IReadOnlyList<UserImage>? userImages = null)
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

        // ② 自分で足した分。**消えたものより前**に置く（観測 → 入力 → 消えたもの）
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
}
