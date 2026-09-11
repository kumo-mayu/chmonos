using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 手元のアバター登録簿から候補を出す。**通信は増えない。**
///
/// 登録簿は未所持の商品の名前まで持っている（実データでは13件中6件が未所持）。
/// BOOTHが404を返すファイルでも、名前から辿り着けることがある。
/// </summary>
public class RegistryCandidateTests
{
    private static AvatarRegistryEntry Kuuta(params string[] aliases) => new()
    {
        ItemId = "4897493",
        DisplayName = "くうた",
        BoothName = "【くうた-Kuuta-】オリジナル3Dモデル",
        Category = "3Dキャラクター",
        Aliases = aliases.Select(text => new AvatarAlias { Text = text, Count = 1 }).ToList(),
    };

    [Fact]
    public void FindsTheAvatarByItsShortName()
    {
        var found = RegistryCandidates.For(@"C:\dl\くうた_衣装_v1.zip", [Kuuta()]);

        var candidate = Assert.Single(found);
        Assert.Equal("4897493", candidate.ItemId);
        Assert.Equal("くうた", candidate.Name);
        Assert.Equal("くうた", candidate.MatchedOn);
    }

    /// <summary>別名は「くうた君対応」のような、まさにファイル名に現れる形で溜まっている。</summary>
    [Fact]
    public void FindsItByAnAliasThatAppearsInFileNames()
    {
        // 表示名でもBOOTHの正式名でも当たらないものが、別名でだけ当たる
        var entry = Kuuta("くうた君対応") with { DisplayName = "Kuuta3D", BoothName = null };

        var found = RegistryCandidates.For(@"C:\dl\【くうた君対応】School sweater.zip", [entry]);

        Assert.Equal("くうた君対応", Assert.Single(found).MatchedOn);
    }

    /// <summary>商品IDが書いてあればそれ以上の証拠は無い。名前の一致より先に出す。</summary>
    [Fact]
    public void PutsAnIdMatchFirst()
    {
        var other = new AvatarRegistryEntry
        {
            ItemId = "1111111",
            DisplayName = "4897493",
        };

        var found = RegistryCandidates.For(@"C:\dl\item_4897493_outfit.zip", [other, Kuuta()]);

        Assert.Equal("4897493", found[0].ItemId);
    }

