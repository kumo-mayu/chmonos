using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>ショップの星とメモ（shops.json・ユーザ判断 2026-09-16）。</summary>
public class ShopNotesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void 星を付けると名前の控え付きで記録ができる()
    {
        var records = ShopNotes.Apply([], "example-shop", "サンプル店", "uuid-1", note => note with { IsFavorite = true }, Now);

        var note = Assert.Single(records);
        Assert.True(note.IsFavorite);
        Assert.Equal("サンプル店", note.NameHint);
        Assert.Equal("uuid-1", note.Uuid);
        Assert.Equal(Now, note.UpdatedAt);
    }

    [Fact]
    public void メモを直しても星は残る()
    {
        var records = ShopNotes.Apply([], "example-shop", "サンプル店", null, note => note with { IsFavorite = true }, Now);
        records = ShopNotes.Apply(records, "example-shop", "サンプル店", null, note => note with { Memo = "改変可・再配布不可" }, Now);

        var note = Assert.Single(records);
        Assert.True(note.IsFavorite);
        Assert.Equal("改変可・再配布不可", note.Memo);
    }

    [Fact]
    public void 星もメモも無くなったら記録を落とす()
    {
        var records = ShopNotes.Apply([], "example-shop", "サンプル店", null, note => note with { IsFavorite = true }, Now);
        records = ShopNotes.Apply(records, "example-shop", "サンプル店", null, note => note with { IsFavorite = false }, Now);

        Assert.Empty(records);
    }

    /// <summary>ショップ一覧はサブドメインを大文字小文字を区別せずに束ねているので、記録も同じにする。</summary>
    [Fact]
    public void 鍵は大文字小文字を区別しない()
    {
        var records = ShopNotes.Apply([], "Example-Shop", null, null, note => note with { IsFavorite = true }, Now);
        records = ShopNotes.Apply(records, "example-shop", null, null, note => note with { Memo = "メモ" }, Now);

        Assert.Single(records);
        Assert.NotNull(ShopNotes.Of(records, "EXAMPLE-SHOP"));
        Assert.Contains("example-shop", ShopNotes.FavoriteKeys(records));
    }

    [Fact]
    public void 名前が空で来たら前の控えを残す()
    {
        var records = ShopNotes.Apply([], "local-1234abcd", "手で入れた店", null, note => note with { IsFavorite = true }, Now);
        records = ShopNotes.Apply(records, "local-1234abcd", null, null, note => note with { Memo = "メモ" }, Now);

        Assert.Equal("手で入れた店", Assert.Single(records).NameHint);
    }
}
