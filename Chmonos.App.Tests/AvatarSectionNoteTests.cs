using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの対応アバターの欄の説明。まだ判定していない商品と、判定して見つからなかった商品を言い分ける（ユーザ判断 2026-10-06）。
/// 未確定から登録した直後は、登録の列が空になるまで判定を待たせるので、その間に「見つかっていません」と出ると誤って読める。
/// </summary>
public class AvatarSectionNoteTests
{
    [Fact]
    public Task 判定していない商品は_判定がまだ終わっていないと言い_判定した商品は見つからないと言う() => Support.TestApp.Run(async app =>
    {
        var notYet = Support.Make.Item("9900901", "作り物の衣装");
        var detected = Support.Make.Item("9900902", "作り物の髪型");
        detected = detected with { Local = detected.Local with { AvatarsDetectedAt = DateTimeOffset.Now } };
        await app.AddItemAsync(notYet);
        await app.AddItemAsync(detected);
        var main = await app.StartAsync();

        Assert.Equal("対応アバターの判定がまだ終わっていません。", new ItemViewModel(notYet, app.Services, main, main.Thumbnails).AvatarSectionNote);
        Assert.Equal(
            "出品者の対応表明は見つかっていません。アバターの管理から検出できます。",
            new ItemViewModel(detected, app.Services, main, main.Thumbnails).AvatarSectionNote);
    });
}
