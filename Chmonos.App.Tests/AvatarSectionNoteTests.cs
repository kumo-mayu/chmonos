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

    /// <summary>
    /// 開いている間に裏の判定が終わったら、勝手に入れ替えず「表示する」を出し、押したらこの欄だけ入れ替える（ユーザ判断 2026-10-06）。
    /// 対応アバターが変わらなかった判定は、判定の日時だけを黙って入れる
    /// </summary>
    [Fact]
    public Task 開いている間に判定が終わると_表示するを出し_押すと対応アバターの欄だけ入れ替わる() => Support.TestApp.Run(async app =>
    {
        var item = Support.Make.Item("9900903", "作り物の衣装");
        await app.AddItemAsync(item);
        var unchanged = Support.Make.Item("9900904", "作り物の髪型");
        await app.AddItemAsync(unchanged);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.False(page.HasDetectedAvatars);

        // 裏の判定が対応アバターを書いた
        var now = DateTimeOffset.Now;
        await app.Services.Store.Items.SaveAsync(item with
        {
            Local = item.Local with
            {
                Avatars = [new Core.Models.AvatarLink { AvatarItemId = "9900950", Name = "作り物のアバター" }],
                AvatarsDetectedAt = now,
            },
        });
        await page.NoteAvatarsDetectedAsync();

        Assert.True(page.HasDetectedAvatars);
        Assert.False(page.HasAvatars);
        page.ShowDetectedAvatarsCommand.Execute(null);
        Assert.False(page.HasDetectedAvatars);
        Assert.Equal("作り物のアバター", Assert.Single(page.Avatars).Name);

        // 変わらなかった判定は「表示する」を出さず、判定の日時だけを入れる
        var quiet = new ItemViewModel(unchanged, app.Services, main, main.Thumbnails);
        await app.Services.Store.Items.SaveAsync(unchanged with { Local = unchanged.Local with { AvatarsDetectedAt = now } });
        await quiet.NoteAvatarsDetectedAsync();
        Assert.False(quiet.HasDetectedAvatars);
        Assert.Equal("出品者の対応表明は見つかっていません。アバターの管理から検出できます。", quiet.AvatarSectionNote);
    });
}
