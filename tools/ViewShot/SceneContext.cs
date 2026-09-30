using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using BoothAssetManager.App;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace ViewShot;

/// <summary>
/// 場面が使う道具。**アプリのサービス一式・主画面の ViewModel・窓の中身を組む所は、ここ1か所にまとめる。**
///
/// アプリ側の試験（BoothAssetManager.App.Tests）にも ViewModel を試験から組む助けができる。
/// できたら、ここの <see cref="StartAsync"/> の中身をそちらへ寄せる（場面は <see cref="SceneContext"/> しか見ていないので、場面は書き直さなくてよい）。
/// </summary>
internal sealed class SceneContext
{
    private readonly Stage _stage;
    private readonly ShotOptions _options;
    private AppServiceContainer? _services;
    private MainViewModel? _main;

    public SceneContext(Stage stage, ShotOptions options, Scene scene)
    {
        _stage = stage;
        _options = options;
        Scene = scene;

        // 場面が作り物を書く保存先。Isolation.Enter が作業用フォルダへ向けてある
        AppPaths.Default.EnsureCreated();
        Seed = new DataStore(AppPaths.Default);
        Fake = new Fake(Seed);
    }

    public Scene Scene { get; }

    /// <summary>作り物を書く保存先。<see cref="StartAsync"/> の前に書く（サービス一式は起動時に読む物がある）。</summary>
    public DataStore Seed { get; }

    public Fake Fake { get; }

    /// <summary>場面の途中で出ようとした知らせの窓（出さずに既定の答えを返した）。出たら結果に書く。</summary>
    public List<string> Notices { get; } = [];

    public AppServiceContainer Services => _services ?? throw new InvalidOperationException("先に StartAsync を呼ぶ。");

    public MainViewModel Main => _main ?? throw new InvalidOperationException("先に StartAsync を呼ぶ。");

    /// <summary>
    /// アプリの起動と同じ順（App.OnStartup）で、サービス一式と主画面の ViewModel を組む。窓は作らない。
    ///
    /// 設定は組む前に書く：表示の色（組んだ後に設定から当て直されるので、今の色を書いておかないと明るい表へ戻る）・
    /// 表示の大きさ・「使っていない間の取得」を切る（起動時の裏の作業が BOOTH へ問い合わせようとする。通信は Isolation が
    /// 止めてあるが、失敗の記録と待ちが裏で回り続けると、見た目が落ち着くのを待つ所が長引く）。
    /// </summary>
    public async Task<MainViewModel> StartAsync(Func<AppSettings, AppSettings>? change = null)
    {
        if (_main is not null)
        {
            return _main;
        }

        var settings = new AppSettings
        {
            ColorTheme = AppTheme.Current.Mode,
            DisplayZoomPercent = _options.ZoomPercent,
            ResumeFetchInBackground = false,
        };
        await Seed.Settings.SaveAsync(change?.Invoke(settings) ?? settings);

        _services = new AppServiceContainer();
        AppTheme.Initialize(_services);
        _main = new MainViewModel(_services);
        return _main;
    }

    /// <summary>主の窓の中身（ナビ・今の画面・下の帯）。窓そのものは出さずに、中身だけを取り出す。</summary>
    public FrameworkElement MainWindow() => Unwrap(new MainWindow { DataContext = Main });

