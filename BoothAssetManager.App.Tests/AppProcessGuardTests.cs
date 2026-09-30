using System.Runtime.CompilerServices;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 道具や試験がアプリの型（App）を作っても、アプリ本体として振る舞わないこと。
/// WPF の Application はコンストラクタで起動の処理を積むので、本体かどうかを App の側で見分けないと、
/// 資源を読むために App を作っただけの道具が、本当の保存先を開いて起動の処理を走らせる（2026-09-30 に起きた）。
/// </summary>
public class AppProcessGuardTests
{
    [Fact]
    public void 試験のプロセスは_アプリ本体ではない()
    {
        Assert.False(App.IsAppProcess);
    }

    [Fact]
    public void 入口がアプリのアセンブリのときだけ_アプリ本体と見なす()
    {
        Assert.True(App.IsEntryOf(typeof(App).Assembly));
        Assert.False(App.IsEntryOf(typeof(AppProcessGuardTests).Assembly));
        Assert.False(App.IsEntryOf(typeof(StoreLocation).Assembly));
        Assert.False(App.IsEntryOf(null));
    }

    [Fact]
    public void アプリの型に触れても_本当の保存先を使う印は立たない()
    {
        var before = StoreLocation.AllowsUserStore;

        // 型の初期化（static App()）を走らせる。道具が new App() したときに起きることと同じ
        RuntimeHelpers.RunClassConstructor(typeof(App).TypeHandle);

        Assert.Equal(before, StoreLocation.AllowsUserStore);
        Assert.False(StoreLocation.AllowsUserStore);
    }
}
