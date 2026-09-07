using BoothAssetManager.Core.Booth;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class H2SectionExtractorTests
{
    /// <summary>
    /// 実際のBOOTHページと同じ構造。短い説明のdivと見出し付きセクションは兄弟で、
    /// 商品名の見出しはどちらの外側にある。
    /// </summary>
    private const string RealisticHtml = """
        <html><body>
          <h2 class="font-bold leading-[32px] m-0 text-[24px] break-all">Frill Knit Set</h2>
          <section class="main-info-column">
            <div class="js-market-item-detail-description description">
              <p class="autolink break-words typography-16 whitespace-pre-line">アバターに着せる衣装です。</p>
            </div>
            <section class="shop__text">
              <h2 class="break-words font-bold leading-[32px] !m-0 pb-16 text-[24px]">前提条件</h2>
              <p class="break-words js-autolink !m-0 !p-0 whitespace-pre-line">セラフィム / マヌカ に対応しています。</p>
            </section>
            <section class="shop__text">
              <h2 class="break-words font-bold leading-[32px] !m-0 pb-16 text-[24px]">◈アップデート履歴◈</h2>
              <p class="break-words js-autolink !m-0 !p-0 whitespace-pre-line">v1.2 マヌカ対応を追加。</p>
            </section>
          </section>
        </body></html>
        """;

    [Fact]
    public void ExtractsEachSectionAsHeadingAndText()
    {
        var result = H2SectionExtractor.Extract(RealisticHtml);

        Assert.Equal(2, result.Sections.Count);
        Assert.Equal("前提条件", result.Sections[0].Heading);
        Assert.Equal("セラフィム / マヌカ に対応しています。", result.Sections[0].Text);
        Assert.Equal("v1.2 マヌカ対応を追加。", result.Sections[1].Text);
    }

    [Fact]
    public void ExcludesItemTitleHeadingOutsideDescriptionContainer()
    {
        var result = H2SectionExtractor.Extract(RealisticHtml);

        Assert.DoesNotContain(result.Sections, section => section.Heading.Contains("Frill Knit Set"));
    }

    [Fact]
    public void KeepsOriginalHeadingAndAddsNormalizedHeading()
    {
        var result = H2SectionExtractor.Extract(RealisticHtml);

        var updateSection = result.Sections[1];
        Assert.Equal("◈アップデート履歴◈", updateSection.Heading);
        Assert.Equal("アップデート履歴", updateSection.NormalizedHeading);
    }

    [Fact]
    public void ReportsDescriptionBodyPresentWhenContainerHasText()
    {
        var result = H2SectionExtractor.Extract(RealisticHtml);

        Assert.True(result.HasDescriptionBody);
        Assert.NotNull(result.DescriptionHtml);
    }

    /// <summary>
    /// 説明はあるのにセクションが取れない状態。実測では25件中6件がこの形だった。
    /// この状態が急増した時はBOOTH側の構造変化を疑う手掛かりになる。
    /// </summary>
    [Fact]
    public void ReportsBodyWithoutSectionsWhenDescriptionIsOneBlock()
    {
        const string html = """
            <section class="main-info-column">
              <div class="js-market-item-detail-description description">
                <p class="autolink">セクション分けをしていない説明文です。</p>
              </div>
            </section>
            """;

        var result = H2SectionExtractor.Extract(html);

        Assert.Empty(result.Sections);
        Assert.True(result.HasDescriptionBody);
    }

    /// <summary>説明の列が取れない構造になっても、セクション自体は文書全体から拾えること。</summary>
    [Fact]
    public void FallsBackToWholeDocumentWhenColumnIsMissing()
    {
        const string html = """
            <div class="something-else">
              <section class="shop__text">
                <h2>利用規約</h2>
                <p>再配布は禁止です。</p>
              </section>
            </div>
            """;

        var result = H2SectionExtractor.Extract(html);

        Assert.Single(result.Sections);
        Assert.Equal("利用規約", result.Sections[0].Heading);
    }

    /// <summary>表示用HTMLには説明とセクションだけを入れる（価格表示などを抱き込まない）。</summary>
    [Fact]
    public void BuildsDisplayHtmlFromDescriptionAndSectionsOnly()
    {
        const string html = """
            <section class="main-info-column">
              <div class="js-market-item-detail-description description"><p>説明本文</p></div>
              <section class="shop__text"><h2>利用規約</h2><p>再配布は禁止です。</p></section>
              <div class="price-box">¥2,500</div>
            </section>
            """;

        var result = H2SectionExtractor.Extract(html);

        Assert.NotNull(result.DescriptionHtml);
        Assert.Contains("説明本文", result.DescriptionHtml);
        Assert.Contains("利用規約", result.DescriptionHtml);
        Assert.DoesNotContain("price-box", result.DescriptionHtml);
    }

    [Fact]
    public void ReturnsNothingForEmptyHtml()
    {
        var result = H2SectionExtractor.Extract(string.Empty);

        Assert.Empty(result.Sections);
        Assert.False(result.HasDescriptionBody);
    }

    [Theory]
    [InlineData("◈アップデート履歴◈", "アップデート履歴")]
    [InlineData("＋更新履歴＋", "更新履歴")]
    [InlineData("● 内容物 ●", "内容物")]
    [InlineData("■商品説明", "商品説明")]
    [InlineData("▶導入方法◀", "導入方法")]
    [InlineData("▌利用規約", "利用規約")]
    [InlineData("【あらすじ】", "あらすじ")]
    [InlineData("使用方法：", "使用方法")]
    [InlineData("＋Avatar Modify Support License＋", "Avatar Modify Support License")]
    [InlineData("", "")]
    public void NormalizeHeadingStripsDecorationFromBothEnds(string heading, string expected)
    {
        Assert.Equal(expected, H2SectionExtractor.NormalizeHeading(heading));
    }

    /// <summary>実測で見つかった4通りの表記ゆれを、装飾付きのまま判定できること。</summary>
    [Theory]
    [InlineData("アップデート履歴")]
    [InlineData("◈アップデート履歴◈")]
    [InlineData("＋更新履歴＋")]
    [InlineData("更新情報")]
    [InlineData("アップデート情報")]
    [InlineData("Update History")]
    [InlineData("Changelog")]
    public void DetectsUpdateHistoryHeadings(string heading)
    {
        Assert.True(H2SectionExtractor.IsUpdateHistoryHeading(heading));
    }

    [Theory]
    [InlineData("利用規約")]
    [InlineData("● 内容物 ●")]
    [InlineData("前提条件")]
    [InlineData("")]
    public void DoesNotTreatOtherHeadingsAsUpdateHistory(string heading)
    {
        Assert.False(H2SectionExtractor.IsUpdateHistoryHeading(heading));
    }
}
