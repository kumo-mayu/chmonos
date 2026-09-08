using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Images;

/// <summary>並べ替えた画像1枚。</summary>
public readonly record struct OrderedImage(string Path, bool IsOrphaned);

/// <summary>
/// 商品画像を並べる。
///
/// 保存名は元URLのハッシュ8桁なので、フォルダを名前順に読むと並びが乱数になる
/// （実測で13件中9件、BOOTHの1枚目と手元の1枚目が食い違っていた）。
/// 1枚目はサムネイルとして全画面に出るので、必ずBOOTHの並びを正とする。
///
/// BOOTH側の一覧から消えた画像は手元に残す方針なので、末尾へ回して印を付ける。
/// </summary>
public static class ItemImageOrder
{
    public static IReadOnlyList<OrderedImage> Arrange(
        string directory,
        IReadOnlyList<BoothImage> images,
        IReadOnlyList<string> onDisk)
    {
        var result = new List<OrderedImage>(onDisk.Count);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            var path = System.IO.Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl));

            if (onDisk.Contains(path, StringComparer.OrdinalIgnoreCase) && taken.Add(path))
            {
                result.Add(new OrderedImage(path, IsOrphaned: false));
            }
        }

        foreach (var path in onDisk)
        {
            if (taken.Add(path))
            {
                result.Add(new OrderedImage(path, IsOrphaned: true));
            }
        }

        return result;
    }

    /// <summary>並びだけが要る場合。</summary>
    public static IReadOnlyList<string> Paths(
        string directory,
        IReadOnlyList<BoothImage> images,
        IReadOnlyList<string> onDisk)
        => Arrange(directory, images, onDisk).Select(entry => entry.Path).ToList();
}
