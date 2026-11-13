using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Chmonos.Mcp;

/// <summary>
/// 127.0.0.1 のポートで待ち受けているプロセスの番号を、Windows の TCP の表（<c>GetExtendedTcpTable</c>）から読む。
/// 札の PID と開始時刻が合っていても、札のポートを別のプロセスが使っていることがある（札の書き換え・ポートを変えた時に札の更新が失敗して、
/// 古いポートを別のプロセスが取った）。そこへトークンを送らないよう、送る前に持ち主を照らす（2026-11-13 の点検 M5）。
/// 持ち主を OS に聞くので、相手の答え（名乗り）を信じずに済む。表は管理者でなくても読める（netstat -ano と同じ）
/// </summary>
internal static class PortOwner
{
    private const int AfInet = 2;

    /// <summary>TCP_TABLE_OWNER_PID_LISTENER：待ち受けの行だけ、持ち主の PID 付き。</summary>
    private const int OwnerPidListener = 3;

    private const int ErrorInsufficientBuffer = 122;

    /// <summary>127.0.0.1（ネットワークの並びのまま uint で読んだ値）。</summary>
    private const uint Loopback = 0x0100007F;

    /// <summary>
    /// 127.0.0.1 の <paramref name="port"/> へつなぐと受けるプロセスの番号。誰も待ち受けていなければ null。表を読めなければ <see cref="Win32Exception"/>。
    /// 127.0.0.1 で待つ行を 0.0.0.0 で待つ行より先に見る（両方あると、127.0.0.1 へのつなぎは狭い方が受ける）。
    /// 通訳は 127.0.0.1 へ IPv4 でつなぐので、IPv6 の表は見ない
    /// </summary>
    public static int? OwnerOf(int port)
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, OwnerPidListener, 0);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (result != ErrorInsufficientBuffer && result != 0)
            {
                throw new Win32Exception((int)result);
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, AfInet, OwnerPidListener, 0);
                if (result == 0)
                {
                    return Find(buffer, port);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            // 読む間に待ち受けが増えて足りなくなった。新しい大きさで読み直す
        }

        throw new Win32Exception((int)result);
    }

    private static int? Find(IntPtr buffer, int port)
    {
        var count = Marshal.ReadInt32(buffer);
        var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
        int? any = null;
        for (var i = 0; i < count; i++)
        {
            var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + (i * rowSize));

            // ポートはネットワークの並び（大きい桁が先）で下の16ビットに入る
            var rowPort = (int)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
            if (rowPort != port)
            {
                continue;
            }

            if (row.LocalAddr == Loopback)
            {
                return (int)row.OwningPid;
            }

            if (row.LocalAddr == 0)
            {
                any = (int)row.OwningPid;
            }
        }

        return any;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);
}
