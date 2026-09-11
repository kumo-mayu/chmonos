using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    /// <summary>
    /// 検索カードに出すときの長辺（DIP）。カードの画像枠は幅およそ226×高さ200なので、
    /// これより大きく復号しても画面では縮めて描かれるだけになる。
    ///
    /// **カードだけは表示の大きさで復号する（#71）。**長辺384pxのまま作ると、
    /// WPFの画像1枚が管理外に約1MiBを取る（576KBの生ピクセルが1MiB単位で確保される）。
    /// 2000件を最後までスクロールしたとき、それが115個・115MB並んでいた。
    /// 240pxに縮めて作ると同じ100枚が112MB→42MBになり、1MiBの確保も消えた（画面なしの試験で実測）。
    /// </summary>
    public const int CardEdgeDip = 240;

    /// <summary>
    /// 商品ページ・編集画面の小さな一覧（68×54 / 58×46 DIP）に出すときの長辺。
    /// 大きく出す1枚は原寸で読むので、一覧は見分けが付けば足りる。
    /// 以前は一覧も原寸で作っていて、編集画面で30件送ると1MiBずつの画像が107個（107MB）並んでいた（#71）。
    /// </summary>
    public const int TileEdgeDip = 96;

    /// <summary>キャッシュの鍵。同じファイルでもカード用と原寸は別物として持つ。</summary>
    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>フォルダの中身と、数えたときのフォルダの更新時刻。</summary>
    private readonly Dictionary<string, (IReadOnlyList<string> Files, DateTime WrittenAt)> _filesByDirectory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly long _budgetBytes;
    private long _usedBytes;
    private long _clock;
    /// <summary>前回GCを急かしてから捨てた量。</summary>
    private long _evictedSinceCollect;

    /// <param name="budgetMegabytes">復号済み画像を保持する上限。</param>
    public ThumbnailLoader(int budgetMegabytes = Core.Models.AppSettings.DefaultThumbnailCacheBudgetMb)
    {
        _budgetBytes = Math.Max(16, budgetMegabytes) * 1024L * 1024L;
    }

    /// <summary>キャッシュが保持している復号済み画像の枚数。</summary>
    public int CachedImageCount => _byKey.Count;

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

    /// <summary>1枚を保存された大きさのまま読む。読めなければ null。</summary>
    public BitmapSource? Load(string path) => Load(path, maxEdgePixels: null);

    /// <summary>
    /// 検索カードに出す1枚を、カードの大きさに縮めて読む。
    /// 画面の拡大率を掛けるので、150%の画面では360pxで作られ、粗くはならない。
    /// </summary>
    public BitmapSource? LoadForCard(string path) => Load(path, EdgePixels(CardEdgeDip));

    /// <summary>小さな一覧に並べる1枚を、一覧の大きさに縮めて読む。</summary>
    public BitmapSource? LoadForTile(string path) => Load(path, EdgePixels(TileEdgeDip));

    private BitmapSource? Load(string path, int? maxEdgePixels)
    {
        var key = maxEdgePixels is { } edge ? $"{path}|{edge}" : path;
        if (_byKey.TryGetValue(key, out var cached))
        {
            cached.LastUsedAt = ++_clock;
            return cached.Image;
        }

        var image = Decode(path, maxEdgePixels);
        Store(key, image);
        return image;
    }

    /// <summary>
    /// 速く流している間に読む長辺（U12・U27）。ユーザ判断で120（96では小さすぎる）。
    /// 止まったら見えている分を <see cref="CardEdgeDip"/> で読み直す。後でユーザが調整するかもしれない
    /// </summary>
    public const int FastCardEdgeDip = 120;

    /// <summary>
    /// 順番待ちの上限。速く流すと、もう見えていない行の読みかけが溜まり、止まった所の絵が後回しになる。
    /// 超えたら古いものから取り消す（そのカードは見えていないので、戻ってきたときにもう一度頼まれる）。
    /// 画面に一度に出るカードは多くて30枚ほどなので、その1.5倍
    /// </summary>
    private const int MaxQueuedDecodes = 48;

    /// <summary>同時に復号する数。画面のスレッドを空けておくため、芯の半分まで（2〜4）。</summary>
    private static readonly int MaxParallelDecodes = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>裏で読む1件。同じ絵を待つカードが複数あれば、読み終わりにまとめて知らせる。</summary>
    private sealed class DecodeRequest
    {
        public required string Key { get; init; }

        public required string Path { get; init; }

        public required int Edge { get; init; }

        public List<Action> Waiters { get; } = [];

        /// <summary>順番待ちの列での位置。読み始めたら null。</summary>
        public LinkedListNode<DecodeRequest>? Node { get; set; }
    }

    /// <summary>順番待ち。先頭が新しい——今見えているカードを先に読む。</summary>
    private readonly LinkedList<DecodeRequest> _queue = new();

    /// <summary>頼まれているもの（順番待ちと読んでいる最中）。同じ絵を二重に読まないために覚えておく。</summary>
    private readonly Dictionary<string, DecodeRequest> _requested = new(StringComparer.OrdinalIgnoreCase);
    private int _running;

    /// <summary>速く流している間か。検索画面が知らせる（U12・U27）。</summary>
    public bool IsFastScrolling { get; set; }

    /// <summary>
    /// 検索カードの止まっているときの1枚。手元にあればすぐ返し、無ければ裏で読み始める（U12）。
    /// 読み終わったら <paramref name="onLoaded"/> を画面のスレッドで呼ぶので、そこで描き直させる。
    ///
    /// 以前は画面のスレッドでその場で読んで縮めていて、速くスクロールすると新しい行が出るたびに
    /// 1枚ずつ画面が止まった（保持の上限を増やしても、初めて見る所では同じだった）。
    /// 復号は状態を触らない関数で、作った画像は凍結しているので別のスレッドで作ってよい。
    /// 保持（辞書）と順番待ちを触るのは画面のスレッドだけにする。
    ///
    /// 速く流している間は小さく（<see cref="FastCardEdgeDip"/>）読む。完全には止めない——
    /// 流しながらでも絵で見つけられるように（ユーザ判断）。正規の大きさを頼まれたときに
    /// 小さい方しか無ければ、読み終わるまでそれを出しておく（灰色に戻すとちらつく）。
    /// </summary>
    public BitmapSource? PeekForCard(string path, Action onLoaded)
    {
        var normalKey = $"{path}|{EdgePixels(CardEdgeDip)}";
        if (_byKey.TryGetValue(normalKey, out var normal))
        {
            normal.LastUsedAt = ++_clock;
            return normal.Image;
        }

        var fastEdge = EdgePixels(FastCardEdgeDip);
        var fastKey = $"{path}|{fastEdge}";
        _byKey.TryGetValue(fastKey, out var small);

        if (IsFastScrolling)
        {
            if (small is not null)
            {
                small.LastUsedAt = ++_clock;
                return small.Image;
            }

            Request(fastKey, path, fastEdge, onLoaded);
            return null;
        }

        Request(normalKey, path, EdgePixels(CardEdgeDip), onLoaded);

        if (small is not null)
        {
            small.LastUsedAt = ++_clock;
        }

        return small?.Image;
    }

    /// <summary>
    /// 小さな一覧（アバター画面の頭の絵など）の1枚。手元にあればすぐ返し、無ければ裏で読む（U18）。
    /// その場で読むと、アバター画面を初めて開いたときに見えている約30行ぶんを画面のスレッドで読み、
    /// 一覧が出るまでが4.3秒から7.7秒に延びた。
    /// </summary>
    public BitmapSource? PeekForTile(string path, Action onLoaded)
    {
        var edge = EdgePixels(TileEdgeDip);
        var key = $"{path}|{edge}";
        if (_byKey.TryGetValue(key, out var cached))
        {
            cached.LastUsedAt = ++_clock;
            return cached.Image;
        }

        Request(key, path, edge, onLoaded);
        return null;
    }

    private void Request(string key, string path, int edge, Action onLoaded)
    {
        if (_requested.TryGetValue(key, out var existing))
        {
            existing.Waiters.Add(onLoaded);

            // もう一度頼まれた＝まだ見えている。列の先頭へ戻す
            if (existing.Node is { } node)
            {
                _queue.Remove(node);
                _queue.AddFirst(node);
            }

            return;
        }

        var request = new DecodeRequest { Key = key, Path = path, Edge = edge };
        request.Waiters.Add(onLoaded);
        request.Node = _queue.AddFirst(request);
        _requested[key] = request;

        while (_queue.Count > MaxQueuedDecodes && _queue.Last is { } oldest)
        {
            _queue.RemoveLast();
            _requested.Remove(oldest.Value.Key);
        }

        Pump();
    }

    private void Pump()
    {
        while (_running < MaxParallelDecodes && _queue.First is { } node)
        {
            _queue.RemoveFirst();
            var request = node.Value;
            request.Node = null;
            _running++;

            Task.Run(() => Decode(request.Path, request.Edge)).ContinueWith(
                done =>
                {
                    _running--;
                    _requested.Remove(request.Key);

                    if (!_byKey.ContainsKey(request.Key))
                    {
                        Store(request.Key, done.IsCompletedSuccessfully ? done.Result : null);
                    }

                    foreach (var waiter in request.Waiters)
                    {
                        waiter();
                    }

                    Pump();
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void Store(string key, BitmapSource? image)
    {
        var entry = new Entry
        {
            Image = image,
            // 復号に失敗したものは「読めない」という結果自体に意味があるので残すが、容量には数えない
            Bytes = image is null ? 0 : (long)image.PixelWidth * image.PixelHeight * 4,
            LastUsedAt = ++_clock,
        };

        _byKey[key] = entry;
        _usedBytes += entry.Bytes;
        EvictIfNeeded();
    }

    /// <summary>
    /// 同じ場所のファイルを取り直したときに呼ぶ。
    /// 名前がURLで決まる画像は差し替えで別名になるが、ショップのバナーのように
    /// 場所が固定のものは、覚えている絵を捨てないと古いままになる。
    /// カード用に縮めたものも一緒に捨てる。
    /// </summary>
    public void Forget(string path)
    {
        var prefix = path + "|";
        foreach (var key in _byKey.Keys
            .Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            _usedBytes -= _byKey[key].Bytes;
            _byKey.Remove(key);
        }
    }

    /// <summary>画面の拡大率を掛けた長辺（ピクセル）。窓がまだ無いときは等倍とみなす。</summary>
    private static int EdgePixels(int dip)
    {
        var scale = System.Windows.Application.Current?.MainWindow is { } window
            ? VisualTreeHelper.GetDpi(window).DpiScaleX
            : 1.0;
        return (int)Math.Ceiling(dip * scale);
    }

    private void EvictIfNeeded()
    {
        if (_usedBytes <= _budgetBytes)
        {
            return;
        }

        var target = (long)(_budgetBytes * EvictionTargetRatio);
        foreach (var pair in _byKey.OrderBy(pair => pair.Value.LastUsedAt).ToList())
        {
            if (_usedBytes <= target)
            {
                break;
            }

            _byKey.Remove(pair.Key);
            _usedBytes -= pair.Value.Bytes;
            _evictedSinceCollect += pair.Value.Bytes;
        }

        // 捨てた画像の実体（WPFの管理外の領域）は、GCが回ってファイナライザが走るまで返らない。
        // 縮小して1枚が小さくなった分、GCが急かされず、スクロール中に数百枚ぶん溜まっていた（#71）。
        // スクロールしている間は手が止まらないので、下の MemoryTrim だけでは山が削れない。
        //
        // 以前は若い世代だけ掃かせていたが、保持している間に何度もGCをくぐった画像は古い世代へ上がっていて、
        // それでは回収できなかった（U12：2000件を端まで流した後、管理ヒープは91MBなのに画像が1,578個生きていた）。
        // 古い世代まで、画面を止めない形（背景のGC）で掃かせる
        if (_evictedSinceCollect >= _budgetBytes / 2)
        {
            _evictedSinceCollect = 0;
            GC.Collect(2, GCCollectionMode.Forced, blocking: false);
        }

        MemoryTrim.Request();
    }

    private static BitmapSource? Decode(string path, int? maxEdgePixels)
    {
        try
        {
            using var image = Image.Load<Bgra32>(path);

            // 拡大はしない。元が小さい画像はそのままの大きさで作る
            if (maxEdgePixels is { } edge && (image.Width > edge || image.Height > edge))
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(edge, edge),
                }));
            }

            var stride = image.Width * 4;
            var size = stride * image.Height;

            // 受け渡しの配列は借りて返す。BitmapSource.Create は中身を自分の領域へ写すので、
            // 呼び終われば要らない。毎回 new すると1枚576KBが大きいオブジェクト用のヒープに積もり、
            // スクロールで数百枚読むとその穴がメモリを押し上げていた（#71）
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
            try
            {
                image.CopyPixelDataTo(buffer.AsSpan(0, size));

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
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return null;
        }
    }
}
