namespace Chmonos.Core.Scanning;

/// <summary>
/// zip が各ファイルに記録している CRC-32（IEEE 802.3）を、ディスクのファイルから計算する（点検29：展開先フォルダを消す前に、中身まで照らすため）。
/// System.IO.Hashing を足さずに済むよう、ここで表を引く形で書く（照らすのは人が押した削除のときだけで、速さは要らない）
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    /// <summary>ファイルの CRC-32。読みながら計算し、丸ごとメモリに載せない。</summary>
    public static uint OfFile(string path, CancellationToken cancellationToken = default)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[1 << 16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, FileOptions.SequentialScan);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
            {
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
        }

        return ~crc;
    }
}
