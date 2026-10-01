using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>読み直した記録が前と同じかの見分け（検索のカードを使い回すため）。</summary>
public sealed class ItemFingerprintTests
{
    [Fact]
    public void 中身が同じなら別に作った記録でも同じ()
    {
        // 読み直すたびに一覧は別の物になる。参照で比べると毎回「違う」になり、使い回せない
        var first = new ItemRecord { Id = "1", Local = new LocalBlock { Memo = "メモ", UserTags = [new UserTagAssignment { Top = "服" }] } };
        var second = new ItemRecord { Id = "1", Local = new LocalBlock { Memo = "メモ", UserTags = [new UserTagAssignment { Top = "服" }] } };

        Assert.Equal(ItemFingerprint.Of(first), ItemFingerprint.Of(second));
    }

    [Fact]
    public void 画面に出る欄が1つ違えば違う()
    {
        var before = new ItemRecord { Id = "1", Local = new LocalBlock { IsFavorite = false } };

        Assert.NotEqual(ItemFingerprint.Of(before), ItemFingerprint.Of(before with { Local = before.Local with { IsFavorite = true } }));
        Assert.NotEqual(
            ItemFingerprint.Of(before),
            ItemFingerprint.Of(before with { Local = before.Local with { UserTags = [new UserTagAssignment { Top = "服" }] } }));
    }
}
