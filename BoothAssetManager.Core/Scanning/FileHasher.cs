using System.Buffers;
using System.Security.Cryptography;

namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// ファイル全体のSHA-256を計算する。
/// 既存の <c>FileInspector.Inspect</c> にも同等の処理はあるが、あちらは同期・キャンセル不可のため、
/// 数百件を順に処理するスキャンでは使わずにこちらを使う。
/// 実測ではディスク経由で約320MB/s（300MBで約1秒）。
/// </summary>
public static class FileHasher
{
    /// <summary>1回に読む量の上限。大きなファイルは前と同じく 1MB ずつ読む。</summary>
    private const int MaxBufferSize = 1 << 20;

    /// <summary>1回に読む量の下限。ディスクの1区画（4KB）より小さく読んでも得が無い。</summary>
    private const int MinBufferSize = 4096;

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        // FileStream 自身の溜め（bufferSize）は使わない。前は 1MB を渡していて、**256バイトのファイルでも1件ごとに 1MB の配列**が
        // 大きいオブジェクト用のヒープにできていた。ばらばらの小さなファイル8万件の取り込みで毎秒約3GBを割り当て、
        // 第2世代の GC が毎秒 50〜100 回走り、その間に商品ページを開くと止まりが3倍ほど長くなっていた（2026-09-30 に測った。
        // docs/research/large-files-2026-09-30.md の「問題6・7」）。読む配列は借りて返し、大きさはファイルに合わせる
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 0,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        var size = BufferSizeFor(stream.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, size), cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>このファイルを読むのに借りる配列の大きさ。ファイルより大きくは借りない。</summary>
    internal static int BufferSizeFor(long fileLength)
        => (int)Math.Clamp(fileLength, MinBufferSize, MaxBufferSize);
}
