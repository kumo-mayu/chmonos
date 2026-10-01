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
    public static SingleInstanceLock? TryAcquire(AppPaths paths)
    {
        try
        {
            Directory.CreateDirectory(paths.Root);
            var stream = new FileStream(
                paths.LockFile,
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
