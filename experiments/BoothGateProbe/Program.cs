// BoothGateProbe
//
// BOOTH への問い合わせの門を PC で1つにした（BoothMachineGate・絶対に破らない決め事1）。
// 試験は同じプロセスの中で2つのクライアントを組んで確かめているが、本当に守りたいのは別々のプロセスの間なので、
// ここでプロセスを2つ立てて叩き、相手の側で間を測る。
//
// **BOOTH へは1本も出さない。**相手はこのプログラムが手元に立てる作り物のサーバ（127.0.0.1）で、
// 門のファイルも引数で渡した場所に置く（本物の門 %LOCALAPPDATA%\Chmonos.Shared には触らない）。
//
// 使い方:
//   dotnet run --project experiments/BoothGateProbe -c Release -- run   <作業フォルダ>   2つのプロセスで叩いて間を測る（約3分）
//   dotnet run --project experiments/BoothGateProbe -c Release -- bench <作業フォルダ>   1本のときの、1回の問い合わせあたりの上乗せを測る
//
// 測る物（run）:
//   1. 2本が続けて問い合わせる        → 間の最小（1.5秒以上か）・同時に出た数（0か）・順番（交互か）
//   2. 裏の作業を続ける1本＋時々押す1本 → 押した側が待たされた長さ
//   3. 片方が送ったまま落ちる          → もう片方が止まらずに続くか・落ちた直後の間
//   4. 間隔の設定が違う2本            → 間の最小（長い方に合っているか）・短い方だけが出続けないか

using System.Diagnostics;
using System.Globalization;
using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("使い方: BoothGateProbe run|bench <作業フォルダ>");
    return 1;
}

switch (args[0])
{
    case "run":
        return await Runner.RunAsync(args[1]);
    case "bench":
        return await Bench.RunAsync(args[1]);
    case "client":
        return await ClientProcess.RunAsync(args);
    default:
        Console.WriteLine($"知らない指定: {args[0]}");
        return 1;
}

/// <summary>作り物のサーバ。届いた刻みと返し終えた刻みを控える。同時に何本来ても受ける（重なりを見たいので）。</summary>
internal sealed class FakeServer : IDisposable
{
    /// <summary>1本の応答にかける時間。本物の実測（0.16〜0.3秒）の間に置く。</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(200);

    private readonly HttpListener _listener = new();
    private readonly List<Hit> _hits = [];
    private int _inFlight;

    public sealed record Hit(string From, long Arrived, long Ended);

    public string Url { get; }

    public int MaxInFlight { get; private set; }

    public FakeServer()
    {
        // 空いている番号を借りる
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var arrived = Stopwatch.GetTimestamp();
        var now = Interlocked.Increment(ref _inFlight);
        lock (_hits)
        {
            MaxInFlight = Math.Max(MaxInFlight, now);
        }

        // 誰からかは、応答を閉じる前に読む（閉じた後は読めない）
        var from = context.Request.QueryString["from"] ?? "?";

        await Task.Delay(Hold);

        // 「応答が終わった」は、最後のバイトを送り出した時点で測る。相手が受け取るのはこの後なので、
        // 相手が「終わった」と見る刻みより遅くはならない。後片付け（Close）が戻るのを待つと、相手が受け取った後まで
        // ずれ込むことがあり、間を実際より短く測ってしまう
        long ended;
        try
        {
            var body = "ok"u8.ToArray();
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            await context.Response.OutputStream.FlushAsync();
            ended = Stopwatch.GetTimestamp();
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException
                                              or InvalidOperationException)
        {
            // 相手が落ちていた（3 の確かめ）。届いたことは数える
            ended = Stopwatch.GetTimestamp();
        }

        Interlocked.Decrement(ref _inFlight);
        lock (_hits)
        {
            _hits.Add(new Hit(from, arrived, ended));
        }
    }

    public IReadOnlyList<Hit> TakeHits()
    {
        lock (_hits)
        {
            var taken = _hits.OrderBy(hit => hit.Arrived).ToList();
            _hits.Clear();
            MaxInFlight = 0;
            return taken;
        }
    }

    public void Dispose() => _listener.Close();
}

