using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chmonos.Core.Storage;

/// <summary>
/// 一時ファイルを本体の名前へ、**名前が途切れずに**付け替える（POSIX の意味の改名。2026-10-02）。
///
/// <c>File.Replace</c>（ReplaceFile）は本体を退けてから一時ファイルを据えるので、その間、ほかの読み手からは
/// 本体が「無い」（<c>File.Exists</c> が偽・開くと見つからない）か、共有違反で開けない。
/// 1本が置き換え続け、2本が削除の共有つきで読み続けると、5秒で 607回の置き換えに対し「無い」13,205回・
/// 見つからない 420回・共有違反 1,556回だった。読み手は「無い」を「まだ作られていない」と区別できず、
/// 商品は null、一覧は空として返っていた（まれに落ちていた試験2つの原因：
/// 「見つからないファイルを探す」と「未確定の登録と均し」。錠を取らない読みが、別の書き手の置き換えに当たっていた）。
///
/// 付け替えを1回の改名（REPLACE_IF_EXISTS | POSIX_SEMANTICS）にすると、名前は常に旧か新のどちらかを指し、
/// 削除の共有つきで開いている読み手がいても通る。同じ条件で 959回置き換えて、読み手の失敗は0回だった。
/// </summary>
internal static class AtomicReplace
{
    private const int FileRenameInfoEx = 22;
    private const uint ReplaceIfExists = 0x1;
    private const uint PosixSemantics = 0x2;
    private const uint Delete = 0x00010000;
    private const uint GenericRead = 0x80000000;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;

    /// <summary>
    /// 付け替える。**この道が使えないとき（Windows でない・FAT や一部の共有フォルダなど対応しないファイルシステム・
    /// 思わぬ失敗）は何もせずに偽を返す**ので、呼び手は今までの置き換えで続ける。
    /// 共有のせいで弾かれた（アプリの外の読み手が削除の共有を許さずに開いている）ときは、
    /// 今までの置き換えと同じ例外を投げて、呼び手のやり直しに任せる。
    /// </summary>
    public static bool TryReplace(string temporaryPath, string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var handle = CreateFileW(temporaryPath, Delete | GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        // FILE_RENAME_INFO：Flags(4) + 詰め + RootDirectory(ポインタ) + FileNameLength(4) + FileName（終端の 0 を含めて渡す）
        var name = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(path));
        var lengthOffset = IntPtr.Size == 8 ? 16 : 8;
        var nameOffset = lengthOffset + 4;
        var info = new byte[nameOffset + name.Length + 2];
        BitConverter.GetBytes(ReplaceIfExists | PosixSemantics).CopyTo(info, 0);
        BitConverter.GetBytes(name.Length).CopyTo(info, lengthOffset);
        name.CopyTo(info, nameOffset);

        if (SetFileInformationByHandle(handle, FileRenameInfoEx, info, info.Length))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error is 5 or 32 or 33)
        {
            // 拒否（5）・共有違反（32）・ロック違反（33）。今までの置き換えと同じ形の例外にして、呼び手のやり直しに乗せる
            throw error == 5
                ? new UnauthorizedAccessException(new Win32Exception(error).Message)
                : new IOException(new Win32Exception(error).Message, unchecked((int)0x80070000) | error);
        }

        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, byte[] information, int size);
}
