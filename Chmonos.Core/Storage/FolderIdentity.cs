using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chmonos.Core.Storage;

/// <summary>
/// 2つの場所が同じ実体か・内側かを見る（外部の点検 2026-10-06）。
///
/// 文字の比べ（<see cref="Path.GetFullPath(string)"/>）だけだと、ジャンクション・シンボリックリンク・subst のドライブなど、
/// 別の名前で同じフォルダを指す場所を「別の場所」と見る。保存先の置き換えでそこを選ぶと、選んだ先の中身を退ける所で
/// 今の保存先の中身ごと退け、失敗の片付けで同じ実体の元ファイルを消していた。
///
/// 実体は、フォルダを開いて OS に聞いた「ボリュームの番号＋ファイルの ID」で見る。
/// リンクの行き先を文字で解く（<see cref="Directory.ResolveLinkTarget(string, bool)"/>）より、subst やドライブ文字の別名まで同じに扱える。
/// 内側かは、調べる側の祖先を根元まで1つずつ開いて比べる（途中の祖先がリンクでも拾える。深さの分だけ開くので、数十回で済む）。
/// </summary>
public static class FolderIdentity
{
    /// <summary><paramref name="path"/> が <paramref name="folder"/> そのものか、その内側か。文字でも実体でも見て、どちらかで重なれば true。</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        if (TextSameOrInside(path, folder))
        {
            return true;
        }

        // 無い場所は何も含まない（これから作る運ぶ先など）。実体で比べるのは在る方だけ
        if (IdOf(folder) is not { } target)
        {
            return false;
        }

        for (var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
             !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            if (IdOf(current) is { } id && id == target)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>同じ実体か（文字でも実体でも）。</summary>
    public static bool IsSame(string a, string b)
        => string.Equals(
               Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
               Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
               StringComparison.OrdinalIgnoreCase)
           || (IdOf(a) is { } left && IdOf(b) is { } right && left == right);

    private static bool TextSameOrInside(string path, string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var container = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(container, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ボリュームの番号とファイルの ID。開けない（無い・権限が無い）・Windows でないなら null。</summary>
    internal static (ulong Volume, ulong High, ulong Low)? IdOf(string path)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(path))
        {
            return null;
        }

        // 中身は読まない（属性を読む権限だけ）。フォルダはバックアップの意味付けを付けないと開けない。
        // リンクは追って開く（FILE_FLAG_OPEN_REPARSE_POINT を付けない）——行き先の実体の ID が欲しい
        using var handle = CreateFileW(
            path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        // ReFS はファイルの ID が 128bit あり、古い GetFileInformationByHandle の 64bit では食い違い得るので、FILE_ID_INFO を先に使う
        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out var info, Marshal.SizeOf<FileIdInfo>()))
        {
            return (info.VolumeSerialNumber, info.FileIdHigh, info.FileIdLow);
        }

        return GetFileInformationByHandle(handle, out var legacy)
            ? (legacy.VolumeSerialNumber, legacy.FileIndexHigh, legacy.FileIndexLow)
            : null;
    }

    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        // FILE_ID_128 の16バイト。同じかを見るだけなので、上下の意味は問わない
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        // FILETIME は 4 バイト揃えの2語。long にすると 8 バイトに揃えられて、後ろの欄がずれる
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileIdInfo information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
}
