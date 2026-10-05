using System.Windows;
using System.Windows.Threading;

namespace Chmonos.App.Tests.Support;

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
/// 作る <c>Application</c> はアプリの <c>App</c> で、資源（色の表・標準の部品の型・既定の文字の見た目）だけを読む。
/// 起動の処理と窓は走らない（<c>App.IsLaunchedAsApp</c> が偽）。部品の型を読み込んだ状態を試験できる（名前の「_」が消えないことの試験）。
/// ViewModel の試験は資源を使わないので、色の表を持っていても結果は変わらない。
/// **画面（View）の組み立ては、データの入った ViewModel とその保存先が要るので、ここでは作らず ViewShot で見る**。
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
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
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
    /// 投げっぱなしの仕事（<c>Forget()</c> で投げた読み込み・保存）が全部済むまで待つ。画面のスレッドで呼ぶ。
    ///
    /// 仕事が済んだ後、結果を画面のスレッドへ運ぶ分（<c>BeginInvoke</c>）が列に残っていることがあるので、
    /// 列が空くまで待ってから、その間に新しく投げられていないかをもう一度見る。
    /// **待ってから書く物（0.5秒後に書く検索の条件など）は数に入らない**——それを確かめる試験は、条件を名指しして <see cref="Until"/> で待つ
    /// </summary>
    public static async Task Settle()
    {
        while (true)
        {
            await Until(() => FireAndForget.Pending == 0, "投げっぱなしの仕事が済む");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (FireAndForget.Pending == 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 名指しした条件が成り立つまで待つ。画面のスレッドを空けながら回る。
    /// 時間で結果が変わる試験にしないよう、**待つのは「済んだか」だけ**にする（何秒で済むかは確かめない）。
    /// 10秒は「来ない」と決める長さで、普通は数ミリ秒で抜ける
    /// </summary>
    public static async Task Until(Func<bool> condition, string what)
    {
        var started = Environment.TickCount64;
        var deadline = started + 10_000;
        while (!condition())
        {
            var now = Environment.TickCount64;
            if (now > deadline)
            {
                throw new TimeoutException($"待っても成り立ちませんでした：{what}");
            }

            if (now - started < SpinFor && Dispatcher.FromThread(Thread.CurrentThread) is not null)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            else
            {
                await Task.Delay(10);
            }
        }
    }

    /// <summary>
    /// <see cref="Until"/> が、時計を使わずに画面のスレッドの列を回すだけで待つ長さ（ミリ秒）。
    ///
    /// **<c>Task.Delay(10)</c> は Windows では 10ms ではなく、時計の刻み（約15.6ms）まで延びる。**裏の読み込みは数ミリ秒で済むのに、
    /// 1回待つたびに1刻みを払い、主画面を作るたび・押すたびの <see cref="Settle"/> に積もっていた
    /// （2026-10-05：一式 約43秒のうち 約14秒。試験ごとの最小の合計で 35.7→21.7秒）。他の担当が CPU を使っていると刻みはさらに延び、
    /// 同じ一式が 38秒から2分半まで揺れた。列を回すだけなら、他の仕事が終わった次の周で抜ける。
    /// 待つ物が裏で本当に時間のかかる作業のときに CPU を回し続けないよう、この長さを過ぎたら時計で待つ
    /// </summary>
    private const long SpinFor = 200;
}
