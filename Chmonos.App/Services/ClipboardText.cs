using System.Runtime.InteropServices;

namespace Chmonos.App.Services;

/// <summary>
/// 文字をクリップボードへ写す。
///
/// WPF の <c>Clipboard.SetText</c> は OLE を通り、写した後の仕上げ（OleFlushClipboard）でクリップボードを開き直す。
/// そこで CLIPBRD_E_CANT_OPEN を投げ、中身は入っているのに失敗を返す
/// （未確定の「ファイル名をコピー」が、写せているのに毎回「コピーできませんでした」と出た。2026-09-29。
/// 0.3 秒待って読み直しても開けなかった）。文字を置くだけなら OLE は要らないので、Win32 で1回開いて置いて閉じる。
/// ほかのプロセスが開いているときだけ少し待って試し直す。画面のスレッドで待つので、合わせて 0.3 秒まで
/// </summary>
public static class ClipboardText
{
    private const int Attempts = 6;
    private const int WaitMilliseconds = 50;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public static bool TrySet(string text)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    return EmptyClipboard() && Put(text);
                }
                finally
                {
                    CloseClipboard();
                }
            }

            System.Threading.Thread.Sleep(WaitMilliseconds);
        }

        return false;
    }

    private static bool Put(string text)
    {
        var bytes = (text.Length + 1) * 2;
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var target = GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        // 置けたらメモリの持ち主はクリップボードに移る。置けなかったときだけ自分で返す
        if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}
