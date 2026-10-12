namespace Chmonos.Core.Storage;

/// <summary>
/// 多重起動の防止。ロックファイルを開いたまま保持する方式にしているので、
/// クラッシュしてもOSがハンドルを解放し、ロックが残り続けることがない。
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private FileStream? _stream;

    private SingleInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>取得できなければ null を返す（既に別のインスタンスが起動している）。</summary>
    public static SingleInstanceLock? TryAcquire(AppPaths paths) => TryAcquire(paths.Root, paths.LockFile);

    /// <summary>
    /// 錠のファイルを指して取る（外から使う API は <see cref="AppPaths.ApiLockFile"/>）。取れなければ null。
    /// 見てから書く確かめ（api.json のプロセス番号）だと、同時に2本起動したときに両方がすり抜けたので、開いたまま持つ錠にした
    /// </summary>
    public static SingleInstanceLock? TryAcquire(string root, string lockFile)
    {
        try
        {
            Directory.CreateDirectory(root);
            var stream = new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return new SingleInstanceLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
