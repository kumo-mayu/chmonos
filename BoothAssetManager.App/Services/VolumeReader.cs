using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 今つながっているボリュームのドライブ文字・通し番号・ラベルを Windows から読む（<c>GetVolumeInformation</c>）。
/// 読めない物（空の光学ドライブ・つながっていないネットワークドライブ）は飛ばす。
/// </summary>
internal sealed class VolumeReader : IVolumeReader
{
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
                if (drive.DriveType is DriveType.NoRootDirectory or DriveType.Unknown or DriveType.CDRom)
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
