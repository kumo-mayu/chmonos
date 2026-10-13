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
    /// 錠のファイルを指して取る。取れなければ null。
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

    /// <summary>
    /// その錠を、ほかのプロセスが掴んでいるか。持ち主は共有なしで開いているので、こちらが開けなければ掴まれている。
    /// 落ちたプロセスの錠は OS が放す（DeleteOnClose で消える）ので、記録に残ったプロセス番号より確か
    /// </summary>
    public static bool IsHeld(string lockFile)
    {
        if (!File.Exists(lockFile))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
