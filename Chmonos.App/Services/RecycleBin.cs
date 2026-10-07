using System.IO;
using System.Runtime.InteropServices;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Services;

/// <summary>
/// ごみ箱へ送る（Windows のシェルの削除）。取り返しのつく消し方だけを使う。
///
/// Microsoft.VisualBasic の DeleteDirectory は、確かめの窓を出さない設定にすると「ごみ箱に入りきらない物は完全に消す」も
/// 黙って行う（FOF_WANTNUKEWARNING を付けない）。ここでは付けて、完全に消すことになるときだけ Windows に聞かせる。
/// </summary>
internal static class RecycleBin
{
    /// <summary>
    /// ごみ箱のあるドライブか。ネットワーク（UNC・割り当てたドライブ）と取り外せるドライブには無く、
    /// そこで消すと完全に消える。分からないときも無いとみなす（消さない側に倒す）。
    /// </summary>
    internal static bool HasRecycleBin(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var drive = new DriveInfo(Path.GetPathRoot(full)!);
            return drive.DriveType == DriveType.Fixed;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 渡す旗。ごみ箱へ送る（ALLOWUNDO）。確かめの窓は出さないが、完全に消すことになるときだけは聞かせる（WANTNUKEWARNING）。
    /// WANTNUKEWARNING を外すと、ごみ箱に入りきらない物を黙って完全に消す
    /// </summary>
    internal const ushort DeleteFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning;

    /// <summary>ごみ箱へ送る。完全に消すかを聞かれて「いいえ」なら <see cref="NotRecyclableException"/>、失敗は IOException。</summary>
    /// <param name="shell">Windows の削除を呼ぶ所（試験で差し替える）。渡す一覧と旗を受け、結果と「やめたか」を返す。</param>
    internal static void Send(string path, Func<string, ushort, (int Result, bool Aborted)>? shell = null)
    {
        // 二重の NUL で終わる一覧（1件）
        var (result, aborted) = (shell ?? CallShell)(Path.GetFullPath(path) + "\0\0", DeleteFlags);
        if (aborted)
        {
            throw new NotRecyclableException("ごみ箱に入りきらないため、削除をやめました。");
        }

        if (result != 0)
        {
            throw new IOException("ごみ箱へ移せませんでした。", unchecked((int)0x80070000) | (result & 0xFFFF));
        }
    }

    private static (int Result, bool Aborted) CallShell(string from, ushort flags)
    {
        var operation = new ShFileOperation { Func = FoDelete, From = from, Flags = flags };
        var result = SHFileOperation(ref operation);
        return (result, operation.AnyOperationsAborted);
    }

    private const uint FoDelete = 3;
    internal const ushort FofSilent = 0x0004;
    internal const ushort FofNoConfirmation = 0x0010;
    internal const ushort FofAllowUndo = 0x0040;
    internal const ushort FofNoErrorUi = 0x0400;
    internal const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Func;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOperation operation);
}
