using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ViewShot;

/// <summary>
/// 窓を出さずに部品を並べ、画像にする舞台。
///
/// **見えない窓口（出さない HwndSource）に載せる。**どこにも載せずに Measure／Arrange だけで描くこともできるが、
/// それだと部品は「画面に載っていない」ままで、Loaded が来ず IsVisible も偽のままになる。
/// このアプリの View は Loaded で読み込みを始める物が多いので、載っていないと空の画面しか描けない。
/// 窓口は親をメッセージ専用（HWND_MESSAGE）にして作り、一度も出さない——画面にもタスクバーにも出ず、フォーカスも取らない。
/// </summary>
internal sealed class Stage : IDisposable
{
    // HWND_MESSAGE。この親を持つ窓は画面に出ない・並びに入らない・他のアプリから列挙されない
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private readonly Canvas _root;
    private readonly Border _frame;
    private double _scale = 1.0;

    public Stage()
    {
        _source = new HwndSource(new HwndSourceParameters("ViewShot")
        {
            WindowStyle = 0,
            ParentWindow = MessageOnlyParent,
            Width = 1,
            Height = 1,
        });

        // 描く範囲の地。主の窓の地（Bg）と同じ鍵を指すので、色の表を差し替えると一緒に変わる
        _frame = new Border();
        _frame.SetResourceReference(Border.BackgroundProperty, "Bg");

        // Canvas は子を欲しがるだけの大きさで並べ、自分の大きさで切らない。窓口の大きさ（1×1）に関係なく、決めた幅と高さで並ぶ
        _root = new Canvas();
        _root.Children.Add(_frame);
        _source.RootVisual = _root;
    }

    public FrameworkElement? Content => _frame.Child as FrameworkElement;

    /// <summary>描く物を載せる。幅・高さが null なら中身に合わせる（小窓）。</summary>
    public void Show(FrameworkElement content, double? width, double? height)
    {
        _frame.Child = content;
        Resize(width, height);
    }

    public void Resize(double? width, double? height)
    {
        _frame.Width = width ?? double.NaN;
        _frame.Height = height ?? double.NaN;
    }

    /// <summary>
    /// 色の表を差し替えた後に呼ぶ。**舞台の上の部品へ、色が変わったことを届ける。**
    ///
    /// アプリは色の表（App.xaml の最初の合わせた辞書）を入れ替え、WPF が「資源が変わった」をアプリの窓へ配る。
    /// 配る先は窓だけで、窓の外に載せた舞台には届かない（入れ替えても、先に作った部品は前の色のまま残った）。
    /// 舞台の根にも同じ表（同じ物）を合わせておき、入れ替えのたびに差し替える。根の資源が変わると、WPF はその下の部品へ配る。
    /// 同じ表なので、部品が引く色はアプリの窓の中と変わらない
    /// </summary>
    public void SyncColorTable()
    {
        var table = Application.Current.Resources.MergedDictionaries.FirstOrDefault(dictionary =>
        {
            var source = dictionary.Source?.OriginalString ?? string.Empty;
            return source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase);
        }) ?? throw new InvalidOperationException("アプリの色の表（Themes/Light.xaml か Dark.xaml）が見つかりません。");

        var merged = _root.Resources.MergedDictionaries;
        if (merged.Count == 1 && ReferenceEquals(merged[0], table))
        {
            return;
        }

