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

    /// <summary>ごみ箱へ送る。完全に消すかを聞かれて「いいえ」なら <see cref="NotRecyclableException"/>、失敗は IOException。</summary>
    internal static void Send(string path)
    {
        var operation = new ShFileOperation
        {
            Func = FoDelete,
            // 二重の NUL で終わる一覧（1件）
            From = Path.GetFullPath(path) + "\0\0",
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
        };

        var result = SHFileOperation(ref operation);
        if (operation.AnyOperationsAborted)
        {
            throw new NotRecyclableException("ごみ箱に入りきらないため、削除をやめました。");
        }

        if (result != 0)
        {
            throw new IOException("ごみ箱へ移せませんでした。", unchecked((int)0x80070000) | (result & 0xFFFF));
        }
    }

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

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
