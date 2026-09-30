using System.IO;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App.Tests;

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
}
