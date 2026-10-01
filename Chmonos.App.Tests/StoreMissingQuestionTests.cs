using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 起動のとき「保存先が見つかりません」と聞くかどうか。
/// 聞いた後の「はい」は本番の location.json を消すので、location.json の指す先が無いときだけ聞く。
/// 2026-10-01 の点検で、環境変数で指定した写しが無いときも聞いていて、「はい」で本番の location.json を消す道を見つけた。
/// </summary>
public class StoreMissingQuestionTests
{
    [Fact]
    public void 設定した場所が無いときだけ_聞く()
    {
        Assert.True(App.AsksWhenStoreMissing(new StoreRoot(@"X:\missing", StoreRootSource.Configured), exists: false));
        Assert.False(App.AsksWhenStoreMissing(new StoreRoot(@"D:\store", StoreRootSource.Configured), exists: true));
    }

    [Fact]
    public void 環境変数で指定した場所は_無くても聞かない()
    {
        // 聞くと「はい」で本番の location.json が消える。指定した側が作る前提で、そのまま開く
        Assert.False(App.AsksWhenStoreMissing(new StoreRoot(@"X:\new-sandbox", StoreRootSource.Environment), exists: false));
    }

    [Fact]
    public void 既定の場所は_無くても聞かない()
    {
        Assert.False(App.AsksWhenStoreMissing(new StoreRoot(@"C:\Users\someone\AppData\Local\Chmonos", StoreRootSource.Default), exists: false));
    }
}
