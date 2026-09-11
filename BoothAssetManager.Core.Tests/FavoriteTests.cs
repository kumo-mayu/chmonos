using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

/// <summary>お気に入りの星（#70）。検索カードから切り替える。</summary>
public sealed class FavoriteTests
{
    [Fact]
    public void 星の保存は星だけを書く()
    {
        // カードが抱えているのは読み込み時の写し。その間に書かれたメモを古い値で潰さない
        var current = new LocalBlock { Memo = "今のメモ", IsFavorite = false };
        var cardCopy = new LocalBlock { Memo = "古い写しのメモ", IsFavorite = true };

        var merged = LocalFields.Merge(current, cardCopy, LocalOwners.Favorite);

        Assert.True(merged.IsFavorite);
        Assert.Equal("今のメモ", merged.Memo);
    }

    [Fact]
    public void 既定では付いていない()
        => Assert.False(new LocalBlock().IsFavorite);
}
