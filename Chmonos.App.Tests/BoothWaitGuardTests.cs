using System.IO;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 問い合わせの間の待ちを差し替えてよいのは、相手が作り物のときだけ（絶対に破らない決め事1）。
///
/// 待ちを差し替えた組み立ては、間隔を空けず、PC で1つの門（<c>BoothMachineGate</c>）にも入らない。
/// 試験（<c>TestApp</c>）は作り物の BOOTH と一緒に渡すので困らないが、道具が通信の出口を本物のまま待ちだけを
/// 差し替えると、本物の BOOTH へ間を空けずに、ほかのアプリと重なって出る。組み立ての入口で断る。
/// </summary>
public sealed class BoothWaitGuardTests
{
    [Fact]
    public void 通信の出口が本物のまま待ちだけを差し替える組み立ては断る()
    {
        // 断るのは保存先に触れる前なので、この場所には何も作られない
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "bam-app-guard-" + Guid.NewGuid().ToString("N")[..8]));

        var thrown = Assert.Throws<ArgumentException>(
            () => new AppServiceContainer(paths, http: null, boothDelay: (_, _) => Task.CompletedTask));

        Assert.Equal("boothDelay", thrown.ParamName);
        Assert.False(Directory.Exists(paths.Root));
    }

    /// <summary>出口の型は自分で転送するが、送りはしない（試験から外へ出ない）。</summary>
    private sealed class NoNetworkHandler : System.Net.Http.HttpClientHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("試験から外へは出ない");
    }

    /// <summary>
    /// 渡した出口が自分で転送する型でも、アプリの組み立ては自動の転送を切る（2026-10-06 の外部の点検）。
    /// 自動の転送は門を通らずに転送先へ出るので、転送は BoothClient が受けて門を通して取り直す。
    /// </summary>
    [Fact]
    public void 組み立ては通信の出口の自動の転送を切る()
    {
        var root = Path.Combine(Path.GetTempPath(), "bam-app-redirect-" + Guid.NewGuid().ToString("N")[..8]);
        var handler = new NoNetworkHandler();
        try
        {
            using (new AppServiceContainer(
                new AppPaths(root), handler, (_, _) => Task.CompletedTask, cleanUpTemporaryUnpacks: false))
            {
                Assert.False(handler.AllowAutoRedirect);
            }
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
