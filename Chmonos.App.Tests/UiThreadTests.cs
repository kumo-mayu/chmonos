using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 試験の「画面のスレッド」（<see cref="UiThread"/>）が、アプリの画面のスレッドと同じ性質を持つこと。
/// ここが崩れると、ほかの試験は通っても、アプリと違う動きを確かめていることになる。
/// WPF の部品を作る試験の書き方の見本も兼ねる（窓は出さない）。
/// </summary>
public class UiThreadTests
{
    [Fact]
    public Task 試験の中身は_STAの画面のスレッドで走る() => UiThread.Run(() =>
    {
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        Assert.NotNull(Application.Current);
        Assert.Same(Application.Current!.Dispatcher, Dispatcher.CurrentDispatcher);
        Assert.True(Application.Current.Dispatcher.CheckAccess());
    });

    [Fact]
    public Task awaitの続きは_同じ画面のスレッドへ戻る() => UiThread.Run(async () =>
    {
        var thread = Environment.CurrentManagedThreadId;

        await Task.Run(() => { });
        Assert.Equal(thread, Environment.CurrentManagedThreadId);

        await Task.Delay(1);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
    });

    [Fact]
    public Task WPFの部品を_窓なしで作って値を読める() => UiThread.Run(() =>
    {
        // STA でないスレッドでは、部品を作った時点で落ちる
        var box = new TextBox { Text = "作り物" };
        var panel = new StackPanel();
        panel.Children.Add(box);

        // 窓に載せなくても、大きさは測れる（並べ方の計算を確かめるときに使う）
        panel.Measure(new Size(200, double.PositiveInfinity));

        Assert.Equal("作り物", box.Text);
        Assert.True(panel.DesiredSize.Height > 0);
    });

    [Fact]
    public Task 画面のスレッドへ投げた仕事は_待てば走る() => UiThread.Run(async () =>
    {
        var ran = false;
        _ = Application.Current!.Dispatcher.BeginInvoke(() => ran = true);

        Assert.False(ran);
        await UiThread.Until(() => ran, "投げた仕事が走る");
    });

