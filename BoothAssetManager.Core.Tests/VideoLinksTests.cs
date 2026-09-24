using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>商品に載っている YouTube の動画を拾う（ユーザ指示 2026-09-14：商品ページに動画の欄）。</summary>
public sealed class VideoLinksTests
{
    private static BoothBlock Booth(string? description = null, IReadOnlyList<string>? embeds = null, params string[] sections) => new()
    {
        Description = description,
        Embeds = embeds ?? [],
        H2Sections = sections.Select(text => new H2Section { Heading = "見出し", Text = text }).ToList(),
    };

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?feature=shared&v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ&t=10s")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    public void 本文のいろいろな形のURLから同じ動画を拾う(string url)
    {
        var links = VideoLinks.Find(Booth(sections: $"紹介動画はこちら\n{url}\n"));

        var link = Assert.Single(links);
        Assert.Equal("dQw4w9WgXcQ", link.VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", link.Url);
        Assert.Equal("https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg", link.ThumbnailUrl);
    }

    [Fact]
    public void 埋め込みのHTMLからも拾う()
    {
        var embed = "<iframe width=\"560\" height=\"315\" src=\"//www.youtube-nocookie.com/embed/abcdefghijk?rel=0\" frameborder=\"0\"></iframe>";

        var link = Assert.Single(VideoLinks.Find(Booth(embeds: [embed])));

        Assert.Equal("abcdefghijk", link.VideoId);
    }

    [Fact]
    public void HTMLの形のアンパサンドを挟んだwatchも拾う()
    {
        var link = Assert.Single(VideoLinks.Find(Booth(description: "https://www.youtube.com/watch?feature=x&amp;v=abcdefghijk")));

        Assert.Equal("abcdefghijk", link.VideoId);
    }

    [Fact]
    public void 同じ動画は1本にまとめ見つけた順に並べる()
    {
        var links = VideoLinks.Find(Booth(
            description: "https://youtu.be/BBBBBBBBBBB",
            embeds: ["<iframe src=\"https://www.youtube.com/embed/AAAAAAAAAAA\"></iframe>"],
            "https://www.youtube.com/watch?v=BBBBBBBBBBB と https://youtu.be/CCCCCCCCCCC"));

        Assert.Equal(new[] { "AAAAAAAAAAA", "BBBBBBBBBBB", "CCCCCCCCCCC" }, links.Select(link => link.VideoId));
    }

    [Theory]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://www.youtube.com/channel/UCabcdefghijklmnop")]
    [InlineData("https://booth.pm/ja/items/1234567")]
    [InlineData("バーチャルYouTuberとしての使用はできません")]
    [InlineData("https://youtu.be/tooshort")]
    [InlineData("https://youtu.be/abcdefghijkl")]
    public void 動画でないURLや文字は拾わない(string text)
    {
        Assert.Empty(VideoLinks.Find(Booth(sections: text)));
    }
}
