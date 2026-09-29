using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
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
    /// 設定を読む前（保存先の確かめ・初回の窓）の色。初回はまだ設定が無いので、既定の「Windows に合わせる」で出す。
    /// Windows の色の設定が変わったのも、ここから見始める
    /// </summary>
    public static void Start()
    {
        Current.Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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
        // 窓の Background は WPF が最初の1コマを描くまで効かない
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
