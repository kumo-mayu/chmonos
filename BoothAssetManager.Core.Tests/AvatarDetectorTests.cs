using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class AvatarTextTests
{
    /// <summary>NFKCが装飾英字と全角を素に戻す。実測では装飾除去より効果が大きかった。</summary>
    [Theory]
    [InlineData("𝐸𝑣𝑒𝑟𝐴𝑓𝑡𝑒𝑟", "everafter")]
    [InlineData("ＫＵＵＴＡ", "kuuta")]
    [InlineData("✧しなの対応✧", "しなの対応")]
    [InlineData("【くうた対応】", "くうた対応")]
    [InlineData("𝐒𝐚𝐡𝐤𝐞𝐭(さふけっと)", "sahketさふけっと")]
    public void NormalizeStripsDecoration(string input, string expected)
        => Assert.Equal(expected, AvatarText.Normalize(input));

    /// <summary>
    /// 長音符は装飾ではない。Unicode上は Script=Common で記号に見えるが、
    /// カタカナ名の内部にあるので落とすと名前が壊れる。
    /// </summary>
    [Theory]
    [InlineData("セーラー")]
    [InlineData("ルーナリット")]
    [InlineData("スーパーヒーロー")]
    public void NormalizeKeepsProlongedSoundMark(string input)
        => Assert.Equal(input.ToLowerInvariant(), AvatarText.Normalize(input));

    /// <summary>
    /// 片割れのサロゲート（絵文字の半分）が混ざっても例外にしない。
    /// 放っておくと、1件の商品のせいで検出全体が止まる。
    /// </summary>
    [Fact]
    public void NormalizeSurvivesBrokenSurrogates()
        => Assert.Equal("マヌカ", AvatarText.Normalize("\uDC8Eマヌカ\uD83D"));

    [Theory]
    [InlineData("くうた対応", "くうた")]
    [InlineData("マヌカ用", "マヌカ")]
    [InlineData("kuuta対応版", "kuuta")]
    [InlineData("しなの専用", "しなの")]
    [InlineData("くうた", "くうた")]
    public void StripsSupportSuffix(string input, string expected)
        => Assert.Equal(AvatarText.Normalize(expected), AvatarText.StripSupportSuffix(input));

    [Theory]
    [InlineData("珍飯亭共通素体", "珍飯亭")]
    [InlineData("えも研素体対応", "えも研")]
    [InlineData("まめふれんず共通素体", "まめふれんず")]
    [InlineData("AVAKIN共通素体", "AVAKIN")]
    public void ExtractsBaseName(string tag, string expected)
        => Assert.Equal(expected, AvatarText.ExtractBaseName(tag));

    /// <summary>「共通素体」だけではどのグループか分からないので拾わない。</summary>
    [Theory]
    [InlineData("共通素体")]
    [InlineData("素体")]
    [InlineData("素体付き")]
    [InlineData("衣装")]
    public void IgnoresBarePhrases(string tag)
        => Assert.Null(AvatarText.ExtractBaseName(tag));
}

public class AvatarDetectorTests
{
    private static readonly string[] Support = ["対応アバター", "対応モデル"];
    private static readonly string[] Ignored = ["クレジット", "使用素材", "利用規約", "更新"];

    /// <summary>
    /// 「対応アバター」節はURLが &lt;a&gt; ではなくプレーンテキストで置かれている（実測）。
    /// 名前とIDが同じ行に並ぶので両方取れる。
    /// </summary>
    [Fact]
    public void ReadsPlainUrlsFromSupportSection()
    {
        var html = """
            <h2>⚘ 対応アバター ⚘</h2>
            <p>キプフェル Kipfel https://mukumi.booth.pm/items/5813187<br>
            まめひなた https://mukumi.booth.pm/items/4340548</p>
            """;

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.True(scan.HasSupportHeading);
        Assert.Equal(["5813187", "4340548"], scan.Support.Select(hit => hit.ItemId));
        Assert.Equal("キプフェル Kipfel", scan.Support[0].NameHint);
    }

    /// <summary>
    /// クレジット節は読まない。宣伝画像に使ったモデルへの謝辞で、
    /// 実測では34件中0件しかアバターではなかった。
    /// </summary>
    [Fact]
    public void IgnoresCreditSections()
    {
        var html = """
            <h2>対応アバター</h2><p>https://booth.pm/ja/items/1111</p>
            <h2>クレジット</h2><p>https://booth.pm/ja/items/2222</p>
            <h2>使用素材</h2><p>https://booth.pm/ja/items/3333</p>
            """;

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.Equal(["1111"], scan.Support.Select(hit => hit.ItemId));
        Assert.Empty(scan.Other);
    }

    /// <summary>その他の見出しの下のリンクは拾うが、弱い信号として分けて返す。</summary>
    [Fact]
    public void SeparatesOtherSections()
    {
        var html = """
            <h2>対応アバター</h2><p>https://booth.pm/ja/items/1111</p>
            <h2>ギミックについて</h2><p>https://booth.pm/ja/items/2222</p>
            """;

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.Equal(["1111"], scan.Support.Select(hit => hit.ItemId));
        Assert.Equal(["2222"], scan.Other);
    }

