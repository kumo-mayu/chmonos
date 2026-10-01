using Chmonos.Core.Resolution;
using Xunit;

namespace Chmonos.Core.Tests;

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

    /// <summary>
    /// AND 検索を全滅させていた余計な語を落とす（正解の分かる319本の外れから）。
    /// 版番号の残り・括弧の付け足し・#タグ・作者の略号・ダウンロードの重複番号・同梱物の種類。
    /// </summary>
    [Theory]
    [InlineData("pochio_v1.3.2_update (1).zip", "pochio")]
    [InlineData("mynail_texture_MikaFuwa_ver.1.20.zip", "mynail Mika Fuwa")]
    [InlineData("PuniNail【VRネイルチップ】【Tinydoll対応】#ぷにらぼ.zip", "Puni Nail")]
    [InlineData("MA_Goggles_Ver_2_update-20240923.zip", "Goggles")]
    [InlineData("A_LegStraight_v1.2.1.zip", "Leg Straight")]
    [InlineData("Dragonfly_1.02_Amber.zip", "Dragonfly Amber")]
    [InlineData("星空アイ_Marycia_ver.1.2.zip", "星空アイ Marycia")]
    [InlineData("やわらか影システム 9.2(for Avatar) PCSS For VRC .zip", "やわらか影システム PCSS")]
    public void DropsWordsThatBreakAnAndSearch(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    /// <summary>
    /// 英単語の尻に続けて書かれた番号と、同じ商品の中の分け方の語（2026-09-29 の再調整）。
    /// 型番（全部大文字）と、番号の後にも字が続く名前は割らない。
    /// </summary>
    [Theory]
    [InlineData("SampleCape2_Mat_Tex_v1.0.zip", "Sample Cape")]
    [InlineData("WidgetStand12b.zip", "Widget Stand")]
    [InlineData("SampleBow_All_Ver1.00.zip", "Sample Bow")]
    [InlineData("Common_SampleBow.zip", "Sample Bow")]
    [InlineData("SampleCape_おまけ.zip", "Sample Cape")]
    [InlineData("shop7xy_Thing.zip", "shop7xy Thing")]
    [InlineData("ABC123_Thing.zip", "ABC123 Thing")]
    public void SplitsNumbersOffWordsAndDropsVariantWords(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    /// <summary>割った番号もシリーズの番号としては残る（並べ直しと点数に使う）。</summary>
    [Fact]
    public void KeepsTheSplitNumberAsASeriesNumber()
    {
        Assert.Equal(["2"], FileNameQuery.SeriesNumbers("SampleCape2_Mat.zip"));
    }

    /// <summary>日本語と英数字の境目で分ける。続けて書かれていると、BOOTHはそれを1語として探す。</summary>
    [Fact]
    public void SplitsBetweenJapaneseAndLatin()
    {
        Assert.Equal("撫 mofu nade controller", FileNameQuery.ToSearchQuery("撫mofu_nade_controller.zip"));
    }

    /// <summary>
    /// 「」は商品名を括っていることが多いので、中身は残す。括弧だけで名前ができている場合も同じ。
    /// 「3Dモデル」の 3D は日本語との境目で分かれ、版番号の形（数字＋英字1字）として落ちる。
    /// 一般的な語なので検索には効かない
    /// </summary>
    [Theory]
    [InlineData("3Dモデル ネックレス「LuneBlanc」.zip", "モデル ネックレス Lune Blanc")]
    [InlineData("【くうた】.zip", "くうた")]
    public void KeepsWhatQuotesAndLoneBracketsHold(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(fileName));
    }

    /// <summary>重複語は潰さない。試作で「Fuwari Fuwari」を1語にしたら当たらなくなった。</summary>
    [Fact]
    public void KeepsRepeatedWords()
    {
        Assert.Equal("Fuwari Fuwari", FileNameQuery.ToSearchQuery("Fuwari_Fuwari_v1.0.zip"));
    }

    /// <summary>引き直しに使う1語。語が2つ以上なら最初の語、短ければ日本語は倍に数えて、いちばん長いもの。1語だけなら引き直さない。</summary>
    [Theory]
    [InlineData("F_撫で音ギミック4_00_Basic.zip", "撫で音ギミック")]
    [InlineData("HeartBeatGimmick_v3.0.3.zip", "")]
    [InlineData("Braid_Caramel.zip", "Braid")]
    [InlineData("Ribbon_ver2_Caramel_Mint.zip", "Ribbon")]
    [InlineData("AB_Ribbon_Caramel.zip", "Ribbon")]
    [InlineData("Bow_Caramel.zip", "Caramel")]
    public void PicksTheMostDistinctiveToken(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.MostDistinctiveToken(fileName));
    }

    /// <summary>語に直接付いた番号を拾う。版番号の各桁は拾わない。</summary>
    [Theory]
    [InlineData("練習用ポーズ集13.zip", new[] { "13" })]
    [InlineData("光のヘイロー012　天使の羽.zip", new[] { "012" })]
    [InlineData("Kipfel_1.2.0.zip", new string[0])]
    [InlineData("Wendy_ver1.01.zip", new string[0])]
    public void PicksUpSeriesNumbers(string fileName, string[] expected)
    {
        Assert.Equal(expected, FileNameQuery.SeriesNumbers(fileName));
    }

    /// <summary>名前の下書きは控えめに：重複番号・配布形態の語・版番号だけ落とし、同梱物の種類は残す。</summary>
    [Theory]
    [InlineData("pochio_v1.3.2_update (1).zip", "pochio")]
    [InlineData("Marycia_texture.zip", "Marycia_texture")]
    [InlineData("Kipfel_1.2.0.zip", "Kipfel")]
    public void DraftsANameConservatively(string fileName, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToNameDraft(fileName));
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

/// <summary>
/// フォルダ名からの検索語。展開物の中身はファイル名では商品に辿り着かないので、
/// 展開元とみなしたフォルダの名前で引く。
/// </summary>
public class FolderNameQueryTests
{
    [Theory]
    [InlineData(@"D:\dl\rurune_v1.1.3", "rurune")]
    [InlineData(@"D:\dl\rurune_v1.1.3\rurune", "rurune")]
    [InlineData(@"D:\dl\Kipfel_1.2.0", "Kipfel")]
    [InlineData(@"D:\dl\hotogiya_Kuuta_ver1.03", "hotogiya Kuuta")]
    public void BuildsAQueryFromAFolderName(string folder, string expected)
    {
        Assert.Equal(expected, FileNameQuery.ToSearchQuery(folder));
    }

    /// <summary>
    /// 展開物の中身をファイル名で引いても意味が無いことの確認。
    /// この結果になるからこそ、検索対象をフォルダへ切り替えている。
    /// </summary>
    [Fact]
    public void AContentFileNameDoesNotIdentifyTheProduct()
    {
        Assert.Equal("cloth", FileNameQuery.ToSearchQuery(@"D:\dl\rurune_v1.1.3\rurune\texture\cloth.psd"));
    }
}