    /// <summary>
    /// 窓を出さずに、その中身を舞台に載せられる形で取り出す。
    ///
    /// 窓は出さない限り自分の中身を並べない（並べるのは窓口ができた後）ので、中身を窓から外して舞台へ移す。
    /// 外すと窓から受けていた物が届かなくなるので、移した先に同じ物を持たせる：
    /// 窓の資源（画面ごとの見た目・ナビの見た目）・名前の表（ElementName の結び付き）・DataContext・地と文字の色。
    /// 届かなくなる物もある——祖先の窓を探す結び付き（RelativeSource AncestorType=Window）と <c>Window.GetWindow</c>（台の限界に書いた）。
    /// </summary>
    public static FrameworkElement Unwrap(Window window)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException($"{window.GetType().Name} の中身が部品ではありません。");
        }

        window.Content = null;

        var host = new Border { Child = content, DataContext = window.DataContext };
        host.Resources.MergedDictionaries.Add(window.Resources);
        if (NameScope.GetNameScope(window) is { } names)
        {
            NameScope.SetNameScope(host, names);
        }

        // 地：窓が書いていればそれを追う（色の表の鍵なら差し替えにも付いてくる）。書いていなければ Windows の窓の地
        if (window.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue)
        {
            host.SetBinding(Border.BackgroundProperty, new Binding(nameof(Window.Background)) { Source = window });
        }
        else
        {
            host.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
        }

        // 文字：自前の見た目を持たない文字は窓の文字の色を継ぐ。窓の既定は Windows の窓の文字の色（色の表が置き換えている）
        host.SetResourceReference(TextElement.ForegroundProperty, SystemColors.WindowTextBrushKey);

        host.UseLayoutRounding = window.UseLayoutRounding;
        host.SnapsToDevicePixels = window.SnapsToDevicePixels;
        host.MinWidth = window.MinWidth;
        host.MinHeight = window.MinHeight;
        host.MaxWidth = window.MaxWidth;
        host.MaxHeight = window.MaxHeight;

        // 幅を決め打ちにしている小窓（SizeToContent が高さだけ）は、その幅で並べる。窓の枠の分（左右で約16）は中身の幅に入らない。
        // 大きさを変えられる窓（主の窓）の幅は書いてあっても使わない——場面と --width が決める
        if (window.SizeToContent == SizeToContent.Height && !double.IsNaN(window.Width))
        {
            host.Width = Math.Max(0, window.Width - WindowFrameWidth);
        }

        return host;
    }

    // 大きさを変えられない小窓の枠（左右の合計）。Windows 10/11 の既定の見た目で 16（8+8。うち見えるのは1pxずつ）
    private const double WindowFrameWidth = 16;

    /// <summary>部品を1つだけ描くときの枠（面の色の上に、余白を付けて置く）。</summary>
    public static FrameworkElement OnSurface(FrameworkElement part, double padding = 16)
    {
        var host = new Border { Child = part, Padding = new Thickness(padding) };
        host.SetResourceReference(Border.BackgroundProperty, "Surface");
        return host;
    }

    /// <summary>舞台に載せて、最初の読み込みが落ち着くまで待つ。載せた後でないと、View は読み込みを始めない（Loaded で始める）。</summary>
    public async Task PresentAsync(FrameworkElement root)
    {
        _stage.Show(root, _options.Width ?? Scene.Width, _options.Height ?? Scene.Height);
        await _stage.SettleAsync();
    }

    public Task SettleAsync() => _stage.SettleAsync();

    public static Task UntilAsync(Func<bool> condition, string what) => Stage.UntilAsync(condition, what);

    /// <summary>今の画面の ViewModel を型で受ける。違う画面が出ていたら、何が出ていたかを言って止まる。</summary>
    public T Screen<T>() where T : class
        => Main.CurrentViewModel as T
           ?? throw new InvalidOperationException(
               $"今の画面は {Main.CurrentViewModel?.GetType().Name ?? "（無し）"} で、{typeof(T).Name} ではありません。");

    public void Dispose()
    {
        _main?.StopBackgroundWork();
        _services?.Dispose();
    }
}

/// <summary>載せた部品の中から、目当ての部品を探す（切り出す範囲・選ぶ行）。</summary>
internal static class Look
{
    public static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in All<T>(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>その文字を出している部品（見えている物だけ）。</summary>
    public static TextBlock? Text(DependencyObject root, string text)
        => All<TextBlock>(root).FirstOrDefault(block => block.IsVisible && block.Text.Contains(text, StringComparison.Ordinal));

    public static T? Named<T>(DependencyObject root, string name) where T : FrameworkElement
        => All<T>(root).FirstOrDefault(element => element.Name == name);

    public static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    /// <summary>画面の部品（UserControl）を型で探す。今の画面だけを切り出すのに使う。</summary>
    public static T? View<T>(DependencyObject root) where T : FrameworkElement => All<T>(root).FirstOrDefault();
}