    /// <summary>最初の見出しより前にも対応表明が置かれることがある。</summary>
    [Fact]
    public void ReadsTextBeforeFirstHeading()
    {
        var html = """
            <p>くうた対応 https://booth.pm/ja/items/4897493</p>
            <h2>利用規約</h2><p>https://booth.pm/ja/items/3087170</p>
            """;

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.Equal(["4897493"], scan.Other);
        Assert.False(scan.HasSupportHeading);
    }

    [Fact]
    public void SkipsSelfReference()
    {
        var html = "<h2>対応アバター</h2><p>https://booth.pm/ja/items/999</p>";

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.Empty(scan.Support);
    }

    /// <summary>対応節で拾えたIDは、弱い側に重複して出さない。</summary>
    [Fact]
    public void DoesNotDuplicateAcrossBuckets()
    {
        var html = """
            <h2>対応アバター</h2><p>https://booth.pm/ja/items/1111</p>
            <h2>おまけ</h2><p>https://booth.pm/ja/items/1111</p>
            """;

        var scan = AvatarDetector.ScanDescription(html, "999", Support, Ignored);

        Assert.Single(scan.Support);
        Assert.Empty(scan.Other);
    }

    [Fact]
    public void FindsBaseNamesFromTags()
    {
        var names = AvatarDetector.ScanBaseTags(["VRChat", "珍飯亭共通素体対応", "衣装", "えも研素体"]);

        Assert.Equal(["珍飯亭", "えも研"], names);
    }

    private static AvatarNameIndex IndexOf(params (string Id, string Name, string[] Aliases)[] avatars)
        => AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries = avatars.Select(a => new AvatarRegistryEntry
            {
                ItemId = a.Id,
                DisplayName = a.Name,
                Category = "3Dキャラクター",
                Aliases = a.Aliases.Select(text => new AvatarAlias { Text = text }).ToList(),
            }).ToList(),
        });

    /// <summary>タグの接尾辞を落として引く。「くうた対応」→ くうた。</summary>
    [Fact]
    public void MatchesTagsAfterStrippingSuffix()
    {
        var index = IndexOf(("4897493", "くうた", ["くうた", "kuuta3d"]));

        var (fromTags, _) = AvatarDetector.ScanNames(index, ["VRChat", "くうた対応"], []);

        Assert.Equal(["4897493"], fromTags);
    }

    /// <summary>variation名は装飾込みなので含有で引く。</summary>
    [Fact]
    public void MatchesVariationNamesWithDecoration()
    {
        var index = IndexOf(("1", "しなの", ["しなの"]), ("2", "マヌカ", ["マヌカ"]));

        var (_, fromVariations) = AvatarDetector.ScanNames(index, [], ["✧Fullpack✧", "✧しなの対応✧", "✧マヌカ対応✧"]);

        Assert.Equal(["1", "2"], fromVariations.OrderBy(x => x));
    }

    /// <summary>タグで既に取れたアバターは、variation側で重複して出さない。</summary>
    [Fact]
    public void DoesNotRepeatTagMatchesInVariations()
    {
        var index = IndexOf(("1", "しなの", ["しなの"]));

        var (fromTags, fromVariations) = AvatarDetector.ScanNames(index, ["しなの対応"], ["しなの版"]);

        Assert.Single(fromTags);
        Assert.Empty(fromVariations);
    }

    /// <summary>
    /// タグは完全一致で引く。含んでいるかで引くと別のアバターに当たる
    /// （所持207件の実データで「ミルティナ」が「ティナ」「ルティ」に当たっていた）。
    /// </summary>
    [Fact]
    public void DoesNotMatchTagsThatOnlyContainAName()
    {
        var index = IndexOf(("tina", "ティナ", ["ティナ"]), ("milltina", "ミルティナ", ["ミルティナ"]));

        var (fromTags, _) = AvatarDetector.ScanNames(index, ["ミルティナ対応"], []);

        Assert.Equal(["milltina"], fromTags);
    }

    /// <summary>敬称も落として比べる。「しなのちゃん対応」は しなの。</summary>
    [Fact]
    public void StripsHonorificsFromTags()
    {
        var index = IndexOf(("1", "しなの", ["しなの"]));

        var (fromTags, _) = AvatarDetector.ScanNames(index, ["しなのちゃん対応"], []);

        Assert.Equal(["1"], fromTags);
    }

    /// <summary>種類名でも、より長い別のアバター名の一部としてしか出てこないものは数えない。</summary>
    [Fact]
    public void IgnoresNamesInsideLongerNamesInVariations()
    {
        var index = IndexOf(("tina", "ティナ", ["ティナ"]), ("milltina", "ミルティナ", ["ミルティナ"]));

        var (_, fromVariations) = AvatarDetector.ScanNames(index, [], ["[ミルティナ] Milltina"]);

        Assert.Equal(["milltina"], fromVariations);
    }

    /// <summary>英字の名前が単語の途中に埋まっているだけのものは数えない（Satellite の中の tell）。</summary>
    [Fact]
    public void IgnoresLatinNamesInsideWords()
    {
        var index = IndexOf(("tell", "Tell", ["Tell"]));

        Assert.Empty(index.FindAvatars("Satellite-Beam Rod"));
        Assert.Equal(["tell"], index.FindAvatars("for Tell"));
    }

    /// <summary>
    /// 覚えた別名は、自分の正式名に出てくるものだけを使う。部分一致で覚えてしまった
    /// 「ミルティナ対応」のような別名で、別のアバターに当たり続けないため。
    /// 手で足した別名は利用者の意思なので、正式名に出てこなくても使う。
    /// </summary>
    [Fact]
    public void UsesOnlyLearnedAliasesThatAppearInTheOwnName()
    {
        var index = AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "tina",
                    BoothName = "オリジナル3Dモデル「ティナ」",
                    DisplayName = "ティナ",
                    Category = "3Dキャラクター",
                    Aliases =
                    [
                        new AvatarAlias { Text = "ミルティナ対応", Source = nameof(AvatarLinkSource.Tag) },
                        new AvatarAlias { Text = "Tina", Source = nameof(AvatarLinkSource.Manual) },
                    ],
                },
            ],
        });

        Assert.Empty(index.FindExact("ミルティナ対応"));
        Assert.Equal(["tina"], index.FindExact("Tina"));
    }

    /// <summary>正式名の「」の中も呼び名にする。表示名や別名に無くても引ける。</summary>
    [Fact]
    public void UsesTheQuotedPartOfTheBoothName()
    {
        var index = AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = "rurune",
                    BoothName = "サメっ子オリジナル3Dモデル「rurune」-ルルネ-",
                    Category = "3Dキャラクター",
                },
            ],
        });

        Assert.Equal(["rurune"], index.FindExact("Rurune"));
    }

    /// <summary>1文字の別名は何にでも当たるので索引に入れない。</summary>
    [Fact]
    public void IgnoresSingleCharacterAliases()
    {
        var index = IndexOf(("1", "あ", ["あ"]));

        Assert.Empty(index.FindAvatars("あいうえお"));
    }
}