/// <summary>別のプロセスとして動く側。本物と同じ組み立て（待ちは本物）で、門だけ渡された場所を使う。</summary>
internal static class ClientProcess
{
    // client <名前> <URL> <門のファイル> <本数> <間隔ms> <始める前の待ちms> <1本ごとの休みms> <この本数目で落ちる(0=落ちない)>
    public static async Task<int> RunAsync(string[] args)
    {
        var name = args[1];
        var url = args[2];
        var gate = new BoothMachineGate(args[3]);
        var count = int.Parse(args[4], CultureInfo.InvariantCulture);
        var interval = int.Parse(args[5], CultureInfo.InvariantCulture);
        var startAfter = int.Parse(args[6], CultureInfo.InvariantCulture);
        var pause = int.Parse(args[7], CultureInfo.InvariantCulture);
        var crashAt = int.Parse(args[8], CultureInfo.InvariantCulture);

        using var http = new HttpClient(new CrashingHandler(crashAt)) { Timeout = TimeSpan.FromSeconds(30) };

        // 本物と同じ待ち（Task.Delay）と時計で、門だけ自分の置き場の物を渡す（外に出していない口。この確かめにだけ見せてある）
        var client = new BoothClient(
            http,
            SettingsSource.Fixed(new AppSettings { FetchIntervalMs = interval }),
            (duration, token) => Task.Delay(duration, token),
            gate,
            TimeProvider.System);

        await Task.Delay(startAfter);

        for (var index = 1; index <= count; index++)
        {
            var watch = Stopwatch.StartNew();
            var result = await client.GetBinaryAsync($"{url}?from={name}&n={index}");
            Console.WriteLine($"lat {name} {index} {watch.ElapsedMilliseconds} {(result.IsSuccess ? "ok" : "ng")}");

            if (pause > 0)
            {
                await Task.Delay(pause);
            }
        }

        return 0;
    }

    /// <summary>決まった本数目で、送った直後に自分を落とす（応答を待たずに消える＝送ったまま落ちたアプリ）。</summary>
    private sealed class CrashingHandler(int crashAt) : DelegatingHandler(new HttpClientHandler())
    {
        private int _count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++_count != crashAt)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            var sending = base.SendAsync(request, cancellationToken);

            // 相手に届くのを待ってから落ちる（応答は 200ms 後なので、まだ返っていない）
            await Task.Delay(80, cancellationToken);
            Process.GetCurrentProcess().Kill();
            return await sending;
        }
    }
}

internal static class Runner
{
    private sealed record ClientPlan(string Name, int Count, int IntervalMs, int StartAfterMs = 0, int PauseMs = 0, int CrashAt = 0);

