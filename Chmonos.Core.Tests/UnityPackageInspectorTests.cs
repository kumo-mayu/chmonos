using System.IO.Compression;
using System.Text;
using Chmonos.Core.Resolution;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 壊れた unitypackage を掴んでも、手掛かり無しとして続ける（点検 2026-09-23）。
/// TarReader は壊れた頭を InvalidDataException 以外（OverflowException・InvalidOperationException）でも知らせ、受けていなかった。
/// </summary>
public sealed class UnityPackageInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-upi-" + Guid.NewGuid().ToString("N"));

    public UnityPackageInspectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public enum Damage
    {
        /// <summary>大きさの欄が base-256 で桁あふれ（OverflowException）。</summary>
        HugeBase256Size,

        /// <summary>GNU の長い名前の項目の大きさが長すぎる（InvalidOperationException）。</summary>
        HugeGnuLongName,

        /// <summary>大きさの欄が数でない（InvalidDataException・前から受けていた）。</summary>
        LettersInSize,
    }

    /// <summary>
    /// 頭を1つだけ持つ tar を gzip した物。**頭の検査の和は正しく付ける**——合わないと別の所で弾かれ、壊れた欄まで読まれない。
    /// </summary>
    private static byte[] BrokenPackage(Damage damage)
    {
        var header = new byte[512];
        Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef/pathname").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("0000644\0").CopyTo(header, 100);
        Encoding.ASCII.GetBytes("0000000\0").CopyTo(header, 108);
        Encoding.ASCII.GetBytes("0000000\0").CopyTo(header, 116);
        Encoding.ASCII.GetBytes("00000000010\0").CopyTo(header, 124);
        Encoding.ASCII.GetBytes("00000000000\0").CopyTo(header, 136);
        header[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(header, 257);
        Encoding.ASCII.GetBytes("00").CopyTo(header, 263);

        switch (damage)
        {
            case Damage.HugeBase256Size:
                header[124] = 0x80;
                for (var i = 125; i < 136; i++)
                {
                    header[i] = 0x7F;
                }

                break;
            case Damage.HugeGnuLongName:
                header[156] = (byte)'L';
                Encoding.ASCII.GetBytes("77777777777\0").CopyTo(header, 124);
                Encoding.ASCII.GetBytes("ustar  \0").CopyTo(header, 257);
                break;
            case Damage.LettersInSize:
                Encoding.ASCII.GetBytes("zzzzzzzzzzz\0").CopyTo(header, 124);
                break;
        }

        for (var i = 148; i < 156; i++)
        {
            header[i] = (byte)' ';
        }

        var sum = header.Sum(b => (int)b);
        Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);

        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(header);
            gzip.Write(new byte[512 * 3]);
        }

        return memory.ToArray();
    }

    private string ZipWith(byte[] package)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("Broken.unitypackage").Open();
        entry.Write(package);
        return path;
    }

    [Theory]
    [InlineData(Damage.HugeBase256Size)]
    [InlineData(Damage.HugeGnuLongName)]
    [InlineData(Damage.LettersInSize)]
    public void BrokenHeaderIsTreatedAsNoClues(Damage damage)
    {
        var zip = ZipWith(BrokenPackage(damage));

        var hints = UnityPackageInspector.Inspect(zip);

        Assert.Empty(hints.Clues);
    }
}
