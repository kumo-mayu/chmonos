using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Chmonos.App.Services;

/// <summary>
/// ドライブの抜き差しの知らせ（<c>WM_DEVICECHANGE</c>）を受けて、まとめて1回だけ <see cref="Changed"/> を上げる
/// （カードの帯「取り外し中」を付け直すため。ユーザ判断 2026-11-20）。
/// ここでは何も確かめない。知らせるだけで、確かめ（ドライブの根だけを見る）は受け手が裏で行う。
/// 抜き差しでは同じ知らせが数回続けて来るので、最後の知らせから待って1回にまとめる。
/// つないだ直後は、ドライブ文字・通し番号が読めるまでに少しかかることがある（待つ理由）。
/// </summary>
internal sealed class DriveChangeWatcher
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypVolume = 2;

    private readonly DispatcherTimer _timer;

    /// <param name="delay">最後の知らせから上げるまで。既定の 1.5 秒は、つないだ直後の読み取りが落ち着く目安（[推測]）。</param>
    public DriveChangeWatcher(TimeSpan? delay = null)
    {
        _timer = new DispatcherTimer { Interval = delay ?? TimeSpan.FromSeconds(1.5) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>ドライブの抜き差しが落ち着いた。画面のスレッドで上がる。</summary>
    public event EventHandler? Changed;

    /// <summary>窓のメッセージの受け口へ付ける。</summary>
    public void Attach(HwndSource source) => source.AddHook(OnMessage);

    /// <summary>抜き差しの知らせが来た。続けて何度呼ばれても <see cref="Changed"/> は1回にまとまる。</summary>
    public void Signal()
    {
        // 止めて掛け直す＝最後の知らせからの時間で数える
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>ボリューム（ドライブ）が付いた・外れた知らせか。ほかの機器（USB の機器そのものなど）は見ない。</summary>
    internal static bool IsVolumeChange(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmDeviceChange || lParam == IntPtr.Zero)
        {
            return false;
        }

        var kind = (int)wParam;
        if (kind is not (DbtDeviceArrival or DbtDeviceRemoveComplete))
        {
            return false;
        }

        // DEV_BROADCAST_HDR { dbch_size; dbch_devicetype; dbch_reserved }
        return Marshal.ReadInt32(lParam, 4) == DbtDevTypVolume;
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (IsVolumeChange(msg, wParam, lParam))
        {
            Signal();
        }

        return IntPtr.Zero;
    }
}
