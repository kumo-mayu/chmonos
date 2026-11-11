using System.Runtime.InteropServices;
using System.Text;

namespace Chmonos.Api;

/// <summary>
/// 接続を開いたプロセスの exe を引く（許しを求めたソフトを窓に出すため。plan 31）。試験は作り物に差し替える。
/// </summary>
public interface IConnectionOwners
{
    /// <summary>
    /// <c>127.0.0.1:<paramref name="clientPort"/></c> から <c>127.0.0.1:<paramref name="serverPort"/></c> へつないだプロセスの exe。分からなければ null。
    /// 要求を受けたその場で呼ぶ（答えを返して接続が閉じると、表から消える）
    /// </summary>
    string? ExePathOf(int serverPort, int clientPort);
}

/// <summary>TCP の表の1行（ポートはホストの順に直した物）。</summary>
internal readonly record struct TcpRow(uint LocalAddress, int LocalPort, uint RemoteAddress, int RemotePort, int ProcessId);

/// <summary>
/// Windows の TCP の表（<c>GetExtendedTcpTable</c>）で相手のポートからプロセスの番号を引き、<c>QueryFullProcessImageName</c> で exe を引く。
/// API は 127.0.0.1（IPv4）だけで待つので、IPv4 の表だけを見る
/// </summary>
internal sealed class TcpConnectionOwners : IConnectionOwners
{
    public static TcpConnectionOwners Instance { get; } = new();

    public string? ExePathOf(int serverPort, int clientPort)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return FindOwner(ReadTable(), serverPort, clientPort) is { } processId ? ImagePathOf(processId) : null;
        }
        catch (Exception exception) when (exception is ExternalException or OutOfMemoryException or ArgumentException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API の接続の持ち主を引く", exception);
            return null;
        }
    }

    /// <summary>
    /// 相手の側の行を選ぶ。ループバックでは1本の接続に両側の行が並ぶ（自分の待つ側：ローカル＝待つポート、相手の側：ローカル＝相手のポート）。
    /// 「ローカルのポート＝相手のポート、かつリモートのポート＝待つポート」で、相手の側だけを選ぶ
    /// </summary>
    internal static int? FindOwner(IEnumerable<TcpRow> rows, int serverPort, int clientPort)
    {
        foreach (var row in rows)
        {
            if (row.LocalPort == clientPort && row.RemotePort == serverPort && IsLoopback(row.LocalAddress) && IsLoopback(row.RemoteAddress))
            {
                return row.ProcessId;
            }
        }

        return null;
    }

    /// <summary>127.0.0.0/8（表の値はネットワークの順なので、先頭の1バイトが下位にある）。</summary>
    private static bool IsLoopback(uint address) => (address & 0xFF) == 127;

    /// <summary>表のポートはネットワークの順（ビッグエンディアン）で、下位の16ビットに入っている。</summary>
    internal static int PortOf(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    private const int AfInet = 2;

    /// <summary>TCP_TABLE_OWNER_PID_ALL（プロセスの番号付きの全部の行）。</summary>
    private const int TableOwnerPidAll = 5;

    private const uint ErrorInsufficientBuffer = 122;

    /// <summary>MIB_TCPROW_OWNER_PID の大きさ（state・localAddr・localPort・remoteAddr・remotePort・owningPid の6つの DWORD）。</summary>
    private const int RowSize = 24;

    private static List<TcpRow> ReadTable()
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TableOwnerPidAll, 0);

        // 表は呼ぶ間にも増えるので、足りなければ大きさを聞き直す（3回で諦める）
        for (var attempt = 0; attempt < 3 && result == ErrorInsufficientBuffer; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TableOwnerPidAll, 0);
                if (result == 0)
                {
                    var count = Marshal.ReadInt32(buffer);
                    var rows = new List<TcpRow>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var offset = 4 + (index * RowSize);
                        rows.Add(new TcpRow(
                            (uint)Marshal.ReadInt32(buffer, offset + 4),
                            PortOf((uint)Marshal.ReadInt32(buffer, offset + 8)),
                            (uint)Marshal.ReadInt32(buffer, offset + 12),
                            PortOf((uint)Marshal.ReadInt32(buffer, offset + 16)),
                            Marshal.ReadInt32(buffer, offset + 20)));
                    }

                    return rows;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return [];
    }

    /// <summary>
    /// プロセスの exe。別の Windows ユーザーのプロセス・もう終わったプロセスは引けない（null）。
    /// 引くのに要る権限が最も小さい開き方（PROCESS_QUERY_LIMITED_INFORMATION）で開く
    /// </summary>
    private static string? ImagePathOf(int processId)
    {
        const uint queryLimitedInformation = 0x1000;
        var handle = OpenProcess(queryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // 長いパス（\\?\ の形）まで入る大きさ
            var buffer = new StringBuilder(32768);
            var length = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString(0, length) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
