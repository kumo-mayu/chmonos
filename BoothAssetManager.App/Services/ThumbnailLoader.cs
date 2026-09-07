using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 保存済みのWebPサムネイルを読み込む。
///
/// WPF標準の <see cref="BitmapImage"/> はWebPを解釈できるとは限らない
/// （OSに追加のコーデックが入っているかどうかに依存する）ため、
/// 取り込み時と同じ ImageSharp で復号してから <see cref="BitmapSource"/> へ変換する。
/// 環境によって画像が出たり出なかったりする状態を避けるための判断。
///
/// 復号結果はパス単位でキャッシュする。カードにマウスを乗せたときに
/// 同じ画像を何度も復号し直さないようにするため。
/// </summary>
public sealed class ThumbnailLoader
{
    private readonly Dictionary<string, BitmapSource?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _filesByDirectory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>そのitemが持つ画像ファイルのパス一覧（表示順）。</summary>
    public IReadOnlyList<string> ListFiles(string imageDirectory)
    {
        if (_filesByDirectory.TryGetValue(imageDirectory, out var cached))
        {
            return cached;
        }

        IReadOnlyList<string> files;
        try
        {
            files = Directory.Exists(imageDirectory)
                ? Directory.EnumerateFiles(imageDirectory, "*.webp").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        _filesByDirectory[imageDirectory] = files;
        return files;
    }

    /// <summary>1枚を読む。読めなければ null。</summary>
    public BitmapSource? Load(string path)
    {
        if (_byPath.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var image = Decode(path);
        _byPath[path] = image;
        return image;
    }

    /// <summary>フォルダ内の最初の1枚。一覧表示の初期状態に使う。</summary>
    public BitmapSource? LoadFirst(string imageDirectory)
    {
        var files = ListFiles(imageDirectory);
        return files.Count == 0 ? null : Load(files[0]);
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            using var image = Image.Load<Bgra32>(path);
            var stride = image.Width * 4;
            var buffer = new byte[stride * image.Height];
            image.CopyPixelDataTo(buffer);

            var bitmap = BitmapSource.Create(
                image.Width,
                image.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                buffer,
                stride);

            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return null;
        }
    }
}
