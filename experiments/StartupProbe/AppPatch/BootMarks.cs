// 計測用（担当BOOT・脇のビルドだけ。master には入れない）。
// CHMONOS_BOOTMARKS にファイルの道があるときだけ、起動の区切りごとに
// プロセスの開始からの時間・画面のスレッドの CPU・JIT した関数の数と時間・GC の回数を控え、落ち着いた後に書き出す
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BoothAssetManager.App;

internal static class BootMarks
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr h, out long c, out long e, out long k, out long u);

    static readonly string? Path = Environment.GetEnvironmentVariable("CHMONOS_BOOTMARKS");
    static readonly DateTime Start = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    static readonly List<string> Lines = new();
    static int _uiThread = -1;
    static Timer? _timer;

    public static bool On => Path != null;

    public static void Mark(string name)
    {
        if (Path == null) return;
        var ms = (DateTime.UtcNow - Start).TotalMilliseconds;
        double cpu = -1;
        if (Environment.CurrentManagedThreadId == _uiThread || _uiThread < 0)
        {
            GetThreadTimes(GetCurrentThread(), out _, out _, out var k, out var u);
            cpu = (k + u) / 10000.0;
        }
        var line = string.Join('\t', name, ms.ToString("F1"), cpu.ToString("F0"),
            System.Runtime.JitInfo.GetCompiledMethodCount(true), System.Runtime.JitInfo.GetCompilationTime(true).TotalMilliseconds.ToString("F0"),
            System.Runtime.JitInfo.GetCompiledMethodCount(false), System.Runtime.JitInfo.GetCompilationTime(false).TotalMilliseconds.ToString("F0"),
            GC.CollectionCount(0), GC.CollectionCount(2),
            Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds.ToString("F0"));
        lock (Lines) Lines.Add(line);
    }

    public static void BeginUi()
    {
        if (Path == null) return;
        _uiThread = Environment.CurrentManagedThreadId;
        // 裏のスレッドからも時刻ごとに控える（JIT の総数が落ち着く時刻を見るため）。10 秒で書き出す
        var ticks = 0;
        _timer = new Timer(_ =>
        {
            ticks++;
            Mark($"t{ticks * 250}");
            if (ticks == 40) Flush();
        }, null, 250, 250);
    }

    static void Flush()
    {
        _timer?.Dispose();
        lock (Lines)
        {
            File.WriteAllText(Path!, "name\tms\tuiCpu\tjitUiN\tjitUiMs\tjitAllN\tjitAllMs\tgc0\tgc2\tprocCpu\n" + string.Join('\n', Lines) + '\n', new UTF8Encoding(false));
        }
    }
}
