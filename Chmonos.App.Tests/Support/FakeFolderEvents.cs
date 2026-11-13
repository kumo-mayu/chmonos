using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 開いている間の監視（plan 49）の作り物の見張り。本物の Windows の見張りは張らず、試験が出来事を起こす
/// （実際のファイルの出来事に結果を左右させない）。
/// </summary>
internal sealed class FakeFolderEvents : IFolderEvents
{
    public event Action<string>? Changed;

    public event Action<FolderEventsFailure>? Failed;

    public bool Disposed { get; private set; }

    public void Fire(string path) => Changed?.Invoke(path);

    public void Fail(FolderEventsFailure failure) => Failed?.Invoke(failure);

    public void Dispose() => Disposed = true;
}

/// <summary>作った作り物の見張りを、フォルダごとに覚える。</summary>
internal sealed class FakeFolderEventsHub
{
    private readonly object _lock = new();
    private readonly List<(string Folder, FakeFolderEvents Events)> _made = [];

    public IFolderEvents? Create(string folder)
    {
        var events = new FakeFolderEvents();
        lock (_lock)
        {
            _made.Add((folder, events));
        }

        return events;
    }

    /// <summary>作った見張り（外した物も含む）。</summary>
    public IReadOnlyList<FakeFolderEvents> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _made.Select(entry => entry.Events)];
            }
        }
    }

    /// <summary>そのフォルダの今の見張りに、出来事を起こす。</summary>
    public void Fire(string folder, string path)
    {
        FakeFolderEvents events;
        lock (_lock)
        {
            events = _made.Last(entry => string.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase) && !entry.Events.Disposed).Events;
        }

        events.Fire(path);
    }
}
