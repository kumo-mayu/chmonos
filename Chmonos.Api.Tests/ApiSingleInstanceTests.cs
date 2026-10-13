using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Api.Tests;

/// <summary>外から使う API の api.json の書き戻し（ほかのプロセスの記録は消さない。2026-10-13）。</summary>
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
