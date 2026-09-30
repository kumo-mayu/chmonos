using BoothAssetManager.Core.Services;
using System.Windows;

namespace BoothAssetManager.App.Services;

/// <summary>出すはずだった知らせ・確認の窓の中身（<see cref="Notice.Intercept"/> が受ける）。</summary>
internal sealed record NoticeRequest(
    string Text, string Caption, MessageBoxButton Button, MessageBoxImage Icon, MessageBoxResult DefaultResult);

/// <summary>
/// 画面から出す知らせ・確認の窓を通す1か所（<see cref="MessageBox"/> の代わり）。
///
/// **なぜ1か所に集めるか：**出した文言と押されたボタンを足跡に残すため（<see cref="UiTrace"/>・ユーザ指示 2026-09-20）。
///
/// **出すのは自前の窓（<see cref="Views.NoticeWindow"/>）。**Windows の MessageBox は Windows が描くので、
/// 暗い表の間も白いまま残っていた（ユーザ指示 2026-09-29）。引数と返り値は MessageBox のままにしてあり、
/// ボタンの並び・既定・Esc の答え・持ち主の決め方も MessageBox に合わせる（呼ぶ所はその動きを前に書かれている）。
/// 手前に出したいときは <see cref="FrontNotice"/> を使う
/// </summary>
internal static class Notice
{
    public static MessageBoxResult Show(
        string text,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var answer = Present(owner: null, text, caption, button, icon, defaultResult);
        Trace(caption, text, button, answer);
        return answer;
    }

    /// <summary>持ち主付き（<see cref="FrontNotice"/> から。主の窓の真ん中に出て、後ろに隠れない）。</summary>
    public static MessageBoxResult Show(
        Window owner,
        string text,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var answer = Present(owner, text, caption, button, icon, defaultResult);
        Trace(caption, text, button, answer);
        return answer;
    }

    /// <summary>
    /// 窓を出す代わりに、出すはずだった文を受けて答えを返す口。**アプリでは null のまま**（窓を出す）。
    /// 試験（BoothAssetManager.App.Tests）と、窓を出さずに画面を描く台（tools/ViewShot）が入れる：
    /// 窓を出すと答える人がいないので止まったままになり、隣で使っている画面の上にも出てしまう。
    /// 文言と、答えごとの続きの動きを、窓なしで確かめられる
    /// </summary>
    internal static Func<NoticeRequest, MessageBoxResult>? Intercept { get; set; }

    private static MessageBoxResult Present(
        Window? owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        if (Intercept is { } intercept)
        {
            return intercept(new NoticeRequest(text, caption, button, icon, defaultResult));
        }

        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted || !IsHandled(button))
        {
            return ShowWindows(owner, text, caption, button, icon, defaultResult);
        }

        // 窓は画面のスレッドでしか作れない（色の表もそのスレッドの物）。MessageBox はどのスレッドからでも出せたので、
        // 外から呼ばれたら画面のスレッドへ渡し、答えが出るまで呼んだスレッドを待たせる（前と同じく、答えを返すまで戻らない）
        if (!app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() => Present(owner, text, caption, button, icon, defaultResult));
        }

        var layout = NoticeLayout.For((NoticeButtonSet)button, (NoticeAnswer)defaultResult);

        // 持ち主を渡されなければ、今手前にあるこのアプリの窓（MessageBox が持ち主を省かれたときと同じ決め方）。
        // 無ければ持ち主なしで、画面の中央に出す
        owner ??= ActiveWindow(app);

        // 主の窓がまだ無いとき（保存先の確かめ・既に起動しているとき）に窓を出すと、
        // WPF はその窓を主の窓にし、閉じたところで「最後の窓が閉じた」としてアプリを終える。
        // MessageBox は WPF の窓ではないのでどちらも起きなかった。出している間だけ止め、閉じたら戻す
        var shutdownMode = app.ShutdownMode;
        var mainWindow = app.MainWindow;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            return Views.NoticeWindow.Ask(owner, text, caption, layout, icon);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Windows.Markup.XamlParseException)
        {
            // 窓を出せない所（窓が閉じかけている・読み込みの失敗）でも、知らせだけは届ける
            Core.Diagnostics.AppLog.Error("知らせの窓", exception);
            return ShowWindows(owner is { IsLoaded: true } ? owner : null, text, caption, button, icon, defaultResult);
        }
        finally
        {
            if (!ReferenceEquals(app.MainWindow, mainWindow) && app.MainWindow is Views.NoticeWindow)
            {
                app.MainWindow = mainWindow;
            }

            app.ShutdownMode = shutdownMode;
        }
    }

    private static bool IsHandled(MessageBoxButton button) => button is
        MessageBoxButton.OK or MessageBoxButton.OKCancel or MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;

    /// <summary>このアプリの、今手前にある窓。持ち主にできるのは出ている窓だけ。</summary>
    private static Window? ActiveWindow(Application app)
    {
        foreach (Window window in app.Windows)
        {
            if (window.IsActive && window.IsVisible)
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>自前の窓を出せないときの逃げ道（Windows の確認の窓）。</summary>
    private static MessageBoxResult ShowWindows(
        Window? owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        return owner is null
            ? MessageBox.Show(text, caption, button, icon, defaultResult)
            : MessageBox.Show(owner, text, caption, button, icon, defaultResult);
    }

    private static void Trace(string caption, string text, MessageBoxButton button, MessageBoxResult answer)
    {
        if (UiTrace.IsOn)
        {
            UiTrace.Write("知らせ", $"「{caption}」{text}｜{button} → {answer}");
        }
    }
}
