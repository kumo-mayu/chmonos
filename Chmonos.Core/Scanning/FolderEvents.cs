namespace Chmonos.Core.Scanning;

/// <summary>
/// 1つの監視フォルダの変化の出来事（plan 49・ユーザ判断 2026-11-13）。
///
/// **出来事の中身は信じない。**「何かが変わった・探し直すきっかけ」にだけ使い、新しいかは <see cref="FolderWatch"/> の判定で決め直す。
/// Windows の見張り（<see cref="System.IO.FileSystemWatcher"/>）は、あふれると出来事を落とし、改名を削除と作成に分けて届けることがあり、
/// ネットワークの場所や同期のアプリの置き場では届かないことがある（<c>docs/spec/folder-watch.md</c>）。
/// 試験は作り物に差し替える（実際のファイルの出来事に結果を左右させない。CLAUDE.md「確かめ方」）。
/// </summary>
public interface IFolderEvents : IDisposable
{
    /// <summary>何かが変わった。引数は変わった場所（ファイルかフォルダ）。裏のスレッドから来る。</summary>
    event Action<string>? Changed;

    /// <summary>見張りがあふれた・壊れた。裏のスレッドから来る。</summary>
    event Action<FolderEventsFailure>? Failed;
}

/// <summary>見張りの失敗の種類。</summary>
public enum FolderEventsFailure
{
    /// <summary>出来事を受けきれずに落とした（<see cref="System.IO.InternalBufferOverflowException"/>）。見張りは生きているので、探し直すだけ。</summary>
    Overflow,

    /// <summary>見張りが止まった（フォルダが消えた・ドライブが外れた・ネットワークが切れた）。張り直しが要る。</summary>
    Broken,
}

/// <summary>
/// <see cref="System.IO.FileSystemWatcher"/> を包んだ見張り。作れない（フォルダが無い・権限が無い）ときは <see cref="TryCreate"/> が null を返す。
/// </summary>
public sealed class FileSystemFolderEvents : IFolderEvents
{
    /// <summary>
    /// 見張りの受け皿の大きさ。既定の 8KB は、zip を展開したフォルダを置くような一度に数百の出来事で溢れる。
    /// 64KB は許される上限（「4 KB or larger, but it must not exceed 64 KB」。受け皿はページに出せないメモリから取る。
    /// https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher.internalbuffersize ）。溢れても探し直すので正しさは変わらない。
    /// フォルダを切り取って貼ると、中身ではなくフォルダの改名だけが届く（同じ文書の FileSystemWatcher の注意）が、探し直しが中を走査するので拾える
    /// </summary>
    private const int BufferBytes = 64 * 1024;

    private readonly FileSystemWatcher _watcher;

    private FileSystemFolderEvents(FileSystemWatcher watcher)
    {
        _watcher = watcher;
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnChanged;

        // ダウンロードは「.crdownload から .zip への改名」で出来上がる。改名を聞かないと、出来上がったことに気付けない
        _watcher.Renamed += (_, e) => Changed?.Invoke(e.FullPath);
        _watcher.Error += (_, e) => Failed?.Invoke(
            e.GetException() is InternalBufferOverflowException ? FolderEventsFailure.Overflow : FolderEventsFailure.Broken);
    }

    public event Action<string>? Changed;

    public event Action<FolderEventsFailure>? Failed;

    /// <summary>フォルダを見張り始める。作れなければ null（張り直しは呼び手が後で試す）。</summary>
    public static IFolderEvents? TryCreate(string folder)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,

                // 中身の書き込み（大きさ・更新日時）と、名前の出入り。属性や最終アクセスは要らない（読むだけで来て、探し直しが増える）
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = BufferBytes,
            };
            var events = new FileSystemFolderEvents(watcher);
            watcher.EnableRaisingEvents = true;
            return events;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed?.Invoke(e.FullPath);

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
