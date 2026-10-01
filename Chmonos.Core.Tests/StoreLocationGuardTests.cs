using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存先の入口の守り。アプリ本体以外（試験・確かめの道具）は、環境変数で保存先を指定しない限り、
/// 利用者の本当の保存先（location.json の指す先・既定の場所）を開けない。
/// 2026-09-30 に、道具の作りかけの版が指定なしでアプリの一式を組み、友人のデータの写しを開いて BOOTH へ取り直しに行った。
/// </summary>
[Collection(nameof(StoreLocationGuardTests))]
[CollectionDefinition(nameof(StoreLocationGuardTests), DisableParallelization = true)]
public class StoreLocationGuardTests : IDisposable
{
    private readonly string? _before = Environment.GetEnvironmentVariable(AppPaths.RootVariable);
    private readonly bool _allowedBefore = StoreLocation.AllowsUserStore;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, _before);
        StoreLocation.AllowsUserStore = _allowedBefore;
    }

    [Fact]
    public void 試験の中では_本当の保存先を使ってよい印は立っていない()
    {
        Assert.False(StoreLocation.AllowsUserStore);
    }

    [Fact]
    public void 指定が無く_印も立っていなければ_止まる()
    {
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, null);
        StoreLocation.AllowsUserStore = false;

        var error = Assert.Throws<InvalidOperationException>(() => StoreLocation.Resolve());
        Assert.Contains(AppPaths.RootVariable, error.Message);
    }

    [Fact]
    public void 環境変数で指定すれば_印が無くても_その場所を返す()
    {
        var root = Path.Combine(Path.GetTempPath(), "bam-store-guard-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, root);
        StoreLocation.AllowsUserStore = false;

        var resolved = StoreLocation.Resolve();

        Assert.Equal(StoreRootSource.Environment, resolved.Source);
        Assert.Equal(Path.GetFullPath(root), resolved.Path);
    }
}
