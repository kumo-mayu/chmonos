// 計測用（担当BOOT・脇のビルドだけ）。CHMONOS_BOOTJIT にファイルの道があるとき、ランタイムの JIT の知らせを
// プロセスの中で受け、関数ごとに「どのスレッドで・何を・何 ms かけて・どの段で」翻訳したかを 10 秒で書き出す。
// 道具を入れずに（dotnet-trace なしで）JIT の中身を見るため
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;

namespace BoothAssetManager.App;

internal sealed class BootJit : EventListener
{
    static readonly string? Path = Environment.GetEnvironmentVariable("CHMONOS_BOOTJIT");
    static BootJit? _instance;
    readonly ConcurrentDictionary<(long, ulong), DateTime> _started = new();
    readonly ConcurrentQueue<string> _rows = new();
    DateTime _t0 = DateTime.UtcNow;

    public static void Begin()
    {
        if (Path == null || _instance != null) return;
        _instance = new BootJit();
        var t = new Timer(_ => _instance.Flush(), null, 10000, Timeout.Infinite);
        GC.KeepAlive(t);
        _timer = t;
    }
    static Timer? _timer;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
        {
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x10); // JIT
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        try
        {
            if (e.PayloadNames == null) return;
            object? P(string n) { var i = e.PayloadNames.IndexOf(n); return i >= 0 ? e.Payload![i] : null; }
            if (e.EventName == "MethodJittingStarted_V1")
            {
                _started[(e.OSThreadId, Convert.ToUInt64(P("MethodID")))] = e.TimeStamp;
            }
            else if (e.EventName != null && e.EventName.StartsWith("MethodLoadVerbose"))
            {
                var key = (e.OSThreadId, Convert.ToUInt64(P("MethodID")));
                if (!_started.TryRemove(key, out var s)) return;
                var ms = (e.TimeStamp - s).TotalMilliseconds;
                var flags = Convert.ToUInt32(P("MethodFlags") ?? 0u);
                // MethodFlags の上位ビットに翻訳の段が入る（7 ビット目から 3 ビット：1=最適化なし 2=Tier0 3=Tier1 4=OSR…）
                var tier = (flags >> 7) & 0x7;
                _rows.Enqueue(string.Join('\t', e.OSThreadId, (s - _t0).TotalMilliseconds.ToString("F1"), ms.ToString("F3"), tier, flags.ToString("x"),
                    P("MethodNamespace"), P("MethodName"), P("MethodSize")));
            }
        }
        catch { }
    }

    void Flush()
    {
        var sb = new StringBuilder("thread\tat\tms\ttier\tflags\tns\tname\tcodeSize\n");
        foreach (var r in _rows) sb.Append(r).Append('\n');
        File.WriteAllText(Path!, sb.ToString(), new UTF8Encoding(false));
    }
}

