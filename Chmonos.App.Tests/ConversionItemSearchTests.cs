using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 組を探す欄の語と探す所を、プロファイルの商品の一覧にも当てる（ユーザ判断 2026-11-20・②）。
/// つなぎ先は向きで元（逆方向）と先（順方向）に分け、両方向と向きの分からない物は両方。外したつなぎ先は当てない。
/// 商品名・商品 ID は「両方」の時だけ当てる。名前・ID は作り物。
/// </summary>
public class ConversionItemSearchTests
{
    private const string Aoi = "9800601";
    private const string Momo = "9800602";

    private static AvatarRegistryEntry Avatar(string id, string name, string? alias = null) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」正式版",
        DisplayName = name,
        Category = "3Dキャラクター",
        Aliases = alias is null ? [] : [new AvatarAlias { Text = alias }],
    };

    private static readonly Dictionary<string, AvatarRegistryEntry> Entries = new()
    {
        [Aoi] = Avatar(Aoi, "アオイ", alias: "Aoi-chan"),
        [Momo] = Avatar(Momo, "モモ"),
    };

    private static readonly Dictionary<string, string> Names = new() { [Aoi] = "アオイ", [Momo] = "モモ" };

    /// <summary>アオイ用（順方向）・作り物素体用（逆方向）・モモ用（外した）を持つ商品「甲」。</summary>
    private static ItemRecord Item()
    {
        var item = Make.Item("9900881", "作り物のプロファイル甲");
        return item with
        {
            Local = item.Local with
            {
                Conversion = new ConversionSetting
                {
                    Targets =
                    [
                        new ConversionTarget { AvatarItemId = Aoi, Forward = true, Slug = "fakea" },
                        new ConversionTarget { BaseName = "作り物素体", Reverse = true, Slug = "fakeb", Confirmed = true },
                        new ConversionTarget { AvatarItemId = Momo, Forward = true, Reverse = true, Slug = "fakec", Rejected = true },
                    ],
                },
            },
        };
    }

    private static bool Matches(string query, ConversionPairSearchScope scope)
        => ConversionItemSearch.Matches(Item(), query, scope, Entries, Names);

    [Theory]
    [InlineData("アオイ", true, false, true)]
    [InlineData("Aoi-chan", true, false, true)]
    [InlineData(Aoi, true, false, true)]
    [InlineData("作り物素体", true, true, false)]
    [InlineData("甲", true, false, false)]
    [InlineData("9900881", true, false, false)]
    [InlineData("モモ", false, false, false)]
    public void 順方向のつなぎ先は先_逆方向は元で当て_商品名は両方の時だけ_外したつなぎ先は当てない(string query, bool both, bool from, bool to)
    {
        Assert.Equal(both, Matches(query, ConversionPairSearchScope.Both));
        Assert.Equal(from, Matches(query, ConversionPairSearchScope.From));
        Assert.Equal(to, Matches(query, ConversionPairSearchScope.To));
    }

    [Fact]
    public void 両方向と向きの分からないつなぎ先は_元でも先でも当たる()
    {
        var item = Make.Item("9900882", "作り物のプロファイル乙");
        foreach (var target in new[]
        {
            new ConversionTarget { AvatarItemId = Aoi, Forward = true, Reverse = true },
            new ConversionTarget { AvatarItemId = Aoi },
        })
        {
            var with = item with { Local = item.Local with { Conversion = new ConversionSetting { Targets = [target] } } };
            Assert.True(ConversionItemSearch.Matches(with, "アオイ", ConversionPairSearchScope.From, Entries, Names));
            Assert.True(ConversionItemSearch.Matches(with, "アオイ", ConversionPairSearchScope.To, Entries, Names));
        }
    }

    // ---- 画面の一覧 ----

    /// <summary>あ（つなぎ先無し・決めていない）・い（アオイ用・決めた）・う（モモ用確認済み＋未設定・途中）・え（アオイ用確認待ち・決めていない）。</summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Entries[Aoi], Entries[Momo]] });
        ConversionTarget To(string id, string slug, bool confirmed) => new() { AvatarItemId = id, Forward = true, Slug = slug, Confirmed = confirmed };
        await ConversionItemGroupTests.SaveProfilesAsync(app, "9900871", "作り物のプロファイルあ", null, "fakea");
        await ConversionItemGroupTests.SaveProfilesAsync(app, "9900872", "作り物のプロファイルい",
            new ConversionSetting { Targets = [To(Aoi, "fakea", confirmed: true)] }, "fakea");
        await ConversionItemGroupTests.SaveProfilesAsync(app, "9900873", "作り物のプロファイルう",
            new ConversionSetting { Targets = [To(Momo, "fakea", confirmed: true)] }, "fakea", "fakeb");
        await ConversionItemGroupTests.SaveProfilesAsync(app, "9900874", "作り物のプロファイルえ",
            new ConversionSetting { Targets = [To(Aoi, "fakea", confirmed: false)] }, "fakea");

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);
        return avatars;
    }

    private static IReadOnlyList<string> Shown(AvatarsViewModel avatars)
        => [.. avatars.ConversionItems.Cast<ConversionItemRowViewModel>().Select(row => row.ItemId)];

    private static IReadOnlyList<string> Headings(AvatarsViewModel avatars)
        => [.. avatars.ConversionItems.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(group => group.Name.ToString()!)];

    [Fact]
    public Task 語を打つと商品の一覧も絞り_群の見出しは当たった数と全部_当たらない群は出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        Assert.Equal(["決めていない（2）", "途中（1）", "決めた（1）"], Headings(avatars));

        avatars.ConversionPairs.Query = "アオイ";

        Assert.Equal(["9900874", "9900872"], Shown(avatars));
        Assert.Equal(["決めていない（1/2）", "決めた（1/1）"], Headings(avatars));
        Assert.Equal("変換（4）", avatars.ConversionModeText);
        Assert.False(avatars.ShowsNoMatchingConversionItems);

        avatars.ConversionPairs.Query = string.Empty;

        Assert.Equal(4, Shown(avatars).Count);
        Assert.Equal(["決めていない（2）", "途中（1）", "決めた（1）"], Headings(avatars));
    });

    [Fact]
    public Task 探す所を替えると商品の一覧も絞り直す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ConversionPairs.Query = "アオイ";

        avatars.ConversionPairs.Scope = ConversionPairSearchScope.From;
        Assert.Empty(Shown(avatars));
        Assert.True(avatars.ShowsNoMatchingConversionItems);

        avatars.ConversionPairs.Scope = ConversionPairSearchScope.To;
        Assert.Equal(["9900874", "9900872"], Shown(avatars));
    });

    [Fact]
    public Task 当てはまる商品が無い時は1行で言い_絞り込みを消すで戻る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.ConversionPairs.Query = "当たらない語";
        Assert.True(avatars.ShowsNoMatchingConversionItems);

        avatars.ConversionPairs.ClearQueryCommand.Execute(null);
        Assert.False(avatars.ShowsNoMatchingConversionItems);
        Assert.Equal(4, Shown(avatars).Count);
    });

    /// <summary>
    /// 選びは絞りで外さない（追加した組と同じ）。ListBox は選んだ行が一覧から消えると選びに null を書き戻すので、
    /// その書き戻し（画面の側で決めていない null）も受けない
    /// </summary>
    [Fact]
    public Task 選んだ商品が絞りで隠れても選びと右の欄は残る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        Assert.Equal("9900871", avatars.SelectedConversion!.ItemId);

        avatars.ConversionPairs.Query = "アオイ";
        avatars.SelectedConversion = null; // 一覧から消えた行の選びを ListBox が書き戻す

        Assert.Equal("9900871", avatars.SelectedConversion!.ItemId);
        Assert.True(avatars.ShowsConversionDetail);
    });

    [Fact]
    public Task 絞っている間の後では_見えている決めていない_途中の商品の中で回る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ConversionPairs.Query = "作り物のプロファイル";
        avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().Single(row => row.ItemId == "9900874");

        avatars.ConversionPairs.Query = "アオイ";
        avatars.LaterCommand.Execute(null);

        // 見えているのは え（決めていない）と い（決めた）だけ。決めた商品へは移らず、隠れた あ・う へも移らない
        Assert.Equal("9900874", avatars.SelectedConversion!.ItemId);

        avatars.ConversionPairs.Query = "モモ";
        avatars.LaterCommand.Execute(null);
        Assert.Equal("9900873", avatars.SelectedConversion!.ItemId);
    });

    [Fact]
    public Task 組を選ぶと商品の選びは絞りで隠れていても外れる() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [new ConversionPair { From = ConversionEnd.Avatar(Momo), To = ConversionEnd.Avatar(Aoi) }],
        });
        var avatars = await OpenAsync(app);
        await avatars.ConversionPairs.ReloadAsync();
        avatars.ConversionPairs.Query = "アオイ";
        Assert.Equal("9900871", avatars.SelectedConversion!.ItemId);

        avatars.ConversionPairs.SelectedRow = avatars.ConversionPairs.Rows.Single();

        Assert.Null(avatars.SelectedConversion);
        Assert.True(avatars.ShowsConversionPairDetail);
    });
}
