using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Images;

namespace Chmonos.App.Tests;

/// <summary>
/// 落とした URL の登録と、まとめての登録が、いつ検索に出て、いつ帯「登録しました」を出すか（計画 28・ユーザ指摘 2026-11-11）。
/// 未確定の「このIDで登録」と同じく、1枚目の画像を取ったところで登録を終え、残りの画像は⑤の段で裏に取る。
/// 直す前は、2枚目以降を1枚ずつ取り終えるまで、検索にも帯にも出なかった（この試験の形で、2枚目の答えを止めて確かめた）。
/// BOOTH は作り物（<see cref="FakeBooth"/>）。
/// </summary>
public class DroppedRegisterTimingTests
{
    private const string ItemA = "1000001";
    private const string ItemB = "1000002";

    private static void AnswerYesToRegister(TestApp app)
        => app.Answer = request => request.Caption == "BOOTHのURLを受け取りました"
            ? MessageBoxResult.Yes
            : request.Button == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.No;

    private static bool HasImage(TestApp app, string itemId, int index)
        => File.Exists(Path.Combine(app.Services.Paths.ItemImagesDir(itemId), ImagePipeline.FileNameFor(FakeBooth.ImageUrl(itemId, index))));

    [Fact]
    public Task 落としたURLの登録は_1枚目を取ったところで検索に出て帯も出し_残りの画像は後から届く() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { SaveImages = true });
        app.Booth.HasItem(ItemA, "作り物の上着", images: 3);
        AnswerYesToRegister(app);
        var main = await app.StartAsync();

        // 2枚目の答えを止める。登録がそれを待たずに終わり、検索と帯が同じ時に出ることを見る
        app.Booth.Hold(url => url == FakeBooth.ImageUrl(ItemA, 2));
        try
        {
            var drop = main.HandleDropAsync(null, $"https://booth.pm/ja/items/{ItemA}");
            await UiThread.Until(() => drop.IsCompleted, "2枚目を待たずに登録が終わる");
            await drop;

            Assert.True(HasImage(app, ItemA, 1));
            Assert.False(HasImage(app, ItemA, 2));
            Assert.False(main.IsRegisteringDropped);
            Assert.NotNull(main.Search.FindItem(ItemA));
            Assert.Equal("「作り物の上着」を登録しました。", main.RegisteredNoticeText);
        }
        finally
        {
            app.Booth.Release();
        }

        // 残りの画像は裏で届く（1枚ずつ1回だけ。1枚目は取り直さない）
        await UiThread.Until(() => HasImage(app, ItemA, 3), "残りの画像が届く");
        await app.SettleAsync();
        Assert.Single(app.Booth.Requests, url => url == FakeBooth.ImageUrl(ItemA, 1));
        Assert.Single(app.Booth.Requests, url => url == FakeBooth.ImageUrl(ItemA, 2));
        Assert.Single(app.Booth.Requests, url => url == FakeBooth.ImageUrl(ItemA, 3));
    });

    [Fact]
    public Task まとめての登録は_済んだ1件ずつ検索に出し_残りの画像は列が済んでから頼む() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { SaveImages = true });
        app.Booth.HasItem(ItemA, "作り物の上着", images: 2);
        app.Booth.HasItem(ItemB, "作り物のスカート", images: 2);
        app.AnswerPastedItems = _ => true;
        var main = await app.StartAsync();

        // 2件目の商品JSONで止める。その間に1件目はもう検索に出ていて、1件目の残りの画像はまだ頼んでいない
        // （列が動いている間は、残りの画像が次の登録の問い合わせの合間に入らない。HoldAfterRegistration）
        app.Booth.Hold(url => url.EndsWith($"/items/{ItemB}.json", StringComparison.Ordinal));
        Task drop;
        try
        {
            drop = main.HandleDropAsync(null, $"https://booth.pm/ja/items/{ItemA} https://booth.pm/ja/items/{ItemB}");
            await UiThread.Until(() => app.Booth.Requests.Any(url => url.EndsWith($"/items/{ItemB}.json", StringComparison.Ordinal)), "2件目の問い合わせが届く");
            await UiThread.Until(() => main.Search.FindItem(ItemA) is not null, "1件目が検索に出る");

            Assert.True(HasImage(app, ItemA, 1));
            Assert.DoesNotContain(app.Booth.Requests, url => url == FakeBooth.ImageUrl(ItemA, 2));
            Assert.Null(main.Search.FindItem(ItemB));
            Assert.True(main.IsRegisteringDropped);
        }
        finally
        {
            app.Booth.Release();
        }

        await drop;
        Assert.NotNull(main.Search.FindItem(ItemB));
        Assert.Equal("2 件の商品を登録しました。", main.RegisteredNoticeText);

        await UiThread.Until(() => HasImage(app, ItemA, 2) && HasImage(app, ItemB, 2), "残りの画像が届く");
        await app.SettleAsync();

        // 残りの画像は、2件の登録の問い合わせが全部済んだ後
        var requests = app.Booth.Requests.ToList();
        Assert.True(
            requests.IndexOf(FakeBooth.ImageUrl(ItemA, 2)) > requests.IndexOf(FakeBooth.ImageUrl(ItemB, 1)),
            string.Join(" / ", requests));
    });
}
