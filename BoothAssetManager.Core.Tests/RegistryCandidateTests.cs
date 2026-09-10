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
                Aliases = [new AvatarAlias { Text = "くうた", Count = 1 }],
            })
            .ToList();

        Assert.Equal(3, RegistryCandidates.For(@"C:\dl\くうた_衣装.zip", many).Count);
    }
}
