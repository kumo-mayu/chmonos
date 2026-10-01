using System.Net;
using System.Text;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// BOOTH への問い合わせの、PC で1つの門（絶対に破らない決め事1。ユーザ判断 2026-09-30）。
///
/// 保存先の違うアプリを2本開くと、それぞれが1.5秒ごとに問い合わせて、合わせると決め事を破っていた。
/// ここでは同じ門（同じファイル）を見る <see cref="BoothClient"/> を2つ組み、合わせて守ることを確かめる。
/// 門は試験ごとの一時フォルダに置く（本物の門には触らない）。時計は手で進める物で、実際には待たない。
/// 別のプロセスから叩く確かめは <c>experiments/BoothGateProbe</c>。
/// </summary>
public sealed class BoothMachineGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-gate-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ManualClock _clock = new();

    private string GateFile => Path.Combine(_dir, "booth-gate.txt");

    public BoothMachineGateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // 門はファイルを開いたままにする。閉じてからでないと一時フォルダが消せない
        foreach (var gate in _gates)
        {
            gate.Dispose();
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private readonly List<BoothMachineGate> _gates = [];

    /// <summary>
    /// 門を1つ開く。呼ぶたびに別の物（別のプロセスと同じく、同じファイルを別々に開く）。
    /// </summary>
    private BoothMachineGate Gate(string? file = null, Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        var gate = new BoothMachineGate(file ?? GateFile, retryDelay);
        lock (_gates)
        {
            _gates.Add(gate);
        }

        return gate;
    }

    /// <summary>手で進める時計。刻み（間を測る方）と壁の時計（日付を読む方）を別々に動かせる。</summary>
    private sealed class ManualClock : TimeProvider
    {
        // 起動から100秒。0 から始めると「まだ問い合わせていない」と見分けがつかない値が混ざる
        private long _ticks = 100 * TimeSpan.TicksPerSecond;

        public DateTimeOffset Wall { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public override DateTimeOffset GetUtcNow() => Wall;

        public void Advance(TimeSpan span) => Interlocked.Add(ref _ticks, span.Ticks);

        public long After(TimeSpan span) => GetTimestamp() + span.Ticks;
    }

    /// <summary>作り物の BOOTH。問い合わせの始まりと終わりの刻みを控える。1本に 0.3 秒かかる。</summary>
    private sealed class RecordingHandler(ManualClock clock, List<(long Start, long End)> log) : HttpMessageHandler
    {
        /// <summary>立てておくと、放すまで応答を返さない（問い合わせの最中を作る）。</summary>
        public TaskCompletionSource? Hold { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<HttpResponseMessage> Respond { get; set; }
            = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };

        public int Count { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            var start = clock.GetTimestamp();
            Entered.TrySetResult();
            if (Hold is { } hold)
            {
                await hold.Task;
            }

            clock.Advance(TimeSpan.FromMilliseconds(300));
            lock (log)
            {
                log.Add((start, clock.GetTimestamp()));
            }

            return Respond();
        }
    }

    private readonly List<(long Start, long End)> _log = [];

    private RecordingHandler Handler() => new(_clock, _log);

    /// <summary>
    /// 門を共有するクライアント。待ちは時計を進めるだけで、言われた長さを <paramref name="waits"/> に控える。
    /// 門は1つずつ別の物を組む（別のプロセスと同じく、同じファイルを別々に開く）。
    /// </summary>
    private BoothClient Client(
        HttpMessageHandler handler,
        List<TimeSpan>? waits = null,
        int intervalMs = 1500,
        BoothMachineGate? gate = null)
        => new(
            new HttpClient(handler),
            SettingsSource.Fixed(new AppSettings { FetchIntervalMs = intervalMs }),
            (duration, _) =>
            {
                waits?.Add(duration);
                _clock.Advance(duration);
                return Task.CompletedTask;
            },
            gate ?? Gate(),
            _clock);

    private static TimeSpan Sum(IEnumerable<TimeSpan> waits)
        => waits.Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait);

    private static void AssertWaited(TimeSpan expected, IEnumerable<TimeSpan> waits)
    {
        var actual = Sum(waits);
        Assert.True((actual - expected).Duration() < TimeSpan.FromMilliseconds(1), $"待った長さ：{actual}（期待：{expected}）");
    }

    /// <summary>門の中身を、別のアプリが書いたように置く。</summary>
    private void Put(BoothGateState state)
    {
        using var turn = Gate().EnterAsync().GetAwaiter().GetResult();
        turn!.Write(state);
    }

    private BoothGateState Peek()
    {
        using var turn = Gate().EnterAsync().GetAwaiter().GetResult();
        return turn!.State;
    }

    [Fact]
    public async Task 別のクライアントが問い合わせた直後は間隔を空けてから出る()
    {
        var waitsB = new List<TimeSpan>();
        var first = Client(Handler());
        var second = Client(Handler(), waitsB);

        await first.GetItemJsonAsync("1");

        // 2つ目のクライアントはまだ1本も問い合わせていない。門が別々なら待たずに出る
        await second.GetItemJsonAsync("2");

        AssertWaited(TimeSpan.FromMilliseconds(1500), waitsB);
        Assert.True(_log[1].Start - _log[0].End >= TimeSpan.FromMilliseconds(1500).Ticks);
    }

    [Fact]
    public async Task 二つのクライアントが交互に問い合わせても合わせて間隔を守る()
    {
        var first = Client(Handler());
        var second = Client(Handler());

        for (var round = 0; round < 4; round++)
        {
            await first.GetItemJsonAsync("1");
            await second.GetItemJsonAsync("2");
            await second.GetItemJsonAsync("3");
        }

        Assert.Equal(12, _log.Count);
        for (var index = 1; index < _log.Count; index++)
        {
            var gap = TimeSpan.FromTicks(_log[index].Start - _log[index - 1].End);
            Assert.True(gap >= TimeSpan.FromMilliseconds(1500), $"{index} 本目の前の間：{gap}");
        }
    }

    /// <summary>
    /// 1つ目の問い合わせが終わるまで、2つ目は出ない。握り直した回数で「試して断られた」を確かめてから、1つ目を終わらせる
    /// （実際の待ちに頼らない）。
    /// </summary>
    [Fact]
    public async Task 別のクライアントが問い合わせている最中は出ない()
    {
        var holding = Handler();
        holding.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Client(holding);

        var retries = 0;
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = Handler();
        var second = Client(waiting, gate: Gate(retryDelay: async (_, _) =>
        {
            if (Interlocked.Increment(ref retries) >= 3)
            {
                refused.TrySetResult();
            }

            await Task.Yield();
        }));

        var firstRequest = first.GetItemJsonAsync("1");
        await holding.Entered.Task;

        var secondRequest = Task.Run(() => second.GetItemJsonAsync("2"));
        await refused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, waiting.Count);

        holding.Hold.SetResult();
        await firstRequest;
        await secondRequest.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, waiting.Count);
        Assert.True(_log[1].Start - _log[0].End >= TimeSpan.FromMilliseconds(1500).Ticks);
    }

    [Fact]
    public async Task 間隔の設定が違えば長い方に合わせる()
    {
        var waitsShort = new List<TimeSpan>();
        var waitsLong = new List<TimeSpan>();
        var shortOne = Client(Handler(), waitsShort, intervalMs: 1500);
        var longOne = Client(Handler(), waitsLong, intervalMs: 5000);

        await shortOne.GetItemJsonAsync("1");

        // 間隔の長い方は、相手の直後でも自分の間隔を空ける
        await longOne.GetItemJsonAsync("2");
        AssertWaited(TimeSpan.FromMilliseconds(5000), waitsLong);

        // 間隔の短い方も、長い方が問い合わせた直後は長い方の間隔を空ける
        await shortOne.GetItemJsonAsync("3");
        AssertWaited(TimeSpan.FromMilliseconds(5000), waitsShort);

        // 自分の直後は、自分の間隔に戻る
        waitsShort.Clear();
        await shortOne.GetItemJsonAsync("4");
        AssertWaited(TimeSpan.FromMilliseconds(1500), waitsShort);
    }

    /// <summary>
    /// 裏の作業を続けているアプリが握り続けない：別のアプリが順番を待っていたら、間隔に譲る間を足す。
    /// 待っている側の間隔が長ければ、そこまで待つ（短い方が出続けると、長い方が永久に出られない）。
    /// </summary>
    [Theory]
    [InlineData(1500, 1750)]
    [InlineData(5000, 5250)]
    public async Task 別のクライアントが順番を待っていたら譲る間を足す(int waiterIntervalMs, int expectedMs)
    {
        var waits = new List<TimeSpan>();
        var busy = Client(Handler(), waits);

        await busy.GetItemJsonAsync("1");

        // 別のアプリが順番待ちに並んだ所
        Put(Peek() with { Waiter = "another", WaiterIntervalMs = waiterIntervalMs });

        await busy.GetItemJsonAsync("2");

        AssertWaited(TimeSpan.FromMilliseconds(expectedMs), waits);

        // 出たら、待っていた人の印は消える（もう居ない相手に譲り続けない）
        Assert.Null(Peek().Waiter);
    }

    [Fact]
    public async Task 待つときは順番待ちに並び自分の間隔を書く()
    {
        var first = Client(Handler());
        await first.GetItemJsonAsync("1");

        // 待ちの途中で止めて、門の中身を見る
        using var stop = new CancellationTokenSource();
        var second = new BoothClient(
            new HttpClient(Handler()),
            SettingsSource.Fixed(new AppSettings { FetchIntervalMs = 4000 }),
            (_, _) =>
            {
                stop.Cancel();
                throw new OperationCanceledException(stop.Token);
            },
            Gate(),
            _clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.GetItemJsonAsync("2", stop.Token));

        var state = Peek();
        Assert.NotNull(state.Waiter);
        Assert.NotEqual(state.Sender, state.Waiter);
        Assert.Equal(4000, state.WaiterIntervalMs);
    }

    [Fact]
    public async Task 一つしか動いていなければ譲る間を足さない()
    {
        var waits = new List<TimeSpan>();
        var only = Client(Handler(), waits);

        await only.GetItemJsonAsync("1");
        await only.GetItemJsonAsync("2");

        AssertWaited(TimeSpan.FromMilliseconds(1500), waits);
        Assert.Null(Peek().Waiter);
    }

    /// <summary>BOOTH に待てと言われたら、別のアプリも待つ（同じ相手なので）。</summary>
    [Fact]
    public async Task 待てと言われたら別のクライアントも待つ()
    {
        var told = Handler();
        told.Respond = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "1800");
            return response;
        };
        var first = Client(told);
        var waits = new List<TimeSpan>();
        var second = Client(Handler(), waits);

        // 指示が上限（60秒）より長いので、その1本は諦める。ほかの問い合わせは上限の分だけ待つ
        var refused = await first.GetItemJsonAsync("1");
        Assert.True(refused.IsRateLimited);

        await second.GetItemJsonAsync("2");

        AssertWaited(TimeSpan.FromSeconds(60), waits);
    }

    /// <summary>
    /// 送ったまま落ちたアプリが居ても、止まったままにならない。錠は OS が放し、「送っている最中」の印だけが残る。
    /// 落ちた1本は相手の側でまだ続いているかもしれないので、終わるまでの分（2秒）を見てから間隔を空けて出る
    /// （「今終わった」とみなして間隔だけ空けると、2つのプロセスで測ったとき、相手から見た間が 1424 ms になった）。
    /// </summary>
    [Fact]
    public async Task 送ったまま落ちたアプリの後は終わるまでの分を見てから間隔を空けて出る()
    {
        Put(new BoothGateState { EndedAt = _clock.GetTimestamp(), InFlight = true, IntervalMs = 1500, Sender = "crashed" });
        _clock.Advance(TimeSpan.FromMinutes(10));

        var waits = new List<TimeSpan>();
        var client = Client(Handler(), waits);
        var result = await client.GetItemJsonAsync("1");

        Assert.True(result.IsSuccess);
        AssertWaited(TimeSpan.FromMilliseconds(3500), waits);
        Assert.False(Peek().InFlight);

        // 1回だけ。次からは普段の間隔
        waits.Clear();
        await client.GetItemJsonAsync("2");
        AssertWaited(TimeSpan.FromMilliseconds(1500), waits);
    }

    /// <summary>
    /// 刻みは Windows を起動してからの経過なので、起動し直すと前の値の方が大きいことがある。
    /// 信じると前の起動の長さだけ待つので、今より先の値は無いものとして扱う。
    /// </summary>
    [Fact]
    public async Task 前に起動していたときの残りでは待たない()
    {
        Put(new BoothGateState
        {
            EndedAt = _clock.After(TimeSpan.FromHours(5)),
            IntervalMs = 30000,
            QuietUntil = _clock.After(TimeSpan.FromHours(6)),
            Sender = "before-reboot",
        });

        var waits = new List<TimeSpan>();
        var result = await Client(Handler(), waits).GetItemJsonAsync("1");

        Assert.True(result.IsSuccess);
        Assert.Empty(waits);
        Assert.Null(Peek().QuietUntil);
    }

    /// <summary>読めない中身は「送ったまま落ちた」と同じに扱う（間を空けずに出る側には倒さない）。長くは止まらない。</summary>
    [Fact]
    public async Task 壊れた中身でも落ちたときと同じだけ待って出る()
    {
        File.WriteAllBytes(GateFile, Encoding.UTF8.GetBytes("こわれた中身\n"));

        var waits = new List<TimeSpan>();
        var result = await Client(Handler(), waits).GetItemJsonAsync("1");

        Assert.True(result.IsSuccess);
        AssertWaited(TimeSpan.FromMilliseconds(3500), waits);
    }

    /// <summary>ほかのアプリが書いた値がどれだけ大きくても、自分の設定の上限（間隔 30 秒・指示の待ち 60 秒）までしか待たない。</summary>
    [Fact]
    public async Task ほかのアプリの値が大きすぎても自分の上限までしか待たない()
    {
        Put(new BoothGateState
        {
            EndedAt = _clock.GetTimestamp(),
            IntervalMs = int.MaxValue,
            QuietUntil = _clock.After(TimeSpan.FromDays(3)),
            Sender = "another",
        });

        var waits = new List<TimeSpan>();
        var result = await Client(Handler(), waits).GetItemJsonAsync("1");

        Assert.True(result.IsSuccess);
        AssertWaited(TimeSpan.FromSeconds(60), waits);
    }

    /// <summary>間は起動してからの刻みで測る。壁の時計を合わせ直しても、待つ長さは変わらない。</summary>
    [Theory]
    [InlineData(-24)]
    [InlineData(24)]
    public async Task 時計が戻っても進んでも待つ長さは変わらない(int hours)
    {
        var waits = new List<TimeSpan>();
        var first = Client(Handler());
        var second = Client(Handler(), waits);

        await first.GetItemJsonAsync("1");
        _clock.Wall += TimeSpan.FromHours(hours);
        await second.GetItemJsonAsync("2");

        AssertWaited(TimeSpan.FromMilliseconds(1500), waits);
    }

    /// <summary>門が開けない場所でも、問い合わせそのものは止めない（アプリ1本の中の決め事は今までどおり守る）。</summary>
    [Fact]
    public async Task 門が開けなくても問い合わせは出て自分の間隔は守る()
    {
        // ファイルの名前の所にフォルダを置く（開けない）
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(blocked);

        var waits = new List<TimeSpan>();
        var client = Client(Handler(), waits, gate: Gate(blocked));

        Assert.True((await client.GetItemJsonAsync("1")).IsSuccess);
        Assert.True((await client.GetItemJsonAsync("2")).IsSuccess);

        AssertWaited(TimeSpan.FromMilliseconds(1500), waits);
    }

    /// <summary>
    /// 待ちを差し替えた組み立て（試験）は、本物の門に入らない。入ると、待たずに本物の門の中身を書き換えて、
    /// 隣で動いている本物のアプリを待たせる。何も言わない組み立て（アプリ・評価台・道具）は必ず入る。
    /// </summary>
    [Fact]
    public void 待ちを差し替えなければ本物の門に入り差し替えた組み立ては入らない()
    {
        using var http = new HttpClient(Handler());

        Assert.Null(new BoothClient(http, new AppSettings(), TestWait.None).MachineGate);
        Assert.Null(new BoothClient(http, () => new AppSettings(), TestWait.None).MachineGate);
        Assert.Same(BoothMachineGate.ForThisUser, new BoothClient(http, new AppSettings()).MachineGate);
        Assert.Same(BoothMachineGate.ForThisUser, new BoothClient(http, () => new AppSettings()).MachineGate);
    }

    /// <summary>
    /// 門を選んで組む口は、外に出していない。出すと、本物の BOOTH を相手に別の門を渡す（門を避ける）作り方ができる。
    /// 外から見える組み立ては、待ちを差し替えるかどうかしか選べない。
    /// </summary>
    [Fact]
    public void 門を選んで組む口は外に出していない()
    {
        var publicOnes = typeof(BoothClient).GetConstructors();

        Assert.NotEmpty(publicOnes);
        Assert.DoesNotContain(
            publicOnes.SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(BoothMachineGate) || parameter.ParameterType == typeof(TimeProvider));
    }

    /// <summary>門は保存先の中に置かない（保存先の違うアプリ同士で共有する。引越しで運ぶ・消す場所にも混ぜない）。</summary>
    [Fact]
    public void 本物の門は既定の保存先の外にある()
    {
        var store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StoreLocation.DefaultRoot)) + Path.DirectorySeparatorChar;

        Assert.False(Path.GetFullPath(BoothMachineGate.DefaultFile).StartsWith(store, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 門の中身は書いた物がそのまま読める()
    {
        var state = new BoothGateState
        {
            EndedAt = 123456789012,
            InFlight = true,
            IntervalMs = 3000,
            QuietUntil = 223456789012,
            Sender = "a1b2c3",
            Waiter = "d4e5f6",
            WaiterIntervalMs = 1500,
        };

        Assert.Equal(state, BoothGateState.Parse(state.ToText()));
        Assert.Equal(BoothGateState.Empty, BoothGateState.Parse(BoothGateState.Empty.ToText()));
        Assert.Equal(BoothGateState.Empty, BoothGateState.Parse(string.Empty));
    }

    /// <summary>書いている途中で落ちた中身（最後の行が無い）は、読めなかった物として扱う。</summary>
    [Fact]
    public void 書きかけの中身は読めなかった物として扱う()
    {
        var text = new BoothGateState { EndedAt = 100, IntervalMs = 1500, Sender = "a" }.ToText();

        Assert.Equal(BoothGateState.Unknown, BoothGateState.Parse(text[..^4]));
        Assert.Equal(BoothGateState.Unknown, BoothGateState.Parse("endedAt=abc\nend\n"));
    }

    /// <summary>前の中身の方が長くても、後ろに古い行が残らない。</summary>
    [Fact]
    public void 短い中身で書き直すと古い行が残らない()
    {
        Put(new BoothGateState { EndedAt = 100, IntervalMs = 1500, Sender = "long-long-long", Waiter = "w", WaiterIntervalMs = 1500 });
        Put(new BoothGateState { EndedAt = 200 });

        Assert.Equal(new BoothGateState { EndedAt = 200 }, Peek());

        // 門が開いたままなので、読み書きを分け合う形で開く
        using var stream = new FileStream(GateFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal(new BoothGateState { EndedAt = 200 }.ToText(), reader.ReadToEnd());
    }

    /// <summary>
    /// 1つのプロセスの中で同じ門を分け合うクライアント（アプリの中の本物の門は1つ）も、1本ずつ・間隔を空けて出る。
    /// ファイルの錠は別々に開いた物どうしの間の物なので、同じ物を分け合う人は門の中で順番を付けている。
    /// </summary>
    [Fact]
    public async Task 同じ門を分け合うクライアントも一本ずつ出る()
    {
        var shared = Gate();
        var holding = Handler();
        holding.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Client(holding, gate: shared);
        var second = Client(Handler(), gate: shared);

        var firstRequest = first.GetItemJsonAsync("1");
        await holding.Entered.Task;
        var secondRequest = Task.Run(() => second.GetItemJsonAsync("2"));

        holding.Hold.SetResult();
        await firstRequest;
        await secondRequest.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, _log.Count);
        Assert.True(_log[1].Start - _log[0].End >= TimeSpan.FromMilliseconds(1500).Ticks);
    }

    /// <summary>落ちたアプリの錠は OS が外す。ここでは門を閉じて同じことを起こし、次の人が握れることを確かめる。</summary>
    [Fact]
    public async Task 握ったまま閉じられた門は次の人が握れる()
    {
        var crashed = Gate();
        var held = await crashed.EnterAsync();
        Assert.NotNull(held);

        // 放さずにファイルごと閉じる（プロセスが落ちると OS がこうする）
        crashed.Dispose();

        using var next = await Gate().EnterAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(next);
    }
}
