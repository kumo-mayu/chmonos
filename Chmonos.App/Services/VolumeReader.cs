using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.App.Services;

/// <summary>
/// 今つながっているボリュームのドライブ文字・通し番号・ラベルを Windows から読む（<c>GetVolumeInformation</c>）。
/// 読めない物（空の光学ドライブ・つながっていないネットワークドライブ）は飛ばす。
///
/// **通し番号で見分けられない文字も飛ばす**（点検 2026-09-23）。読み替えは「同じ番号＝同じボリューム」に頼るので、
/// 番号が同じでも中身が違う文字を渡すと、記録を別の場所の下に出してしまう。
/// <list type="bullet">
/// <item>ネットワークドライブ：番号は共有を置いたボリュームの物で、同じ NAS の別の共有を割り当てた文字が同じ番号になる</item>
/// <item>subst の文字：元のドライブと同じ番号を返すので、フォルダを文字に割り当てただけで「ドライブ文字が変わった」に見える</item>
/// </list>
/// 通し番号0と、同じ番号が2文字に見えるとき（複製したディスク）は Core の側で読み替えに使わない。
/// </summary>
internal sealed class VolumeReader : IVolumeReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, StringBuilder targetPath, int maxLength);

    /// <summary>subst で割り当てた文字か。subst の先は <c>\??\C:\…</c> の形になる（本物のボリュームは <c>\Device\HarddiskVolume…</c>）。</summary>
    private static bool IsSubst(string letter)
    {
        var target = new StringBuilder(1024);
        return QueryDosDeviceW(letter, target, target.Capacity) > 0
            && target.ToString().StartsWith(@"\??\", StringComparison.Ordinal);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);

    public IReadOnlyList<MountedVolume> Mounted()
    {
        var volumes = new List<MountedVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is DriveType.NoRootDirectory or DriveType.Unknown or DriveType.CDRom or DriveType.Network
                    || IsSubst(drive.Name[..2]))
                {
                    continue;
                }

                var label = new StringBuilder(261);
                if (!GetVolumeInformationW(drive.Name, label, label.Capacity, out var serial, out _, out _, null, 0))
                {
                    continue;
                }

                volumes.Add(new MountedVolume(
                    drive.Name[..2].ToUpperInvariant(),
                    serial.ToString("X8"),
                    label.Length > 0 ? label.ToString() : null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return volumes;
    }
}
