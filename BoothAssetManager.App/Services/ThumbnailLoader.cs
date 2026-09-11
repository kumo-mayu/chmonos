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
/// 復号結果はパス単位でキャッシュするが、上限を設けて古いものから捨てる。
/// 保持しているのは圧縮前の生ピクセル（Bgra32）で、長辺384pxなら1枚あたり最大576KB、
/// ディスク上の実測平均15KBに対して30倍以上になる。
/// 上限が無いと、ライブラリが数百件になった時点でメモリを食い潰す。
/// </summary>
public sealed class ThumbnailLoader
{
    private sealed class Entry
    {
        public required BitmapSource? Image { get; init; }

        public required long Bytes { get; init; }

        /// <summary>最後に読まれた順番。小さいものから捨てる。</summary>
        public long LastUsedAt { get; set; }
    }

    /// <summary>上限を超えたら、ここまで減らしてから戻る。毎回1枚ずつ捨てて並べ直さないため。</summary>
    private const double EvictionTargetRatio = 0.8;

    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>フォルダの中身と、数えたときのフォルダの更新時刻。</summary>
    private readonly Dictionary<string, (IReadOnlyList<string> Files, DateTime WrittenAt)> _filesByDirectory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly long _budgetBytes;
    private long _usedBytes;
    private long _clock;

    /// <param name="budgetMegabytes">復号済み画像を保持する上限。</param>
    public ThumbnailLoader(int budgetMegabytes = 192)
    {
        _budgetBytes = Math.Max(16, budgetMegabytes) * 1024L * 1024L;
    }

    /// <summary>キャッシュが保持している復号済み画像の枚数。</summary>
    public int CachedImageCount => _byPath.Count;

    /// <summary>キャッシュが使っているメモリ量。</summary>
    public long CachedBytes => _usedBytes;

    /// <summary>
    /// そのitemが持つ画像ファイルのパス一覧（表示順）。
    ///
    /// **覚えた一覧は、フォルダの更新時刻が変わっていたら数え直す。**
    /// 以前は一度数えたら覚えたままで、取り込みの④⑤や裏での取得が後から画像を置いても、
    /// 起動し直すまで「画像が無い」ままだった（友人の報告）。
    /// ファイルを足すと入れ物のフォルダの更新時刻が変わるので、時刻を1回見るだけで気付ける
    /// （画像の保存名はURLのハッシュなので、増えるときは必ず新しい名前になる）。
    /// </summary>
    public IReadOnlyList<string> ListFiles(string imageDirectory)
    {
        var writtenAt = LastWriteOf(imageDirectory);
        if (_filesByDirectory.TryGetValue(imageDirectory, out var cached) && cached.WrittenAt == writtenAt)
        {
            return cached.Files;
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

        _filesByDirectory[imageDirectory] = (files, writtenAt);
        return files;
    }

    /// <summary>フォルダの更新時刻。無ければ最小値（作られたら変わったと分かる）。</summary>
    private static DateTime LastWriteOf(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetLastWriteTimeUtc(directory) : DateTime.MinValue;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// このitemの画像フォルダを数え直させる。
    /// 「この商品の画像取得を優先」で枚数が増えたときに呼ぶ。
    /// 覚えたままだと、落としたばかりの画像が一覧に出てこない。
    /// </summary>
    public void ForgetDirectory(string imageDirectory) => _filesByDirectory.Remove(imageDirectory);

    /// <summary>1枚を読む。読めなければ null。</summary>
    public BitmapSource? Load(string path)
    {
        if (_byPath.TryGetValue(path, out var cached))
        {
            cached.LastUsedAt = ++_clock;
            return cached.Image;
        }

        var image = Decode(path);
        var entry = new Entry
        {
            Image = image,
            // 復号に失敗したものは「読めない」という結果自体に意味があるので残すが、容量には数えない
            Bytes = image is null ? 0 : (long)image.PixelWidth * image.PixelHeight * 4,
            LastUsedAt = ++_clock,
        };

        _byPath[path] = entry;
        _usedBytes += entry.Bytes;
        EvictIfNeeded();

        return image;
    }

    /// <summary>
    /// 同じ場所のファイルを取り直したときに呼ぶ。
    /// 名前がURLで決まる画像は差し替えで別名になるが、ショップのバナーのように
    /// 場所が固定のものは、覚えている絵を捨てないと古いままになる。
    /// </summary>
    public void Forget(string path)
    {
        if (_byPath.Remove(path, out var entry))
        {
            _usedBytes -= entry.Bytes;
        }
    }

    /// <summary>フォルダ内の最初の1枚。一覧表示の初期状態に使う。</summary>
    private void EvictIfNeeded()
    {
        if (_usedBytes <= _budgetBytes)
        {
            return;
        }

        var target = (long)(_budgetBytes * EvictionTargetRatio);
        foreach (var pair in _byPath.OrderBy(pair => pair.Value.LastUsedAt).ToList())
        {
            if (_usedBytes <= target)
            {
                break;
            }

            _byPath.Remove(pair.Key);
            _usedBytes -= pair.Value.Bytes;
        }
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
