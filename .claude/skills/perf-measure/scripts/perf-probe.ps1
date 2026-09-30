# 固まり・メモリ・通信の相手を、外から見張る（perf-kit.ps1 の Start-ChmonosProbe が別のプロセスで走らせる。直には呼ばない）。
#
#   stalls.csv … 窓へ空のメッセージ（WM_NULL）を送り続け、返りが 33ms（2コマ）を超えた回の、時刻と長さ
#   mem.csv    … 作業セットとプライベートを 250ms ごと
#   tcp.csv    … プロセスの TCP の相手を1秒ごとに見て、新しく見えた接続だけ
#
# 出力フォルダに stop が置かれるか、相手のプロセスが閉じたら終わり、done を置く
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$OutDir)
try {
  Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.Globalization; using System.IO; using System.Net; using System.Runtime.InteropServices; using System.Threading;
public static class ChmonosProbe {
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
  [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);
  const string Format = "yyyy-MM-dd HH:mm:ss.fff";
  static string Now() { return DateTime.Now.ToString(Format, CultureInfo.InvariantCulture); }

  public static void Run(int pid, string outDir) {
    var proc = Process.GetProcessById(pid);
    var stopFile = Path.Combine(outDir, "stop");
    using var stalls = new StreamWriter(Path.Combine(outDir, "stalls.csv")) { AutoFlush = true };
    using var mem = new StreamWriter(Path.Combine(outDir, "mem.csv")) { AutoFlush = true };
    using var tcp = new StreamWriter(Path.Combine(outDir, "tcp.csv")) { AutoFlush = true };
    stalls.WriteLine("time,ms"); mem.WriteLine("time,ws_mb,priv_mb"); tcp.WriteLine("time,remote,state");
    var inv = CultureInfo.InvariantCulture;

    var side = new Thread(() => {
      var lastTcp = DateTime.MinValue; var seen = new HashSet<string>();
      while (!File.Exists(stopFile)) {
        try {
          proc.Refresh(); if (proc.HasExited) break;
          mem.WriteLine(Now() + "," + (proc.WorkingSet64 / 1048576.0).ToString("F1", inv) + "," + (proc.PrivateMemorySize64 / 1048576.0).ToString("F1", inv));
        } catch { break; }
        if ((DateTime.Now - lastTcp).TotalMilliseconds >= 1000) {
          lastTcp = DateTime.Now;
          // 同じ接続が続く間は書かない（新しく見えた物だけ）
          foreach (var row in Connections(pid)) if (seen.Add(row)) tcp.WriteLine(Now() + "," + row);
        }
        Thread.Sleep(250);
      }
    }) { IsBackground = true };
    side.Start();
    File.WriteAllText(Path.Combine(outDir, "ready"), "x");

    var sw = new Stopwatch();
    while (!File.Exists(stopFile)) {
      proc.Refresh(); if (proc.HasExited) break;
      var hwnd = proc.MainWindowHandle;
      if (hwnd == IntPtr.Zero) { Thread.Sleep(100); continue; }
      var start = Now(); IntPtr result;
      sw.Restart();
      // 180 秒まで待つ（それより長い固まりは 180 秒として出る）
      SendMessageTimeout(hwnd, 0, IntPtr.Zero, IntPtr.Zero, 0, 180000, out result);
      sw.Stop();
      if (sw.Elapsed.TotalMilliseconds > 33) stalls.WriteLine(start + "," + sw.Elapsed.TotalMilliseconds.ToString("F0", inv));
      Thread.Sleep(10);
    }
    side.Join(2000);
  }

  // プロセスが持つ TCP の接続（IPv4）。相手の番地:口 と 状態の番号
  static List<string> Connections(int pid) {
    var list = new List<string>(); int len = 0;
    GetExtendedTcpTable(IntPtr.Zero, ref len, false, 2, 5, 0);   // 2 = IPv4、5 = プロセスごとの全部の接続
    var buf = Marshal.AllocHGlobal(len);
    try {
      if (GetExtendedTcpTable(buf, ref len, false, 2, 5, 0) != 0) return list;
      var n = Marshal.ReadInt32(buf); var row = buf + 4;
      for (var i = 0; i < n; i++) {
        var state = Marshal.ReadInt32(row);
        var addr = (uint)Marshal.ReadInt32(row + 12);
        var port = (ushort)IPAddress.NetworkToHostOrder(Marshal.ReadInt16(row + 16));
        if (Marshal.ReadInt32(row + 20) == pid && addr != 0) list.Add(new IPAddress(addr) + ":" + port + "," + state);
        row += 24;
      }
    }
    finally { Marshal.FreeHGlobal(buf); }
    return list;
  }
}
'@
  [ChmonosProbe]::Run($ProcessId, $OutDir)
}
catch { Set-Content -LiteralPath (Join-Path $OutDir 'error.txt') -Value $_.Exception.ToString() }
finally { Set-Content -LiteralPath (Join-Path $OutDir 'done') -Value 'x' }
