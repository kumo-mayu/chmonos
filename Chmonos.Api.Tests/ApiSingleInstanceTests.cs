using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Api.Tests;

/// <summary>外から使う API の多重起動（保存先ごとに1本。2026-10-13）。</summary>
public sealed class ApiSingleInstanceTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void 錠は1本目だけが取れ_放せば次が取れる()
    {
        var first = SingleInstanceLock.TryAcquire(_paths.Root, _paths.ApiLockFile);
        Assert.NotNull(first);
        Assert.Null(SingleInstanceLock.TryAcquire(_paths.Root, _paths.ApiLockFile));

        // アプリの錠とは別（アプリと API は同じ保存先で並んで動く）
        using (var app = SingleInstanceLock.TryAcquire(_paths))
        {
            Assert.NotNull(app);
        }

        first!.Dispose();
        using var again = SingleInstanceLock.TryAcquire(_paths.Root, _paths.ApiLockFile);
        Assert.NotNull(again);
    }

    [Fact]
    public void 止めるときは_自分の記録のときだけ止まったと書く()
    {
        // ほかのプロセスの記録は消さない（消すと、動いている API が見えなくなる）
        ApiFiles.WriteEndpoint(_paths, new ApiEndpointRecord { Port = 40001, Token = "other-token", ProcessId = Environment.ProcessId + 1 });
        ApiFiles.MarkStopped(_paths, 40000, "my-token");
        Assert.Equal(("other-token", Environment.ProcessId + 1), (ApiFiles.ReadEndpoint(_paths)!.Token, ApiFiles.ReadEndpoint(_paths)!.ProcessId));

        ApiFiles.WriteEndpoint(_paths, new ApiEndpointRecord { Port = 40000, Token = "my-token", ProcessId = Environment.ProcessId });
        ApiFiles.MarkStopped(_paths, 40000, "my-token");
        var stopped = ApiFiles.ReadEndpoint(_paths)!;
        Assert.Null(stopped.ProcessId);
        Assert.Equal("my-token", stopped.Token);
    }
}
