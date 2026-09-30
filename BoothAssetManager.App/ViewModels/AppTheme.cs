using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using Microsoft.Win32;

namespace BoothAssetManager.App.ViewModels;

/// <summary>選べる表示の色の1つ（設定画面の一覧の1行）。</summary>
public sealed class ColorThemeOption
{
    public required ColorThemeMode Mode { get; init; }

    public string Label => ColorTheme.Label(Mode);

    /// <summary>読み上げと自動操作から見える名前。既定だと型名になる</summary>
    public override string ToString() => Label;
}

/// <summary>
/// 表示の色（明るい／暗い／Windows に合わせる。ユーザ指示 2026-09-29）。設定の <c>colorTheme</c> に覚える。
///
/// **当て方は色の表の差し替え。**App.xaml が合わせている色の表（<c>Themes/Light.xaml</c>）を <c>Themes/Dark.xaml</c> と入れ替える。
/// 画面は色を DynamicResource で指しているので、入れ替えるとその場で全部の色が変わる（起動し直さなくてよい）。
/// DynamicResource にした代わりの重さは、窓を出さずにカード2000枚を作って測った（2026-09-29）：作る時間の差はぶれの中、
/// 保持は参照1つあたり約120バイト（カード1枚に約60の参照で、2000枚すべてを作っても +14MB。一覧は見えている分しか作らない）。
///
/// 窓の題の帯（Windows が描く所）は色の表が届かないので、窓ごとに DWM に暗くするよう頼む（<see cref="Watch"/>）。
/// 知らせと確認の窓は自前の窓（<c>Views/NoticeWindow</c>）なので表に従う。白いまま残るのは、Windows が描くファイルとフォルダを選ぶ窓だけ。
/// </summary>
public sealed class AppTheme : ViewModelBase
{
    private static readonly Uri LightUri = new("/BoothAssetManager.App;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("/BoothAssetManager.App;component/Themes/Dark.xaml", UriKind.Relative);

    private readonly AppServiceContainer? _services;
    private ColorThemeMode _mode;
    private bool? _appliedDark;

    private AppTheme(AppServiceContainer? services, ColorThemeMode mode)
    {
        _services = services;
        _mode = mode;
    }

    /// <summary>今の値。起動の最初に <see cref="Start"/>、設定を読んだ後に <see cref="Initialize"/> で作り直す。</summary>
    public static AppTheme Current { get; private set; } = new(null, ColorThemeMode.System);

    public static IReadOnlyList<ColorThemeOption> Options { get; } =
    [
        new ColorThemeOption { Mode = ColorThemeMode.System },
        new ColorThemeOption { Mode = ColorThemeMode.Light },
        new ColorThemeOption { Mode = ColorThemeMode.Dark },
    ];

    /// <summary>今、暗い表を当てているか。</summary>
    public static bool IsDark => Current._appliedDark == true;

    /// <summary>
    /// サービス一式を作る前（保存先の確かめ・初回の窓・「既に起動しています」）の色。
    /// 保存先に設定があればその表示の色、無い・読めない（初回・保存先が見つからない）なら既定の「Windows に合わせる」で出す。
    /// Windows の色の設定が変わったのも、ここから見始める
    /// </summary>
    public static void Start()
    {
        UseStoredMode();
        Current.Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 保存先の設定の表示の色を、読めれば当てる（ユーザ判断 2026-09-30）。読むだけで書かない（<see cref="ColorThemePeek"/>）。
    ///
    /// 前は設定の色をサービス一式ができてから当てていたので、その前に出る「既に起動しています」の窓は、
    /// 表示の色を「明るい」にしていても Windows が暗ければ暗く出た。
    /// 保存先の確かめで既定の場所へ切り替えた後にも呼ぶ（そこに前の設定があれば、続く窓はその色で出す）。
    /// 読めなければ何も変えない。
    /// </summary>
    public static void UseStoredMode()
    {
        if (ColorThemePeek.Read() is not { } mode || mode == Current._mode)
        {
            return;
        }

        // 設定画面の「表示の色」はまだ無いので、知らせる相手はいない。当てた色（_appliedDark）は引き継ぎ、同じなら表を入れ替えない
        var appliedDark = Current._appliedDark;
        Current = new AppTheme(null, mode) { _appliedDark = appliedDark };
        Current.Apply();
    }

    /// <summary>設定から作り直す。画面のスレッドで、主の窓を作る前に1回。</summary>
    public static void Initialize(AppServiceContainer services)
    {
        var appliedDark = Current._appliedDark;
        Current = new AppTheme(services, services.Settings.ColorTheme) { _appliedDark = appliedDark };
        Current.Apply();
    }

    /// <summary>閉じるときに外す（Windows の知らせは静的なので、外さないと終わるまで握られる）。</summary>
    public static void Stop() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// <summary>表示の色。変えるとその場で全体の色が変わり、すぐ保存する。</summary>
    public ColorThemeMode Mode
    {
        get => _mode;
        set
        {
            if (SetField(ref _mode, value))
            {
                Apply();
                SaveAsync().Forget();
            }
        }
    }

    /// <summary>
    /// 窓の題の帯を今の色に合わせる。窓ができたら（ハンドルがあれば今、無ければできた所で）当てる。
    /// 主の窓と小窓は作ったところで呼ぶ（小窓は <c>DialogFit.Prepare</c>）
    /// </summary>
    public static void Watch(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyTitleBar(window, IsDark);
        }
        else
        {
            window.SourceInitialized += (_, _) => ApplyTitleBar(window, IsDark);
        }
    }

    /// <summary>
    /// 主の窓を、最初の1コマを描き終えるまで DWM で隠す（点検 2026-09-30：暗い表でも、起動の瞬間に本文が2〜3コマ真っ白に出ていた）。
    /// **窓を出してから WPF が最初の1コマを画面へ出すまでの間は、DWM が白で見せる。**窓の Background も、
    /// 描画の地の色（<c>CompositionTarget.BackgroundColor</c>。0e113b8 で塗った）も、WPF が描いてからしか効かないので、
    /// 前後を撮り比べても白いコマの数は変わらなかった。隠して（DWMWA_CLOAK）おけば、見えた最初のコマから中身が出る。
    /// 見せるのは描き終えた知らせ（<see cref="Window.ContentRendered"/>）の後の、次の描画の回。
    /// 描画の回を数えて早めに見せる形（2回目で見せる）も試したが、3巡のうち2巡で白いコマが1つ残った。
    /// この形は3巡とも白0コマで、中身が出揃う時刻は直す前と同じ（窓ができてから 538〜597ms。前は 560〜607ms）。
    /// 空の暗い地だけのコマ（前は約0.3秒）が出なくなり、その間は窓がまだ見えない。
    /// 何かで知らせが来なくても窓が見えないままにならないよう、2秒で必ず見せる
    /// </summary>
    public static void HideUntilFirstFrame(Window window)
    {
        var shown = false;
        void Reveal()
        {
            if (shown)
            {
                return;
            }

            shown = true;
            SetCloak(window, false);
        }

        window.SourceInitialized += (_, _) => SetCloak(window, true);

        var fallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        fallback.Tick += (_, _) =>
        {
            fallback.Stop();
            Reveal();
        };
        window.SourceInitialized += (_, _) => fallback.Start();

        window.ContentRendered += (_, _) =>
        {
            // ContentRendered は画面のスレッドが描く物を渡した所で来る。描画のスレッドが画面へ出すのはその後なので、
            // 次の描画の回まで待ってから見せる。優先度の低い仕事として後回しにすると、起動の読み込みに押されて約0.3秒遅れた
            void OnNextFrame(object? sender, EventArgs e)
            {
                System.Windows.Media.CompositionTarget.Rendering -= OnNextFrame;
                fallback.Stop();
                Reveal();
            }

            System.Windows.Media.CompositionTarget.Rendering += OnNextFrame;
        };
    }

    private static void SetCloak(Window window, bool cloak)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 効かない Windows（7 以前）では隠れないだけで、今までどおり白く出る。失敗は捨てる
        var value = cloak ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmCloak, ref value, sizeof(int));
    }

    private void Apply()
    {
        var dark = ColorTheme.IsDark(_mode, ReadAppsUseLightTheme());
        if (_appliedDark == dark)
        {
            return;
        }

        _appliedDark = dark;
        if (Application.Current is not { } app)
        {
            return;
        }

        // 色の表を入れ替える。表は App.xaml の最初の合わせた辞書（Light.xaml）
        var dictionaries = app.Resources.MergedDictionaries;
        var index = FindTable(dictionaries);
        var table = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (index < 0)
        {
            dictionaries.Insert(0, table);
        }
        else
        {
            dictionaries[index] = table;
        }

        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window, dark);
        }
    }

    private static int FindTable(IList<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Windows の「アプリ モード」。読めなければ null（古い Windows・壊れた値）。</summary>
    private static int? ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows の色の設定が変わった。「Windows に合わせる」のときだけ追う。
    /// 知らせは Windows の知らせ用のスレッドから来るので、画面のスレッドへ渡す
    /// </summary>
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Current._mode == ColorThemeMode.System)
            {
                Current.Apply();
            }
        });
    }

    private async Task SaveAsync()
    {
        if (_services is null)
        {
            return;
        }

        // 画面のスレッドで値を写してから渡す（当てるのは錠の中で、別のスレッドのことがある）
        var mode = _mode;
        await _services.Commands.ExecuteAsync(new UiCommand.ChangeSettings(settings => settings with
        {
            ColorTheme = mode,
        }));
    }

    // 窓の題の帯を暗くする属性。Windows 10 20H1 以降は 20、それより前（1809〜1909）は 19。
    // 効かない版では何も起きない（帯が明るいままになるだけ）ので、失敗は捨てる
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    // 窓を DWM で隠す属性（DWMWA_CLOAK）。隠しても窓は在り、前面・タスクバー・UI Automation はそのまま
    private const int DwmCloak = 13;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 描き始める前の地も色の表の背景にする。既定は白で、暗い表でも窓を出した一瞬だけ白く見える（ユーザ指摘 2026-09-30）。
        // 窓の Background は WPF が最初の1コマを描くまで効かない。
        // ただしこの色も最初の1コマまでは効かず、起動の瞬間の白は消えなかった（撮り比べて差なし）。そちらは HideUntilFirstFrame で隠す
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target
            && Application.Current?.TryFindResource("Bg") is System.Windows.Media.SolidColorBrush background)
        {
            target.BackgroundColor = background.Color;
        }

        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }
}
