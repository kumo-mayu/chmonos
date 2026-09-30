using System.Security.Cryptography;
using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ファイルのハッシュ（<see cref="FileHasher"/>）。読む配列をファイルの大きさに合わせて借りる形にしたので、
/// 配列より小さい・ちょうど・大きいファイルで、前と同じ値（ファイル全体の SHA-256）が出ることを見る。
/// </summary>
public sealed class FileHasherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "file-hasher-" + Guid.NewGuid().ToString("N"));

    public FileHasherTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string NewFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>毎回同じ並びの作り物の中身（乱数の種を固定する）。</summary>
    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    [Theory]
    [InlineData(0)]                  // 空のファイル
    [InlineData(1)]
    [InlineData(256)]                // ばらばらの小さな画像
    [InlineData(4095)]               // 借りる下限のすぐ下
    [InlineData(4096)]               // ちょうど下限
    [InlineData(4097)]
    [InlineData((1 << 20) - 1)]      // 上限のすぐ下
    [InlineData(1 << 20)]            // ちょうど上限（1回で読み切る）
    [InlineData((1 << 20) + 1)]      // 上限を1バイト超える（2回に分かれる）
    [InlineData(2_500_000)]          // 何回かに分けて読む
    public async Task ファイル全体のSHA256を返す(int length)
    {
        var bytes = Bytes(length);
        var path = NewFile($"sample-{length}.bin", bytes);

        var hash = await FileHasher.ComputeSha256Async(path);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), hash);
    }

    [Fact]
    public async Task 知られた値と合う()
    {
        // "abc" の SHA-256（FIPS 180 の例）
        var path = NewFile("abc.txt", "abc"u8.ToArray());

        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            await FileHasher.ComputeSha256Async(path));
    }

    /// <summary>
    /// 借りる配列はファイルより大きくしない。前は 256 バイトのファイルにも 1MB を割り当てていて、
    /// ばらばらの小さなファイル2万件のハッシュで 20GB を割り当てていた（直した後は 21MB）。
    /// </summary>
    [Theory]
    [InlineData(0L, 4096)]
    [InlineData(256L, 4096)]
    [InlineData(4096L, 4096)]
    [InlineData(100_000L, 100_000)]
    [InlineData(1L << 20, 1 << 20)]
    [InlineData(5_000_000_000L, 1 << 20)]   // 4GB を超えても桁あふれしない
    public void 借りる配列はファイルの大きさに合わせる(long fileLength, int expected)
        => Assert.Equal(expected, FileHasher.BufferSizeFor(fileLength));

    [Fact]
    public async Task 中断を渡すと止まる()
    {
        var path = NewFile("big.bin", Bytes(3_000_000));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileHasher.ComputeSha256Async(path, cancel.Token));
    }
}
