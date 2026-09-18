using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り直した結果、何を「変わった」と呼ぶか。
///
/// **決めないと要確認が毎週埋まる。**見るのは人が気にする変化だけ。
/// </summary>
public class BoothChangesTests
{
    private static BoothBlock Block(
        string name = "商品",
        string? price = "¥ 1,000",
        bool endOfSale = false,
        int wishLists = 100,
        string? description = "説明",
        int variations = 1,
        int images = 2,
        IReadOnlyList<H2Section>? sections = null)
        => new()
        {
            FetchedAt = DateTimeOffset.Now,
            Name = name,
            PriceText = price,
            IsEndOfSale = endOfSale,
            WishListsCount = wishLists,
            Description = description,
            Variations = Enumerable.Range(1, variations)
                .Select(index => new BoothVariation { Id = index, Name = $"v{index}" })
                .ToList(),
            Images = Enumerable.Range(1, images)
                .Select(index => new BoothImage { OriginalUrl = $"https://booth.pximg.net/{index}.jpg" })
                .ToList(),
            H2Sections = sections ?? [],
        };

    private static H2Section Section(string heading, string text)
        => new() { Heading = heading, NormalizedHeading = heading, Text = text };

    [Fact]
    public void SaysNothingChangedWhenNothingDid()
    {
        Assert.Empty(BoothChanges.Describe(Block(), Block()));
    }

    /// <summary>**スキ数は見ない。**必ず動くので、毎週全商品が「変わった」になる。</summary>
    [Fact]
    public void IgnoresTheWishListCountBecauseItAlwaysMoves()
    {
        Assert.Empty(BoothChanges.Describe(Block(wishLists: 100), Block(wishLists: 5000)));
    }

    /// <summary>
    /// 見出しがある商品では、説明文そのもの（見出しの外の短い説明）は見ない。
    /// 見出しごとに比べれば「どこが変わったか」を名指しできるため。
    /// </summary>
    [Fact]
    public void IgnoresTheShortDescriptionWhenSectionsExist()
    {
        var sections = new[] { Section("使い方", "本文") };

        Assert.Empty(BoothChanges.Describe(
            Block(description: "説明です", sections: sections),
            Block(description: "説明でず", sections: sections)));
    }

    /// <summary>
    /// 見出しが取れない商品は、説明文しか手掛かりが無いのでそれを比べる（2026-09-18 に設計へ合わせた）。
    /// </summary>
    [Fact]
    public void ReportsTheDescriptionWhenThereAreNoSections()
    {
        var diffs = BoothChanges.Describe(Block(description: "前の説明"), Block(description: "後の説明"));

        Assert.Single(diffs);
        Assert.Equal("説明文", diffs[0].Field);
        Assert.Equal("後の説明", diffs[0].After);
    }

    /// <summary>見出しごとに名指しする。更新履歴の見出しだけを強い通知にする。</summary>
    [Fact]
    public void NamesTheChangedSectionAndMarksUpdateHistoryAsStrong()
    {
        var diffs = BoothChanges.Describe(
            Block(sections: [Section("更新履歴", "v1.0"), Section("使い方", "本文")]),
            Block(sections: [Section("更新履歴", "v1.1"), Section("使い方", "本文")]));

        Assert.Single(diffs);
        Assert.Equal("更新履歴", diffs[0].Field);
        Assert.True(BoothChanges.HasStrongChange(diffs));
    }

    /// <summary>ほかの見出しの変化は、強い通知にしない。</summary>
    [Fact]
    public void DoesNotMarkOtherSectionsAsStrong()
    {
        var diffs = BoothChanges.Describe(
            Block(sections: [Section("使い方", "前")]),
            Block(sections: [Section("使い方", "後")]));

        Assert.Single(diffs);
        Assert.False(BoothChanges.HasStrongChange(diffs));
    }

    /// <summary>取り直しでHTMLが取れないと見出しは0件になる。全部消えたと知らせると嘘になる。</summary>
    [Fact]
    public void SaysNothingWhenTheHtmlCouldNotBeRead()
    {
        Assert.Empty(BoothChanges.Describe(Block(sections: [Section("使い方", "本文")]), Block()));
    }

    /// <summary>販売終了はいちばん知りたい。もう買えないため。</summary>
    [Fact]
    public void ReportsWhenTheItemStoppedBeingSold()
    {
        var diffs = BoothChanges.Describe(Block(endOfSale: false), Block(endOfSale: true));

        Assert.Single(diffs);
        Assert.Equal("販売状況", diffs[0].Field);
        Assert.Equal("販売終了", diffs[0].After);
    }

    /// <summary>販売終了から戻ったことも知らせる。買い直せるようになった。</summary>
    [Fact]
    public void ReportsWhenTheItemCameBackOnSale()
    {
        var diffs = BoothChanges.Describe(Block(endOfSale: true), Block(endOfSale: false));

        Assert.Single(diffs);
        Assert.Equal("販売中", diffs[0].After);
    }

    [Fact]
    public void ReportsPriceNameVariationAndImageChanges()
    {
        var diffs = BoothChanges.Describe(
            Block(name: "旧", price: "¥ 1,000", variations: 1, images: 2),
            Block(name: "新", price: "¥ 1,200", variations: 3, images: 5));

        var fields = diffs.Select(diff => diff.Field).ToList();

        Assert.Contains("商品名", fields);
        Assert.Contains("価格", fields);
        Assert.Contains("種類", fields);
        Assert.Contains("画像", fields);
    }

    /// <summary>開かなくても判断できるように、1行に並べる。</summary>
    [Fact]
    public void WritesOneLineThatCanBeReadWithoutOpeningTheItem()
    {
        var diffs = BoothChanges.Describe(
            Block(price: "¥ 1,000", images: 8),
            Block(price: "¥ 1,200", images: 10));

        Assert.Equal("価格 ¥ 1,000 → ¥ 1,200 / 画像 8枚 → 10枚", BoothChanges.Summarize(diffs));
    }
}