    /// <summary>
    /// ローマ字のファイル名も読みで当てる。
    /// <c>hotogiya_Kuuta_ver1.03.zip</c> はBOOTH検索では拾えなかった組。
    /// </summary>
    [Fact]
    public void MatchesARomajiFileNameThroughTheReading()
    {
        var readings = new KanjiReadings(
            Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz"));
        var bridge = new SearchBridge(new JapaneseDictionary(
            Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz"),
            Path.Combine(Path.GetTempPath(), "registry-candidate-bridge.cache")));

        var found = RegistryCandidates.For(
            @"C:\dl\hotogiya_Kuuta_ver1.03.zip", [Kuuta()], bridge, readings);

        Assert.Equal("4897493", Assert.Single(found).ItemId);
    }

    /// <summary>
    /// 名前そのものが短い項目には、2文字の読みでも当てる。
    /// 実データで Tori_v1_0_0.zip → 『Bird/鳥』、Eku_PC_v1_0_0.zip → エク が正解だった。
    /// </summary>
    [Theory]
    [InlineData(@"C:\dl\Tori_v1_0_0.zip", "Bird/鳥")]
    [InlineData(@"C:\dl\Eku_PC_v1_0_0.zip", "エク")]
    public void MatchesAShortNameThroughATwoLetterReading(string file, string displayName)
    {
        var readings = new KanjiReadings(
            Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz"));
        var bridge = new SearchBridge(new JapaneseDictionary(
            Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz"),
            Path.Combine(Path.GetTempPath(), "registry-candidate-bridge.cache")));
        var entry = new AvatarRegistryEntry { ItemId = "6", DisplayName = displayName, Category = "3Dキャラクター" };

        Assert.Equal("6", Assert.Single(RegistryCandidates.For(file, [entry], bridge, readings)).ItemId);
    }

    /// <summary>
    /// BOOTHから一度も取れていない項目には印を付ける。
    /// 404でも項目は作られるので（categoryがnullのまま）、そこが手掛かりになる。
    /// </summary>
    [Fact]
    public void MarksEntriesBoothNeverAnsweredFor()
    {
        var gone = Kuuta() with { Category = null, CheckedAt = DateTimeOffset.Now };

        Assert.True(RegistryCandidates.For(@"C:\dl\くうた_衣装.zip", [gone])[0].NeverFetched);
        Assert.False(RegistryCandidates.For(@"C:\dl\くうた_衣装.zip", [Kuuta()])[0].NeverFetched);
    }

    /// <summary>当たらないものは出さない。1文字の一致で拾わないこと。</summary>
    [Fact]
    public void SaysNothingWhenNothingMatches()
    {
        var single = new AvatarRegistryEntry { ItemId = "222", DisplayName = "あ" };

        Assert.Empty(RegistryCandidates.For(@"C:\dl\まったく別の商品.zip", [Kuuta(), single]));
    }

    [Fact]
    public void HandlesAnEmptyRegistry()
    {
        Assert.Empty(RegistryCandidates.For(@"C:\dl\くうた.zip", []));
    }

    /// <summary>並べすぎると選べないので3件まで。</summary>
    [Fact]
    public void StopsAtThreeCandidates()
    {
        var many = Enumerable.Range(0, 6)
            .Select(index => new AvatarRegistryEntry
            {
                ItemId = $"90000{index}",
                DisplayName = $"くうた{index}",
            })
            .ToList();

        Assert.Equal(3, RegistryCandidates.For(@"C:\dl\くうた0_くうた1_くうた2_くうた3_くうた4_くうた5.zip", many).Count);
    }

    /// <summary>
    /// 登録簿の2項目以上が持っている表記では当てない。どれを指しているか決められず、
    /// 実データでは「シェーダー」「ポーズ」がそれぞれ3項目に付いていて、外ればかりを3件並べていた。
    /// </summary>
    [Fact]
    public void IgnoresATextSharedByTwoEntries()
    {
        var shaderA = new AvatarRegistryEntry { ItemId = "1", DisplayName = "水シェーダー", Aliases = [new AvatarAlias { Text = "シェーダー", Count = 1 }] };
        var shaderB = new AvatarRegistryEntry { ItemId = "2", DisplayName = "目シェーダー", Aliases = [new AvatarAlias { Text = "シェーダー", Count = 1 }] };

        Assert.Empty(RegistryCandidates.For(@"C:\dl\新しいシェーダー.zip", [shaderA, shaderB]));
    }

    /// <summary>短いラテン文字と、語の途中での一致では当てない（「VR」が VRChat に、「Nemo」が Nemoria に当たっていた）。</summary>
    [Theory]
    [InlineData("VR", @"C:\dl\VRネイルチップ.zip")]
    [InlineData("Nemo", @"C:\dl\Accessory featuring Nemoria flowers.zip")]
    public void IgnoresShortOrPartialLatinMatches(string alias, string file)
    {
        var entry = new AvatarRegistryEntry { ItemId = "3", DisplayName = "どこかのアバター", Aliases = [new AvatarAlias { Text = alias, Count = 1 }] };

        Assert.Empty(RegistryCandidates.For(file, [entry]));
    }

    /// <summary>ラテン文字の名前も、語として現れていれば当てる。</summary>
    [Fact]
    public void MatchesALatinNameAsAWord()
    {
        var entry = new AvatarRegistryEntry { ItemId = "4", DisplayName = "もふ子", Aliases = [new AvatarAlias { Text = "Mofuko", Count = 1 }] };

        Assert.Equal("4", Assert.Single(RegistryCandidates.For(@"C:\dl\Mofuko_PSD.zip", [entry])).ItemId);
    }

    /// <summary>一般的な語（天使・アバター）は別名に紛れ込んでいても使わない。</summary>
    [Fact]
    public void IgnoresGenericWords()
    {
        var entry = new AvatarRegistryEntry { ItemId = "5", DisplayName = "ぷまちゃん", Aliases = [new AvatarAlias { Text = "天使", Count = 1 }] };

        Assert.Empty(RegistryCandidates.For(@"C:\dl\光のヘイロー012　天使の羽.zip", [entry]));
    }
}
