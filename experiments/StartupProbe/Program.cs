// 起動の速さを外から測る見張り。Start-ChmonosApp より先に走らせ、新しく出た Chmonos.App を見つけて、
// 主の窓へ WM_NULL を送り続ける。「1.5 秒続けて 33ms 以内に返る」ようになった時刻（最後の 33ms 超えの返りの終わり）を、
// プロセスの開始から数える。前の担当（docs/history/dotnet10-2026-09-30.md）と同じ決まり。
// 落ち着いた時点のスレッドごとの CPU 時間（名前付き）も控える
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

static class P
{
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetThreadDescription(IntPtr h, out IntPtr desc);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    static string ThreadName(int id)
    {
        var h = OpenThread(0x0800, false, (uint)id); // THREAD_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            if (GetThreadDescription(h, out var p) < 0 || p == IntPtr.Zero) return "";
            var s = Marshal.PtrToStringUni(p) ?? ""; LocalFree(p); return s;
        }
        finally { CloseHandle(h); }
    }

    static int Main(string[] args)
    {
        string outPath = args[0]; string label = args[1];
        var ignore = new HashSet<int>(Process.GetProcessesByName("Chmonos").Select(p => p.Id));
        var wait = Stopwatch.StartNew();
        Process? proc = null;
        while (proc == null)
        {
            if (wait.Elapsed.TotalSeconds > 60) { Console.Error.WriteLine("起動が見えなかった"); return 2; }
            proc = Process.GetProcessesByName("Chmonos").FirstOrDefault(p => !ignore.Contains(p.Id));
            if (proc == null) Thread.Sleep(2);
        }
        var start = proc.StartTime.ToUniversalTime();
        double Ms() => (DateTime.UtcNow - start).TotalMilliseconds;
        double foundAt = Ms();
        IntPtr hwnd = IntPtr.Zero; double windowAt = -1;
        while (hwnd == IntPtr.Zero)
        {
            if (proc.HasExited || Ms() > 60000) { Console.Error.WriteLine("窓が出なかった"); return 3; }
            proc.Refresh(); hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) Thread.Sleep(2);
        }
        windowAt = Ms();
        var uiTid = (int)GetWindowThreadProcessId(hwnd, out _);
        double firstReply = -1, lastLongEnd = -1; int longCount = 0; double longSum = 0, longMax = 0;
        while (true)
        {
            var s = Ms();
            SendMessageTimeout(hwnd, 0, IntPtr.Zero, IntPtr.Zero, 0, 30000, out _);
            var e = Ms(); var d = e - s;
            if (firstReply < 0) firstReply = e;
            if (d > 33) { lastLongEnd = e; longCount++; longSum += d; longMax = Math.Max(longMax, d); }
            var settledAt = lastLongEnd >= 0 ? lastLongEnd : firstReply;
            if (e - settledAt >= 1500) break;
            if (e > 60000) break;
            Thread.Sleep(5);
        }
        var settled = lastLongEnd >= 0 ? lastLongEnd : firstReply;
        proc.Refresh();
        var threads = new List<object>();
        double ui = 0, tcw = 0;
        foreach (ProcessThread t in proc.Threads)
        {
            double cpu; try { cpu = t.TotalProcessorTime.TotalMilliseconds; } catch { continue; }
            var name = ThreadName(t.Id);
            if (t.Id == uiTid) { ui = cpu; name = "(UI) " + name; }
            if (name.Contains("Tiered Compilation")) tcw += cpu;
            if (cpu >= 20) threads.Add(new { id = t.Id, name, cpu = Math.Round(cpu) });
        }
        var rec = new Dictionary<string, object>
        {
            ["label"] = label, ["pid"] = proc.Id, ["time"] = DateTime.Now.ToString("HH:mm:ss"),
            ["found"] = Math.Round(foundAt), ["window"] = Math.Round(windowAt), ["firstReply"] = Math.Round(firstReply),
            ["settled"] = Math.Round(settled), ["longCount"] = longCount, ["longSum"] = Math.Round(longSum), ["longMax"] = Math.Round(longMax),
            ["cpuTotal"] = Math.Round(proc.TotalProcessorTime.TotalMilliseconds), ["cpuUi"] = Math.Round(ui), ["cpuTcw"] = Math.Round(tcw),
            ["wsMB"] = proc.WorkingSet64 / 1048576, ["privMB"] = proc.PrivateMemorySize64 / 1048576,
            ["others"] = Process.GetProcessesByName("Chmonos").Count(p => p.Id != proc.Id),
            ["threads"] = threads,
        };
        File.AppendAllText(outPath, JsonSerializer.Serialize(rec) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"{label}: settled={settled:F0} window={windowAt:F0} first={firstReply:F0} ui={ui:F0} tcw={tcw:F0} total={proc.TotalProcessorTime.TotalMilliseconds:F0}");
        return 0;
    }
}
