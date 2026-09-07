using BoothAssetManager.Core.Resolution;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class FileNameQueryTests
{
    /// <summary>実際の配布ファイル名から、検索に使える語が取れること。</summary>
    [Theory]
    [InlineData("Kipfel_1.2.0.zip", "Kipfel")]
    [InlineData("Wendy_ver1.01.zip", "Wendy")]
    [InlineData("Tori_v1_1_1.zip", "Tori")]
    [InlineData("FREYSIA.101.zip", "FREYSIA")]
    [InlineData("Milfy_v1.5.0.zip", "Milfy")]
    [InlineData("HeartBeatGimmick_v3.0.3.zip", "Heart Beat Gimmick")]
    public void StripsTrailingVersionFromFileName(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    /// <summary>
    /// 配布形態を表すだけの語は検索の邪魔になるので落とす。
    /// ただし落とすのは語まるごとの場合だけで、「ShapekeyAddon」のように
    /// 商品名と繋がっている場合は切り離さない（切ると別の語を壊すため）。
    /// </summary>
    [Theory]
    [InlineData("SDN_Fullpack1.0.zip", "SDN")]
    [InlineData("Something_Append.zip", "Something")]
    [InlineData("Kuuta_ShapekeyAddon.zip", "Kuuta Shapekey Addon")]
    public void DropsDistributionNoiseTokens(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    /// <summary>
    /// 続けて書かれた英単語は分かち書きにする。
    /// 実測で「SinAvatarPen」のまま検索すると0件だったが、実際の商品名は
    /// 「真・アバターペンシステム Sin Avatar Pen System」と語が分かれている。
    /// </summary>
    [Theory]
    [InlineData("SinAvatarPen_v1.2.2.zip", "Sin Avatar Pen")]
    [InlineData("HeartBeatGimmick_v3.0.3.zip", "Heart Beat Gimmick")]
    [InlineData("FREYSIA.101.zip", "FREYSIA")]
    public void SplitsRunTogetherEnglishWords(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    [Fact]
    public void KeepsJapaneseNames()
    {
        Assert.Equal("撫で音ギミック", FileNameQuery.ToSearchQuery("撫で音ギミック1_01.zip"));
    }

    [Fact]
    public void ReturnsEmptyForEmptyInput()
    {
        Assert.Equal(string.Empty, FileNameQuery.ToSearchQuery(string.Empty));
    }

    /// <summary>
    /// 語境界での照合。単純な部分一致だと「Tori」が「Mistoria」にまで当たり、
    /// 実測で誤った候補が上位に来てしまった。
    /// </summary>
    [Theory]
    [InlineData("【オリジナル3Dモデル】Wendy -ウェンディ-", "Wendy", true)]
    [InlineData("キプフェル Kipfel / オリジナル3Dモデル", "Kipfel", true)]
    [InlineData("FREYSIA💍No.101", "FREYSIA", true)]
    [InlineData("✨発売記念【18アバター対応】Mistoria✨", "Tori", false)]
    [InlineData("🚧VECTORIAL STRUCT", "Tori", false)]
    [InlineData("Victorian Whiskers Maid", "Tori", false)]
    public void MatchesLatinWordsOnWordBoundaries(string itemName, string query, bool expected)
    {
        Assert.Equal(expected, FileNameQuery.LooksRelated(itemName, query));
    }

    /// <summary>日本語には語の区切りが無いので、部分一致で判定する。</summary>
    [Fact]
    public void MatchesJapaneseBySubstring()
    {
        Assert.True(FileNameQuery.LooksRelated("【フカさんの！】撫で音ギミック【VRChat】", "撫で音ギミック"));
    }

    /// <summary>番号だけが違う商品を区別するために、3桁以上の数字を拾う。</summary>
    [Fact]
    public void PicksUpSignificantNumbers()
    {
        Assert.Equal(["101"], FileNameQuery.SignificantNumbers("FREYSIA.101.zip"));
    }

    /// <summary>版番号（1.2.0 など）は商品の区別に使えないので拾わない。</summary>
    [Theory]
    [InlineData("Kipfel_1.2.0.zip")]
    [InlineData("Wendy_ver1.01.zip")]
    public void IgnoresVersionLikeNumbers(string fileName)
    {
        Assert.Empty(FileNameQuery.SignificantNumbers(fileName));
    }
}
