using System.IO;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 一式の組み立てが途中で止まったら、取った多重起動の錠を放してから投げる（外部の点検 2026-10-06）。
/// 前は錠を握ったまま残り、開き直すと「既に起動しています」になっていた
/// </summary>
public sealed class StartupFailureTests
{
    /// <summary>出口の型は自分で転送するが、送りはしない（試験から外へ出ない）。</summary>
    private sealed class NoNetworkHandler : System.Net.Http.HttpClientHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("試験から外へは出ない");
    }

    [Fact]
    public void 設定のJSONが壊れていて組み立てが止まっても_錠は放してある()
    {
        var root = Path.Combine(Path.GetTempPath(), "bam-app-startup-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "{ 壊れた");
        try
        {
            Assert.ThrowsAny<Exception>(() => new AppServiceContainer(
                paths, new NoNetworkHandler(), (_, _) => Task.CompletedTask, cleanUpTemporaryUnpacks: false));

            using var again = SingleInstanceLock.TryAcquire(paths);
            Assert.NotNull(again);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
