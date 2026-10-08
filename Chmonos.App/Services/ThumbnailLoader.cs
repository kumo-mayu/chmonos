using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Chmonos.App.Services;

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
    /// <summary>
    /// 保持の1件。**WPFの絵ではなく、画素のバイト列で持つ**（U12・3回目）。
    /// WPFの絵は一度画面に出すと、描画のための写しをもう1枚持ち、絵が生きている間はそれも残る。
    /// 絵のまま保持すると、保持1MBにつき全体で約2.2MBになっていた（上限132MBで最大約620MB）。
    /// 画素で持てば、写しができるのは画面に出している分だけになる
    /// </summary>
    private sealed class Entry
    {
        /// <summary>Bgra32 の画素。読めなかったものは null（「読めない」という結果自体に意味があるので残す）。</summary>
        public required byte[]? Pixels { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

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
    public static int CardEdgeDip => CardMetrics.EdgeDip;

    /// <summary>
    /// 商品ページ・編集画面の小さな一覧（68×54 / 58×46 DIP）に出すときの長辺。
    /// 大きく出す1枚は原寸で読むので、一覧は見分けが付けば足りる。
    /// 以前は一覧も原寸で作っていて、編集画面で30件送ると1MiBずつの画像が107個（107MB）並んでいた（#71）。
    /// </summary>
    public const int TileEdgeDip = 96;

    /// <summary>
    /// 一覧の行の頭に出す小さな絵（30〜38DIP の四角・UniformToFill）の**短い辺**。
    /// 枠いっぱいに切り抜いて出すので、足りないと困るのは短い辺の方（長い辺で縮めると、横長の絵は短い辺が枠より小さくなりぼやける）。
    /// 一番大きい枠（38DIP）に少し余らせて40。150%の画面では60pxで作られ、57pxの枠を満たす。
    /// 96DIP の長辺で読んでいた頃に比べ、正方形の絵なら画素は約6分の1（150%で144×144 → 60×60）
    /// </summary>
    public const int IconShortEdgeDip = 40;

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

    /// <summary>
    /// 画素から作ったWPFの絵を、直近に使った分だけ持っておく数。
    /// 画面に並ぶカードは多くて40枚ほどで、なぞって切り替える分と商品ページの一覧を足しても収まる。
    /// ここを超えた古い絵は手放す（画面の部品が使っていれば、それが離すまでは生きている）
    /// </summary>
    private const int MaxLiveBitmaps = 64;

    /// <summary>画素から作ったWPFの絵。同じ絵を読むたびに作り直さないために、直近の分だけ持つ。</summary>
    private readonly Dictionary<string, (BitmapSource Image, long LastUsedAt)> _live = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>復号した画素。裏のスレッドで作り、保持へ入れるのは画面のスレッド。</summary>
    private sealed record Decoded(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// 保持している画素から、画面に出すWPFの絵を作る（直近の分は作り直さない）。
    /// 作るのは画素の写しだけなので、復号し直すよりずっと軽い（1枚0.5MB程度の写し）
    /// </summary>
    private BitmapSource? Materialize(string key, Entry entry)
    {
        entry.LastUsedAt = ++_clock;
        if (entry.Pixels is null)
        {
            return null;
        }

        if (_live.TryGetValue(key, out var live))
        {
            _live[key] = (live.Image, _clock);
            return live.Image;
        }

        var bitmap = BitmapSource.Create(
            entry.Width, entry.Height, 96, 96, PixelFormats.Bgra32, null, entry.Pixels, entry.Width * 4);
        bitmap.Freeze();
        _live[key] = (bitmap, _clock);
        CollectAfterBitmaps(entry.Pixels.Length);

        if (_live.Count > MaxLiveBitmaps)
        {
            foreach (var old in _live.OrderBy(pair => pair.Value.LastUsedAt).Take(_live.Count - MaxLiveBitmaps).ToList())
            {
                _live.Remove(old.Key);
            }
        }

        return bitmap;
    }

    /// <summary>前回GCを頼んでから作ったWPFの絵の画素の量。</summary>
    private long _createdSinceCollect;

    /// <summary>
    /// 作った絵がこれだけ溜まったら裏のGCを頼む。画面に並ぶカード（多くて40枚・150%の画面で1枚約0.45MB）の
    /// 1画面ぶん余りで、流している間だけ数秒に1回になる
    /// </summary>
    private const long CreatedBytesPerCollect = 24L * 1024 * 1024;

    /// <summary>
    /// 作ったWPFの絵は、使われなくなっても画素がWPFの管理外に残り、GCが回って後片付けが走るまで返らない。
    /// 流している間は、小さく読んだ絵を止まった所で大きく読み直して差し替えるので、捨てる絵が次々にできる。
    /// 保持の画素を捨てたとき（<see cref="EvictIfNeeded"/>）の合図だけでは足りず、
    /// 2000件を流す途中で、死んだ絵が438個・管理外に約50MB溜まっていた（2026-09-24 にダンプで数えた）。
    /// 作った量で、画面を止めない形（背景のGC）で古い世代まで掃かせる
    /// </summary>
    private void CollectAfterBitmaps(long bytes)
    {
        _createdSinceCollect += bytes;
        if (_createdSinceCollect < CreatedBytesPerCollect)
        {
            return;
        }

        _createdSinceCollect = 0;
        GC.Collect(2, GCCollectionMode.Forced, blocking: false);
    }

    /// <summary>前回若い世代を掃かせてから復号した画素の量。</summary>
    private long _decodedSinceYoungCollect;

    private const long DecodedBytesPerYoungCollect = 8L * 1024 * 1024;

    /// <summary>
    /// 流している間は復号が続き、1枚ごとの作業の割り当てで若い世代が膨らんでから掃かれる。
    /// 若い世代の大きさ（GCgen0size）を8MBにすると山が下がり固まりも減ったが、その設定は環境変数でしか効かず、
    /// runtimeconfig に書いても読まれない（2026-09-24 に GC の回数で確かめた）。同じことを、復号した量で頼む
    /// </summary>
    private void CollectYoungAfterDecodes(long bytes)
    {
        _decodedSinceYoungCollect += bytes;
        if (_decodedSinceYoungCollect < DecodedBytesPerYoungCollect)
        {
            return;
        }

        _decodedSinceYoungCollect = 0;
        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
    }

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
    /// <remarks>
    /// **どのスレッドからでも呼べる**（控えは錠で守る）。一覧の行を裏で組む画面（改変・タグの管理）が、
    /// 行ごとの1枚目を画面のスレッドの外で引けるように。フォルダを見る所は錠の外で行う
    /// </remarks>
    public IReadOnlyList<string> ListFiles(string imageDirectory)
    {
        var writtenAt = LastWriteOf(imageDirectory);
        lock (_filesGate)
        {
            if (_filesByDirectory.TryGetValue(imageDirectory, out var cached) && cached.WrittenAt == writtenAt)
            {
                return cached.Files;
            }
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

        // 覚えるフォルダに上限を付ける。商品を開くたび・カードを出すたびに1つずつ増え、
        // 起動している間は消えなかった（2000件を一巡すると2000件ぶんのパスの一覧を抱えたまま）。
        // 数え直しは時刻を見て一覧を取り直すだけなので、あふれたら全部忘れても重くならない
        lock (_filesGate)
        {
            if (_filesByDirectory.Count >= MaxRememberedDirectories)
            {
                _filesByDirectory.Clear();
            }

            _filesByDirectory[imageDirectory] = (files, writtenAt);
        }

        return files;
    }

    /// <summary><see cref="_filesByDirectory"/> の錠。</summary>
    private readonly object _filesGate = new();

    /// <summary>
    /// 覚えておくフォルダの数。検索画面に一度に並ぶカードは多くて40枚ほどで、行き来する範囲を足しても
    /// 数百で足りる。1件あたりパスが10本前後（約1KB）なので、512件で0.5MB程度
    /// </summary>
    private const int MaxRememberedDirectories = 512;

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
    public void ForgetDirectory(string imageDirectory)
    {
        lock (_filesGate)
        {
            _filesByDirectory.Remove(imageDirectory);
        }
    }

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
        if (!_byKey.TryGetValue(key, out var cached))
        {
            cached = Store(key, Decode(path, maxEdgePixels));
        }

        return Materialize(key, cached);
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

        /// <summary>縮める先の辺（ピクセル）。0 なら保存された大きさのまま。</summary>
        public required int Edge { get; init; }

        /// <summary><see cref="Edge"/> を短い辺に当てるか（頭の小さな絵）。既定は長い辺。</summary>
        public bool ShortEdge { get; init; }

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
    /// <summary>カードの絵を読む今の大きさ（画素）と、その1つ前。前の大きさの絵は、今の大きさを読み終わるまでの代わりに出す。</summary>
    private int _cardEdgePixels;

    private int _previousCardEdgePixels;

    public BitmapSource? PeekForCard(string path, Action onLoaded)
    {
        var normalEdge = EdgePixels(CardEdgeDip);
        if (normalEdge != _cardEdgePixels)
        {
            _previousCardEdgePixels = _cardEdgePixels;
            _cardEdgePixels = normalEdge;
        }

        var normalKey = $"{path}|{normalEdge}";
        if (_byKey.TryGetValue(normalKey, out var normal))
        {
            return Materialize(normalKey, normal);
        }

        var fastEdge = EdgePixels(FastCardEdgeDip);
        var fastKey = $"{path}|{fastEdge}";
        _byKey.TryGetValue(fastKey, out var small);
        var smallKey = fastKey;

        // カードの大きさを変えて読む大きさの刻みを越えたときは、前の大きさで読んだ絵を読み終わるまで出しておく
        // （灰色に戻すと、スライダーを動かすたびに見えている絵が全部ちらつく）
        if (small is null && _previousCardEdgePixels > 0 && _previousCardEdgePixels != normalEdge)
        {
            var previousKey = $"{path}|{_previousCardEdgePixels}";
            if (_byKey.TryGetValue(previousKey, out small))
            {
                smallKey = previousKey;
            }
        }

        if (IsFastScrolling)
        {
            if (small is not null)
            {
                return Materialize(smallKey, small);
            }

            Request(fastKey, path, fastEdge, onLoaded);
            return null;
        }

        Request(normalKey, path, normalEdge, onLoaded);
        return small is null ? null : Materialize(smallKey, small);
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
            return Materialize(key, cached);
        }

        Request(key, path, edge, onLoaded);
        return null;
    }

    /// <summary>
    /// 一覧の行の頭の小さな絵（<see cref="IconShortEdgeDip"/>）。手元にあればすぐ返し、無ければ裏で読む。
    /// 大きく出す所（アバターのカード・ホバーの窓）と同じ値を使う行では使わない（小さく読んだ絵が引き伸ばされる）
    /// </summary>
    public BitmapSource? PeekForIcon(string path, Action onLoaded) => PeekForFill(path, IconShortEdgeDip, onLoaded);

    /// <summary>
    /// 四角い枠いっぱいに切り抜いて出す（UniformToFill）1枚を、**短い辺**を枠の大きさ（DIP）に合わせて裏で読む。
    /// </summary>
    public BitmapSource? PeekForFill(string path, int shortEdgeDip, Action onLoaded)
    {
        var edge = EdgePixels(shortEdgeDip);
        var key = $"{path}|s{edge}";
        if (_byKey.TryGetValue(key, out var cached))
        {
            return Materialize(key, cached);
        }

        Request(key, path, edge, onLoaded, shortEdge: true);
        return null;
    }

    /// <summary>
    /// 大きく出す1枚を保存された大きさのまま、裏で読む（商品ページ・改変の大きい絵）。
    /// 前は画面のスレッドでその場で読み、絵を送るたび・なぞって切り替えるたびに1枚ぶん止まっていた
    /// </summary>
    public BitmapSource? PeekFull(string path, Action onLoaded)
    {
        if (_byKey.TryGetValue(path, out var cached))
        {
            return Materialize(path, cached);
        }

        Request(path, path, 0, onLoaded);
        return null;
    }

    /// <summary>
    /// 長辺を表示の大きさ（DIP）に縮めて、裏で読む（ショップのバナーなど、保存された大きさより小さく出す1枚）。
    /// </summary>
    public BitmapSource? PeekSized(string path, int edgeDip, Action onLoaded)
    {
        var edge = EdgePixels(edgeDip);
        var key = $"{path}|{edge}";
        if (_byKey.TryGetValue(key, out var cached))
        {
            return Materialize(key, cached);
        }

        Request(key, path, edge, onLoaded);
        return null;
    }

    private void Request(string key, string path, int edge, Action onLoaded, bool shortEdge = false)
    {
        if (_requested.TryGetValue(key, out var existing))
        {
            // 同じ持ち主の知らせは1つだけ持つ。カードは描き直すたびに同じ頼みを繰り返すので、
            // 足し続けると1枚の読み終わりに同じ知らせが何十回も飛び、あふれて取り消されたときも
            // 同じ数だけ知らせ直していた（知らせ直した先がまた頼んで積むので、件数の2乗で膨らむ）
            if (!existing.Waiters.Contains(onLoaded))
            {
                existing.Waiters.Add(onLoaded);
            }

            // もう一度頼まれた＝まだ見えている。列の先頭へ戻す
            if (existing.Node is { } node)
            {
                _queue.Remove(node);
                _queue.AddFirst(node);
            }

            return;
        }

        var request = new DecodeRequest { Key = key, Path = path, Edge = edge, ShortEdge = shortEdge };
        request.Waiters.Add(onLoaded);
        request.Node = _queue.AddFirst(request);
        _requested[key] = request;

        while (_queue.Count > MaxQueuedDecodes && _queue.Last is { } oldest)
        {
            _queue.RemoveLast();
            _requested.Remove(oldest.Value.Key);

            // 取り消した頼みの持ち主には、列が空いたら知らせ直す。まだ見えていれば頼み直し、見えていなければ何も起きない。
            // 知らせないと、見えたまま取り消された絵が二度と来ない——ショップ一覧は広い窓で一度に56枚ほど並び、
            // 最初に頼んだ上の段のアイコンが頭文字のままになった（ユーザ指摘 2026-09-12）。
            // 同じ持ち主は1回だけ知らせる（何度取り消されても、知らせ直しは1回で足りる）
            foreach (var waiter in oldest.Value.Waiters)
            {
                if (_droppedSet.Add(waiter))
                {
                    _dropped.Add(waiter);
                }
            }
        }

        Pump();
    }

    /// <summary>順番待ちからあふれて取り消した頼みの持ち主。列が空いたら知らせ直す。</summary>
    private readonly List<Action> _dropped = [];

    /// <summary><see cref="_dropped"/> に同じ持ち主を二度入れないための控え（並びは <see cref="_dropped"/> が持つ）。</summary>
    private readonly HashSet<Action> _droppedSet = [];

    /// <summary>列が空いたら、取り消した頼みの持ち主に知らせ直す（見えていれば頼み直す）。</summary>
    private void RetryDropped()
    {
        if (_queue.Count > 0 || _dropped.Count == 0)
        {
            return;
        }

        var retry = _dropped.ToList();
        _dropped.Clear();
        _droppedSet.Clear();
        foreach (var waiter in retry)
        {
            waiter();
        }
    }

    private void Pump()
    {
        while (_running < MaxParallelDecodes && _queue.First is { } node)
        {
            _queue.RemoveFirst();
            var request = node.Value;
            request.Node = null;
            _running++;

            Task.Run(() => Decode(request.Path, request.Edge > 0 ? request.Edge : null, request.ShortEdge)).ContinueWith(
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
                    RetryDropped();
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// <summary>
    /// 読めなかった覚え1件のおおよその大きさ。鍵の文字列（1字2バイト）と、入れ物・辞書の1行の分（実物で数百バイト）。
    /// 正確でなくてよい：予算の中で古い順に追い出されることが目的
    /// </summary>
    internal static long FailedEntryBytes(string key) => key.Length * 2L + 200;

    private Entry Store(string key, Decoded? decoded)
    {
        var entry = new Entry
        {
            Pixels = decoded?.Pixels,
            Width = decoded?.Width ?? 0,
            Height = decoded?.Height ?? 0,
            // 復号に失敗したものは「読めない」という結果自体に意味があるので残す。画素は無いが、
            // 覚え1件ぶん（場所の文字列と入れ物）を数える（外部の点検 2026-10-07）。0 と数えると予算を超えないので
            // 追い出しが始まらず、読めない画像を見て回るほど、覚えだけが増え続けた
            Bytes = decoded?.Pixels.LongLength ?? FailedEntryBytes(key),
            LastUsedAt = ++_clock,
        };

        _byKey[key] = entry;
        _usedBytes += entry.Bytes;
        EvictIfNeeded();
        CollectYoungAfterDecodes(entry.Bytes);
        return entry;
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
            _live.Remove(key);
        }
    }

    /// <summary>
    /// 画面の拡大率と表示の大きさを掛けた辺（ピクセル）。窓がまだ無いときは等倍とみなす。
    /// 倍率が変わると鍵（辺の画素）が変わるので、前の倍率で読んだ絵は使われなくなり、古い順に捨てられる
    /// </summary>
    private static int EdgePixels(int dip) => DisplayScale.Pixels(dip);

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

    /// <summary>
    /// 復号して、Bgra32 の画素を返す。**保持するのは画素そのもの**なので、配列は1枚ごとに作る
    /// （以前は借りた配列から WPF の絵へ写して返していた。今は絵を作るのは画面に出すときだけ）。
    /// </summary>
    private static Decoded? Decode(string path, int? maxEdgePixels, bool shortEdge = false)
    {
        try
        {
            // 寸法を頭だけ読んで確かめてから復号する（ImageLimits）。大きすぎる物は読めない画像と同じ扱い
            if (Core.Images.ImageLimits.IsTooLarge(Image.Identify(Core.Images.ImageLimits.FirstFrame, path)))
            {
                return null;
            }

            using var image = Image.Load<Bgra32>(Core.Images.ImageLimits.FirstFrame, path);

            // 拡大はしない。元が小さい画像はそのままの大きさで作る
            if (shortEdge && maxEdgePixels is { } shortSide && Math.Min(image.Width, image.Height) > shortSide)
            {
                // 短い辺を合わせる（Min は短い辺が指定に届くまで縮め、拡大はしない）
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Min,
                    Size = new Size(shortSide, shortSide),
                }));
            }
            else if (!shortEdge && maxEdgePixels is { } edge && (image.Width > edge || image.Height > edge))
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(edge, edge),
                }));
            }

            var pixels = new byte[image.Width * 4 * image.Height];
            image.CopyPixelDataTo(pixels);
            return new Decoded(pixels, image.Width, image.Height);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return null;
        }
    }
}