    [Fact]
    public async Task 画面のスレッドへ投げた仕事の中で落ちると_その試験の失敗になる()
    {
        // 受ける人がいない例外はプロセスごと終わらせる。一式が止まる代わりに、投げた試験を落とす
        var failure = await Assert.ThrowsAsync<AggregateException>(() => UiThread.Run(async () =>
        {
            var ran = false;
            _ = Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                ran = true;
                throw new InvalidOperationException("投げた仕事の中の失敗");
            });
            await UiThread.Until(() => ran, "投げた仕事が走る");
        }));

        Assert.Contains(failure.InnerExceptions, inner => inner.Message == "投げた仕事の中の失敗");
    }

    [Fact]
    public async Task 中身が落ちると_その失敗がそのまま試験に届く()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => UiThread.Run((Action)(() => throw new InvalidOperationException("中身の失敗"))));

        Assert.Equal("中身の失敗", failure.Message);
    }

    [Fact]
    public Task 投げっぱなしの仕事は_Settleで済むまで待てる() => UiThread.Run(async () =>
    {
        var before = FireAndForget.Pending;
        var done = false;
        var gate = new TaskCompletionSource();

        async Task WorkAsync()
        {
            await gate.Task;
            done = true;
        }

        WorkAsync().Forget();
        Assert.Equal(before + 1, FireAndForget.Pending);

        gate.SetResult();
        await UiThread.Settle();

        Assert.True(done);
        Assert.Equal(before, FireAndForget.Pending);
    });

    /// <summary>
    /// Core が裏へ投げた作業（確定の後の検出など）も、Settle は済むまで待つ（2026-10-03）。
    /// 待たずに試験を終えると、後片付けが保存先を消した後も作業が走り続け、一時ファイルを消されて落ち、
    /// その失敗が次の試験のログに混ざって、関係の無い試験が落ちていた
    /// </summary>
    [Fact]
    public Task Coreが裏へ投げた作業も_Settleで済むまで待てる() => UiThread.Run(async () =>
    {
        var done = false;
        var gate = new TaskCompletionSource();

        Chmonos.Core.Diagnostics.BackgroundWork.Run("試験の裏の作業", async () =>
        {
            await gate.Task;
            done = true;
        });
        Assert.True(FireAndForget.Pending > 0);

        gate.SetResult();
        await UiThread.Settle();

        Assert.True(done);
    });

    // ---- 画面のスレッドのタイマーに乗る部品 ----

    [Fact]
    public Task 遅らせた保存は_待っている間に今やると_1回だけ走る() => UiThread.Run(async () =>
    {
        // 間隔は長くしておく：試験では時計で走らせず、「閉じる前に今書く」の道（RunNowAsync）だけを見る
        var runs = 0;
        var save = new Debounced(TimeSpan.FromHours(1), () =>
        {
            runs++;
            return Task.CompletedTask;
        });

        save.Request();
        save.Request();
        await save.RunNowAsync();
        Assert.Equal(1, runs);

        // 待っている物が無ければ、今やるは何もしない
        await save.RunNowAsync();
        Assert.Equal(1, runs);
    });

    [Fact]
    public Task 遅らせた保存は_捨てたら走らない() => UiThread.Run(async () =>
    {
        var runs = 0;
        var save = new Debounced(TimeSpan.FromHours(1), () => runs++);

        save.Request();
        save.Cancel();
        await save.RunNowAsync();

        Assert.Equal(0, runs);
    });

    // ---- 表示の切り替え（XAML が使う変換）----

    private static object Convert(System.Windows.Data.IValueConverter converter, object? value, object? parameter = null)
        => converter.Convert(value, typeof(object), parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void 真偽と空文字で_出す_隠すを切り替える()
    {
        Assert.Equal(Visibility.Visible, Convert(BoolToVisibilityConverter.Instance, true));
        Assert.Equal(Visibility.Collapsed, Convert(BoolToVisibilityConverter.Instance, false));
        Assert.Equal(Visibility.Collapsed, Convert(BoolToVisibilityConverter.Instance, null));

        Assert.Equal(Visibility.Collapsed, Convert(InverseBoolToVisibilityConverter.Instance, true));
        Assert.Equal(Visibility.Visible, Convert(InverseBoolToVisibilityConverter.Instance, false));

        Assert.Equal(Visibility.Collapsed, Convert(EmptyToCollapsedConverter.Instance, ""));
        Assert.Equal(Visibility.Collapsed, Convert(EmptyToCollapsedConverter.Instance, null));
        Assert.Equal(Visibility.Visible, Convert(EmptyToCollapsedConverter.Instance, "文"));

        Assert.Equal(Visibility.Visible, Convert(EmptyToVisibleConverter.Instance, ""));
        Assert.Equal(Visibility.Collapsed, Convert(EmptyToVisibleConverter.Instance, "文"));

        Assert.Equal(false, Convert(InverseBoolConverter.Instance, true));
        Assert.Equal(true, Convert(InverseBoolConverter.Instance, false));
    }

    [Fact]
    public void ナビの札は_0件なら出さない()
    {
        // 0が並ぶと、片付いたことではなく数字そのものが目に入る
        Assert.Equal(Visibility.Collapsed, Convert(ZeroToCollapsedConverter.Instance, 0));
        Assert.Equal(Visibility.Visible, Convert(ZeroToCollapsedConverter.Instance, 3));
        Assert.Equal(Visibility.Collapsed, Convert(ZeroToCollapsedConverter.Instance, null));
    }

    [Fact]
    public void 幅から引いた結果は_0より小さくしない()
    {
        // 狭めた窓で負の幅になると例外になる
        Assert.Equal(180.0, Convert(SubtractConverter.Instance, 200.0, "20"));
        Assert.Equal(0.0, Convert(SubtractConverter.Instance, 10.0, "20"));
        Assert.Equal(DependencyProperty.UnsetValue, Convert(SubtractConverter.Instance, 200.0, "数でない"));
        Assert.Equal(DependencyProperty.UnsetValue, Convert(SubtractConverter.Instance, "幅でない", "20"));
    }

    [Fact]
    public void 扱わないことにした行は_薄くする()
    {
        Assert.Equal(0.45, Convert(ExcludedOpacityConverter.Instance, true));
        Assert.Equal(1.0, Convert(ExcludedOpacityConverter.Instance, false));
    }
}
