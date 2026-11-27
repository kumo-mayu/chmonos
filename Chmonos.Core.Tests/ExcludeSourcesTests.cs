using Chmonos.Core.Models;

namespace Chmonos.Core.Tests;

/// <summary>
/// 外した操作の出どころ：記録の日本語の文から API の英字の値を引く（ユーザ判断 2026-11-27・案 A）。
/// 記録の文は配った版の excluded.json と同じ文字で書かれているので、定数を直に比べず文字で確かめる（定数を言い換えたら前の記録が引けなくなる）。
/// </summary>
public sealed class ExcludeSourcesTests
{
    [Theory]
    [InlineData("未確定画面から除外", "resolve")]
    [InlineData("未確定画面から元zipごと除外", "resolveArchive")]
    [InlineData("未確定画面からまとめて除外", "resolveBulk")]
    [InlineData("フォルダビューからフォルダごと除外", "folderView")]
    public void 画面が書く4つの文は_それぞれの値になる(string reason, string value)
        => Assert.Equal(value, ExcludeSources.ValueOf(reason));

    [Theory]
    [InlineData("BOOTH商品ではない")]
    [InlineData("")]
    [InlineData("未確定画面から除外 ")]
    public void 知らない文は_other(string reason)
        => Assert.Equal(ExcludeSources.Other, ExcludeSources.ValueOf(reason));

    [Fact]
    public void 文の無い記録は_null_のまま()
        => Assert.Null(ExcludeSources.ValueOf(null));

    [Fact]
    public void 画面が書く定数は_表の文と同じ()
    {
        Assert.Equal("resolve", ExcludeSources.ValueOf(ExcludeSources.ResolveText));
        Assert.Equal("resolveArchive", ExcludeSources.ValueOf(ExcludeSources.ResolveArchiveText));
        Assert.Equal("resolveBulk", ExcludeSources.ValueOf(ExcludeSources.ResolveBulkText));
        Assert.Equal("folderView", ExcludeSources.ValueOf(ExcludeSources.FolderViewText));
    }
}