    public static async Task<int> RunAsync(string workFolder)
    {
        Directory.CreateDirectory(workFolder);
        using var server = new FakeServer();
        Console.WriteLine($"作り物のサーバ: {server.Url}（BOOTH へは出さない）");

        var failed = 0;

        failed += await ScenarioAsync(server, workFolder, "1. 2本が続けて問い合わせる", 1500,
            new ClientPlan("A", 12, 1500), new ClientPlan("B", 12, 1500, StartAfterMs: 700));

        failed += await ScenarioAsync(server, workFolder, "2. 裏の作業を続ける1本（A）＋時々押す1本（B）", 1500,
            new ClientPlan("A", 16, 1500), new ClientPlan("B", 4, 1500, StartAfterMs: 4000, PauseMs: 4300));

        failed += await ScenarioAsync(server, workFolder, "3. 片方（A）が3本目を送ったまま落ちる", 1500,
            new ClientPlan("A", 10, 1500, CrashAt: 3), new ClientPlan("B", 8, 1500, StartAfterMs: 700));

        failed += await ScenarioAsync(server, workFolder, "4. 間隔の設定が違う2本（A=1.5秒・B=4秒）", 4000,
            new ClientPlan("A", 6, 1500), new ClientPlan("B", 6, 4000, StartAfterMs: 700));

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "全部守れている" : $"守れていない確かめ: {failed}");
        return failed == 0 ? 0 : 2;
    }

    private static async Task<int> ScenarioAsync(
        FakeServer server,
        string workFolder,
        string title,
        int requiredGapMs,
        params ClientPlan[] plans)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title}");

        // 確かめごとに新しい門（前の確かめの中身を持ち越さない）
        var gateFile = Path.Combine(workFolder, $"gate-{Guid.NewGuid():N}.txt");
        var self = Environment.ProcessPath!;

        var runs = plans.Select(plan =>
        {
            var start = new ProcessStartInfo(self)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var argument in new[]
                     {
                         "client", plan.Name, server.Url, gateFile,
                         plan.Count.ToString(CultureInfo.InvariantCulture),
                         plan.IntervalMs.ToString(CultureInfo.InvariantCulture),
                         plan.StartAfterMs.ToString(CultureInfo.InvariantCulture),
                         plan.PauseMs.ToString(CultureInfo.InvariantCulture),
                         plan.CrashAt.ToString(CultureInfo.InvariantCulture),
                     })
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start)!;
            return (plan, process, output: process.StandardOutput.ReadToEndAsync());
        }).ToList();

        var latencies = new Dictionary<string, List<long>>();
        foreach (var (plan, process, output) in runs)
        {
            var text = await output;
            await process.WaitForExitAsync();
            latencies[plan.Name] = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split(' '))
                .Where(parts => parts.Length >= 4 && parts[0] == "lat")
                .Select(parts => long.Parse(parts[3], CultureInfo.InvariantCulture))
                .ToList();
            Console.WriteLine($"   {plan.Name}: 終了コード {process.ExitCode}・終わった問い合わせ {latencies[plan.Name].Count}/{plan.Count}");
        }

        // 応答を返し終えるのを待つ（落ちた相手の分）
        await Task.Delay(400);
        var maxInFlight = server.MaxInFlight;
        var hits = server.TakeHits();

        var gaps = new List<double>();
        for (var index = 1; index < hits.Count; index++)
        {
            gaps.Add(Stopwatch.GetElapsedTime(hits[index - 1].Ended, hits[index].Arrived).TotalMilliseconds);
        }

        var order = string.Concat(hits.Select(hit => hit.From));
        Console.WriteLine($"   届いた本数: {hits.Count}　順番: {order}");
        Console.WriteLine($"   同時に届いた最大: {maxInFlight} 本");
        if (gaps.Count > 0)
        {
            var sorted = gaps.Order().ToList();
            Console.WriteLine(
                $"   前の応答が終わってから次が届くまで: 最小 {sorted[0]:0} ms・中央 {sorted[sorted.Count / 2]:0} ms・最大 {sorted[^1]:0} ms");
        }

        foreach (var (name, list) in latencies.Where(pair => pair.Value.Count > 0))
        {
            Console.WriteLine($"   {name} の1本の長さ（呼んでから返るまで）ms: {string.Join(" ", list)}");
        }

        // 届いた本数も見る。1本も数えられていないのに「守れている」と言わない（落ちた1本は、返らなかったが届いている）
        var expected = latencies.Values.Sum(list => list.Count) + plans.Count(plan => plan.CrashAt > 0);
        if (hits.Count != expected)
        {
            Console.WriteLine($"   → **数が合わない**（届いた {hits.Count} 本・終わった問い合わせ＋落ちた分 {expected} 本）。測り直す");
            return 1;
        }

        var ok = maxInFlight <= 1 && gaps.All(gap => gap >= requiredGapMs);
        Console.WriteLine(ok
            ? $"   → 守れている（1本ずつ・{requiredGapMs} ms 以上）"
            : $"   → **守れていない**（同時 {maxInFlight} 本・間の最小 {(gaps.Count > 0 ? gaps.Min() : 0):0} ms）");
        return ok ? 0 : 1;
    }
}

/// <summary>1本しか動いていないときの、1回の問い合わせあたりの上乗せ。</summary>
internal static class Bench
{
    private sealed class InstantHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
    }

    public static async Task<int> RunAsync(string workFolder)
    {
        Directory.CreateDirectory(workFolder);
        const int Requests = 3000;
        Func<TimeSpan, CancellationToken, Task> noWait = (_, _) => Task.CompletedTask;

        // 相手は即答する作り物で、待ちも飛ばす。残るのは BoothClient の中の手間だけなので、門の有り無しの差が上乗せになる。
        // 門ありは「続けて問い合わせる」形：間隔を待つ前に1回握って読み、待った後に握り直して、送る前と終わった後に書く
        async Task<double> MeasureAsync(BoothMachineGate? gate)
        {
            using var http = new HttpClient(new InstantHandler());
            var client = new BoothClient(http, SettingsSource.Fixed(new AppSettings()), noWait, gate, TimeProvider.System);
            for (var warm = 0; warm < 200; warm++)
            {
                await client.GetBinaryAsync("http://127.0.0.1/x");
            }

            var watch = Stopwatch.StartNew();
            for (var index = 0; index < Requests; index++)
            {
                await client.GetBinaryAsync("http://127.0.0.1/x");
            }

            return watch.Elapsed.TotalMilliseconds * 1000 / Requests;
        }

        Console.WriteLine($"1回の問い合わせあたり（{Requests} 回の平均・マイクロ秒）。3回ずつ測る");
        for (var round = 1; round <= 3; round++)
        {
            var without = await MeasureAsync(null);
            var with = await MeasureAsync(new BoothMachineGate(Path.Combine(workFolder, $"bench-{Guid.NewGuid():N}.txt")));
            Console.WriteLine($"  {round} 回目: 門なし {without:0.0} µs・門あり {with:0.0} µs・上乗せ {with - without:0.0} µs");
        }

        Console.WriteLine("間隔は 1,500,000 µs。上乗せがその何%かを見る");
        return 0;
    }
}