        if (merged.Count == 0)
        {
            merged.Add(table);
        }
        else
        {
            merged[0] = table;
        }
    }

    /// <summary>
    /// 表示の倍率（Windows の拡大率に当たる物）。画素の細かさだけでなく、部品が「自分のいる画面の倍率」として見る値も替える
    /// （画素に合わせる丸め・1px の線の太さ）。替えないと、実行した PC の拡大率で丸めた物を引き伸ばすだけになる
    /// </summary>
    public void SetScale(double scale)
    {
        _scale = scale;
        VisualTreeHelper.SetRootDpi(_root, new DpiScale(scale, scale));

        // 絵を読む大きさ（カードの絵は画素で読む）。主の窓が置かれたモニターの拡大率として渡している値
        Chmonos.App.Services.DisplayScale.SetMonitor(scale);

        // 倍率が変わっても並べ直しは自動では起きない（窓口の側の拡大率の知らせを経ていないため）
        InvalidateAll(_root);
    }

    private static void InvalidateAll(DependencyObject node)
    {
        if (node is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateVisual();
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            InvalidateAll(VisualTreeHelper.GetChild(node, i));
        }
    }

    /// <summary>
    /// 見た目が落ち着くまで待つ。**描いた画像が続けて同じになったら落ち着いたとみなす。**
    ///
    /// 画面は読み込みを裏で走らせ、届いた物から出す（一覧・「見つかりません」の印・絵）。何を待てばよいかは画面ごとに違い、
    /// 絵の読み込み（ThumbnailLoader）には「全部読み終えた」の知らせが無い。描いた結果そのものを見れば、どの画面でも同じ待ち方で済む。
    /// 0.4秒変わらなければ終わりとする——裏の読み込みは数十msで届き、入力の保存の遅れ（0.8秒）のような長い待ちは見た目を変えない。
    /// 回り続ける部品（長さの無い進み具合の棒）があると揃わないので、上限で打ち切って「落ち着かなかった」と返す。
    /// </summary>
    public async Task<bool> SettleAsync(TimeSpan? limit = null)
    {
        var deadline = Stopwatch.StartNew();
        var max = limit ?? TimeSpan.FromSeconds(8);
        var quiet = Stopwatch.StartNew();
        byte[]? previous = null;

        while (deadline.Elapsed < max)
        {
            await IdleAsync();
            var current = Pixels(Render());
            if (previous is not null && current.AsSpan().SequenceEqual(previous))
            {
                if (quiet.Elapsed >= TimeSpan.FromMilliseconds(400))
                {
                    return true;
                }
            }
            else
            {
                quiet.Restart();
                previous = current;
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>画面のスレッドに溜まった仕事（並べ直し・結び付け・読み込みの続き）を全部流す。</summary>
    public static async Task IdleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>条件が成り立つまで待つ（読み込みが届いた・行が並んだ）。届かなければ、何を待っていたかを言って止まる。</summary>
    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? limit = null)
    {
        var clock = Stopwatch.StartNew();
        var max = limit ?? TimeSpan.FromSeconds(15);
        while (!condition())
        {
            if (clock.Elapsed > max)
            {
                throw new TimeoutException($"{max.TotalSeconds:0} 秒待っても届きませんでした：{what}");
            }

            await Task.Delay(20);
            await IdleAsync();
        }
    }

    public BitmapSource Render()
    {
        _root.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(_frame.ActualWidth * _scale));
        var height = Math.Max(1, (int)Math.Ceiling(_frame.ActualHeight * _scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * _scale, 96 * _scale, PixelFormats.Pbgra32);
        bitmap.Render(_frame);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>部品の四角（画素）。切り出しに使う。舞台の外・畳まれている部品は null。</summary>
    public Int32Rect? BoundsOf(FrameworkElement element, double margin)
    {
        if (!element.IsDescendantOf(_frame) || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        var box = element.TransformToAncestor(_frame)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        box.Inflate(margin, margin);
        box.Intersect(new Rect(0, 0, _frame.ActualWidth, _frame.ActualHeight));
        if (box.IsEmpty)
        {
            return null;
        }

        return ToPixels(box);
    }

    public Int32Rect ToPixels(Rect dips)
    {
        var left = (int)Math.Floor(dips.Left * _scale);
        var top = (int)Math.Floor(dips.Top * _scale);
        var right = (int)Math.Ceiling(dips.Right * _scale);
        var bottom = (int)Math.Ceiling(dips.Bottom * _scale);
        return new Int32Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static BitmapSource Crop(BitmapSource image, Int32Rect region)
    {
        var x = Math.Clamp(region.X, 0, image.PixelWidth - 1);
        var y = Math.Clamp(region.Y, 0, image.PixelHeight - 1);
        var width = Math.Clamp(region.Width, 1, image.PixelWidth - x);
        var height = Math.Clamp(region.Height, 1, image.PixelHeight - y);
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }

    public static byte[] Pixels(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public void Dispose() => _source.Dispose();
}
