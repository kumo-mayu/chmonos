using System.Windows;
using System.Windows.Threading;

namespace BoothAssetManager.App.Tests.Support;

/// <summary>
/// 試験の「画面のスレッド」。STA のスレッドを1本立てて、試験の中身をそこで走らせる。
///
/// **なぜ1本を使い回すか：**ViewModel は <c>Application.Current.Dispatcher</c> を画面のスレッドとして扱う
/// （裏から届く進み具合をそこへ運ぶ・<c>DispatcherTimer</c> で遅らせて保存する）。<c>Application</c> は1つのプロセスに1つしか作れず、
/// 作ったスレッドに付くので、試験ごとにスレッドを立てると2本目から画面のスレッドが合わなくなる。
/// 1本なので、試験は並べて走らせない（<c>AssemblyInfo.cs</c>）。
///
/// **なぜスレッドプールでそのまま走らせないか：**画面のスレッドが無いと <c>await</c> の続きがばらばらのスレッドへ戻り、
/// アプリでは1本のスレッドでしか触らない一覧を、2本から同時に書き換えることになる（たまにだけ落ちる試験になる）。
///
/// 作る <c>Application</c> は素の物で、アプリ本体の <c>App</c> ではない（<c>App</c> は起動の処理と窓を持つ）。
/// 色の表・型の資源は入っていないので、**画面の部品（View・XAML）はここでは作れない**。
/// </summary>
internal static class UiThread
{
    /// <summary>
    /// 1つの試験が戻るまでの上限。窓が出て答えを待っている・待つ物が来ない、で止まったままになるのを、
    /// 一式ごと固まる代わりにその試験の失敗にする。普通の試験は1秒かからないので、遅いマシンでも届かない長さにしてある
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static readonly Lazy<Dispatcher> Started = new(Start);

    private static readonly object StrayGate = new();
    private static readonly List<Exception> Stray = [];

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var dispatcher = Dispatcher.CurrentDispatcher;

            // 画面のスレッドへ投げた仕事（BeginInvoke・タイマー）の中で落ちると、受ける人がいないのでプロセスごと終わる。
            // 受けて覚え、走っていた試験の失敗にする
            dispatcher.UnhandledException += (_, args) =>
            {
                lock (StrayGate)
                {
                    Stray.Add(args.Exception);
                }

                args.Handled = true;
            };

            ready.SetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "試験の画面のスレッド",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>試験の中身を画面のスレッドで走らせる。<c>await</c> の続きも同じスレッドへ戻る。</summary>
    public static async Task Run(Func<Task> body)
    {
        var dispatcher = Started.Value;
        lock (StrayGate)
        {
            Stray.Clear();
        }

        var running = await dispatcher.InvokeAsync(body);
        if (await Task.WhenAny(running, Task.Delay(Limit)) != running)
        {
            throw new TimeoutException(
                $"試験が {Limit.TotalSeconds:0} 秒たっても戻りません。窓を出す道を通ったか、待っている物が来ていません。");
        }

        await running;

        Exception[] stray;
        lock (StrayGate)
        {
            stray = [.. Stray];
            Stray.Clear();
        }

        if (stray.Length > 0)
        {
            throw new AggregateException("画面のスレッドへ投げた仕事の中で落ちました。", stray);
        }
    }

    /// <summary>待つ物が無い試験（WPF の部品を1つ作って値を読む、など）。</summary>
    public static Task Run(Action body) => Run(() =>
    {
        body();
        return Task.CompletedTask;
    });

    /// <summary>
    /// 投げっぱなしの仕事（読み込み・保存）が済むのを待つ。画面のスレッドを空けながら、条件が成り立つまで回る。
    /// 時間で結果が変わる試験にしないよう、**待つのは「済んだか」だけ**にする（何秒で済むかは確かめない）。
    /// 10秒は「来ない」と決める長さで、普通は数十ミリ秒で抜ける
    /// </summary>
    public static async Task Until(Func<bool> condition, string what)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"待っても成り立ちませんでした：{what}");
            }

            await Task.Delay(10);
        }
    }
}
