using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の「プロジェクトの中を調べる」の結果の行（メモ15-③）。使ったものの行と同じく、絵（無ければ頭文字）と
/// 大きな絵の吹き出しを出す。絵を持つかは、1枚目の場所があるかで決まる。
/// </summary>
public class ProjectCandidateRowTests
{
    private static ProjectCandidateRowViewModel Row(string name, string? thumbnailPath = null)
        => new() { ItemId = "1", Name = name, Present = 1, Total = 2, ThumbnailPath = thumbnailPath };

    [Fact]
    public void 絵の場所があれば_吹き出しの絵を出す()
        => Assert.True(Row("作り物の靴", @"C:\images\a.webp").HasHoverImage);

    [Fact]
    public void 絵の場所が無ければ_吹き出しの絵は出さず_頭文字を出す()
    {
        var row = Row("作り物の靴");

        Assert.False(row.HasHoverImage);
        Assert.Null(row.Thumbnail);
        Assert.Null(row.HoverImage);
        Assert.Equal("作", row.Initial);
    }
}
