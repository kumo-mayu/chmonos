using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定「一覧のカードに属性の札を表示」（ユーザ判断 2026-10-06）。既定は出さず、出さないときはカードの高さが属性の段のぶん低くなる。
/// カードの高さは <see cref="CardMetrics"/> が持つ静的な値なので、試験の終わりで既定（出さない）へ戻す。
/// </summary>
public class CardAttributeToggleTests
{
    // 属性の段の高さ（札18＋間4）。XAML のカードと描く側（CardInfoStrip）の定数と同じ値を、試験では数で持つ
    private const double AttributeRow = 22;

    private static async Task<(SearchViewModel Search, SettingsViewModel Settings)> OpenAsync(TestApp app)
    {
        var item = Make.Item("9900001", "作り物の衣装");
        await app.AddItemAsync(item with
        {
            Local = item.Local with { Attributes = new Dictionary<string, int> { ["質感"] = 63 } },
        });
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        return (main.Search, settings);
    }

    private static CardInfo InfoOfFirstCard(SearchViewModel search)
        => search.ListItems.First().InfoFor(narrow: false);

    [Fact]
    public void 設定の既定は出さない() => Assert.False(new AppSettings().ShowCardAttributes);

    [Fact]
    public Task 既定では属性の段を出さず_カードの高さは段のぶん低い() => TestApp.Run(async app =>
    {
        try
        {
            var (search, _) = await OpenAsync(app);

            var info = InfoOfFirstCard(search);
            Assert.False(info.ShowAttributes);
            Assert.Empty(info.Attributes);
            Assert.False(CardMetrics.ShowAttributes);
            Assert.Equal(CardMetrics.ImageHeight + 176 - AttributeRow, CardMetrics.Height);
        }
        finally
        {
            CardMetrics.ApplyShowAttributes(false);
        }
    });

    [Fact]
    public Task 設定を入れると札が出て高さが戻り_切るとまた縮む_開いている一覧がその場で替わる() => TestApp.Run(async app =>
    {
        try
        {
            var (search, settings) = await OpenAsync(app);
            var shortHeight = CardMetrics.Height;

            settings.ShowCardAttributes = true;
            await app.SettleAsync();

            Assert.True(app.Services.Settings.ShowCardAttributes);
            Assert.Equal(["質感 63"], InfoOfFirstCard(search).Attributes.Select(chip => chip.Text));
            Assert.Equal(shortHeight + AttributeRow, CardMetrics.Height);

            settings.ShowCardAttributes = false;
            await app.SettleAsync();

            Assert.False(app.Services.Settings.ShowCardAttributes);
            Assert.Empty(InfoOfFirstCard(search).Attributes);
            Assert.Equal(shortHeight, CardMetrics.Height);
        }
        finally
        {
            CardMetrics.ApplyShowAttributes(false);
        }
    });

    [Fact]
    public Task 切ってもリストの属性の列は残る() => TestApp.Run(async app =>
    {
        try
        {
            var (search, _) = await OpenAsync(app);

            Assert.Equal("質感 63", InfoOfFirstCard(search).AttributesLine);
        }
        finally
        {
            CardMetrics.ApplyShowAttributes(false);
        }
    });

    [Fact]
    public Task 切っている間は_カードに表示する属性の欄を押せない_選んだ属性は残る() => TestApp.Run(async app =>
    {
        var (_, settings) = await OpenAsync(app);
        try
        {
            settings.AddCardAttribute("質感");
            await app.SettleAsync();
            Assert.False(settings.CardAttributesEnabled);

            settings.ShowCardAttributes = true;
            await app.SettleAsync();
            Assert.True(settings.CardAttributesEnabled);

            settings.ShowCardAttributes = false;
            await app.SettleAsync();
            Assert.Equal(["質感"], app.Services.Settings.CardAttributes);
        }
        finally
        {
            CardMetrics.ApplyShowAttributes(false);
        }
    });
}
