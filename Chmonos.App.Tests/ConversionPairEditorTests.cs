using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 人が足す変換の組の画面（<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）：アバターの管理の詳細の
/// 「このアバターの衣装を着せられるアバター」と、商品ページの「変換経由」の吹き出し。名前・ID は作り物。
/// </summary>
public class ConversionPairEditorTests
{
    private const string Aoi = "9800401";
    private const string Momo = "9800402";
    private const string ClothingId = "9900891";

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
    };

    private static Task SaveRegistryAsync(TestApp app) => app.Store.Avatars.SaveAsync(new AvatarRegistry
    {
        Entries = [Avatar(Aoi, "アオイ"), Avatar(Momo, "モモ")],
        BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
    });

    private static async Task<AvatarsViewModel> OpenAvatarsAsync(TestApp app)
    {
        await SaveRegistryAsync(app);
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == Aoi);
        return avatars;
    }

    [Fact]
    public Task 候補から選ぶと組を足して札に出し_外すと消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAvatarsAsync(app);
        var pairs = avatars.ConversionPairs;

        // 見せているアバター自身は候補に出さない。共通素体は「（共通素体）」を添えて出す
        Assert.DoesNotContain("アオイ", pairs.Suggestions);
        Assert.Contains("作り物素体（共通素体）", pairs.Suggestions);

        await pairs.AddAsync("モモ");
        await pairs.AddAsync("作り物素体（共通素体）");

        Assert.Equal(["モモ", "作り物素体（共通素体）"], pairs.Rows.Select(row => row.Name));
        Assert.Equal(
            [new ConversionPair { From = ConversionEnd.Avatar(Aoi), To = ConversionEnd.Avatar(Momo) }, new ConversionPair { From = ConversionEnd.Avatar(Aoi), To = ConversionEnd.Base("作り物素体") }],
            app.Store.ConversionPairs.Load().Pairs);

        pairs.Rows[0].RemoveCommand.Execute(null);
        await UiThread.Until(() => pairs.Rows.Count == 1, "外した札が消える");
        Assert.Equal(ConversionEnd.Base("作り物素体"), Assert.Single(app.Store.ConversionPairs.Load().Pairs).To);
    });

    [Fact]
    public Task 候補に無い文字は足さずにそう言い_ほかのアバターの組は札に出さない() => TestApp.Run(async app =>
    {
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [new ConversionPair { From = ConversionEnd.Avatar(Momo), To = ConversionEnd.Avatar(Aoi) }],
        });
        var avatars = await OpenAvatarsAsync(app);
        var pairs = avatars.ConversionPairs;

        await pairs.AddAsync("作り物の知らないアバター");

        Assert.Empty(pairs.Rows);
        Assert.Equal("候補からアバターか共通素体を選んでください。", pairs.Notice.Text);
        Assert.Single(app.Store.ConversionPairs.Load().Pairs);
    });

    [Fact]
    public Task 組から来た変換経由の札は_吹き出しで足した組からと言う() => TestApp.Run(async app =>
    {
        await SaveRegistryAsync(app);
        await app.Store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [new ConversionPair { From = ConversionEnd.Avatar(Aoi), To = ConversionEnd.Avatar(Momo) }],
        });
        var clothing = Make.Item(ClothingId, "作り物の衣装");
        clothing = clothing with
        {
            Local = clothing.Local with
            {
                Avatars = [new AvatarLink { AvatarItemId = Aoi, Name = "アオイ", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(clothing);
        var main = await app.StartAsync();

        var page = new ItemViewModel(clothing, app.Services, main, main.Thumbnails);

        var row = Assert.Single(page.ViaConversionAvatars);
        Assert.Equal("モモ", row.Name);
        Assert.Contains("「アオイ」用を、追加した組で変換できる見込みです。", row.ViaTooltip, StringComparison.Ordinal);
    });
}