public class AvatarNameShorteningTests
{
    /// <summary>
    /// 表示名はBOOTHの正式名そのままだと読めない。
    /// 商品名の中に現れるタグ（＝別名）のうち最も短いものが呼び名になる。
    /// </summary>
    [Fact]
    public void PrefersTheShortestAliasFoundInTheName()
    {
        var name = "【くうた-Kuuta-】オリジナル3Dモデル #Kuuta3D";

        Assert.Equal("くうた", AvatarText.ShortenName(name, ["kuuta3d", "くうた", "くうた対応"]));
    }

    /// <summary>商品名に現れない別名は採らない（他の商品から覚えた表記が混ざるため）。</summary>
    [Fact]
    public void IgnoresAliasesNotInTheName()
    {
        Assert.Equal("マヌカ", AvatarText.ShortenName("【マヌカ】オリジナル3Dモデル", ["まめひなた", "マヌカ"]));
    }

    /// <summary>別名が無ければ先頭の【…】を採る。</summary>
    [Fact]
    public void FallsBackToTheLeadingBracket()
    {
        Assert.Equal("ミルフィ Milfy", AvatarText.ShortenName("【ミルフィ Milfy】オリジナル3Dモデル"));
    }

    /// <summary>「【無料】lilToon」を「無料」と呼んでしまわない。</summary>
    [Theory]
    [InlineData("【無料】lilToon", "lilToon")]
    [InlineData("【期間限定】【セール】あるアバター", "あるアバター")]
    [InlineData("【VRChat想定】【マヌカ】衣装", "マヌカ")]
    public void SkipsMarketingBrackets(string name, string expected)
        => Assert.Equal(expected, AvatarText.ShortenName(name));

    /// <summary>
    /// 「」『』で括られた語が名前そのもの。ここを先に見ないと、
    /// 短いだけのタグ（「サメ」）を名前にしてしまう。
    /// </summary>
    [Theory]
    [InlineData("サメっ子オリジナル3Dモデル「rurune」-ルルネ-", "rurune")]
    [InlineData("オリジナル3Dモデル『Bird/鳥』", "Bird/鳥")]
    public void PrefersQuotedNames(string name, string expected)
        => Assert.Equal(expected, AvatarText.ShortenName(name, ["サメ", "3D"]));

    [Fact]
    public void KeepsPlainNamesAsIs()
        => Assert.Equal("ミルフィ Milfy / オリジナル3Dモデル",
            AvatarText.ShortenName("ミルフィ Milfy / オリジナル3Dモデル"));

    /// <summary>頭文字は飾り記号を飛ばす。そのままだと「【」ばかり並ぶ。</summary>
    [Theory]
    [InlineData("【くうた】", "く")]
    [InlineData("✿マヌカ✿", "マ")]
    [InlineData("Kipfel", "K")]
    [InlineData("", "?")]
    [InlineData("★☆♪", "?")]
    public void TakesTheFirstRealCharacterAsInitial(string input, string expected)
        => Assert.Equal(expected, AvatarText.InitialOf(input));
}
