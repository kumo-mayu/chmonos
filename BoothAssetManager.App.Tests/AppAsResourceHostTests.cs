using System.Runtime.CompilerServices;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 試験や確かめの道具の中では、<see cref="App"/> は資源を読むための入れ物としてだけ使われる。
/// 起動の処理を進めず、利用者の本当の保存先を使ってよい印も立てない。
/// 2026-09-30 に、<c>new App()</c> しただけの道具で起動の処理が走り、本番の指す保存先が開いた
/// （WPF の Application はコンストラクタの中で OnStartup を呼ぶ仕事を積む）。
/// </summary>
public class AppAsResourceHostTests
{
    [Fact]
    public void 試験の中では_アプリ本体として起動された扱いにならない()
    {
        Assert.False(App.IsLaunchedAsApp);
    }

    [Fact]
    public void Appの型に触れても_本当の保存先を使ってよい印は立たない()
    {
        RuntimeHelpers.RunClassConstructor(typeof(App).TypeHandle);

        Assert.False(StoreLocation.AllowsUserStore);
    }
}
