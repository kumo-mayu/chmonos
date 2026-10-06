using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 通知の行の頭の商品の絵（ユーザ指示 2026-10-06「通知画面にもアイコンが必要だろう」）。作りは取り込みの探した結果の行と同じ：
/// 見えて読まれるまで記録を読まず、決め方は検索のカードと同じ（★の指名が勝つ）。商品を指さない知らせには絵を付けない。
/// </summary>
public class InboxPictureTests
{
    private const string Pinned = "9900021";
    private const string Plain = "9900022";
    private const string Gone = "9900023";
    private const string Broken = "9900024";

    private static readonly DateTimeOffset Day = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));

    private static NotificationRecord Record(string id, NotificationKind kind, string? itemId, string title) => new()
    {
        Id = id,
        Kind = kind,
        Title = title,
        Detail = string.Empty,
        ItemId = itemId,
        CreatedAt = Day,
    };

    /// <summary>絵を2枚持ち、2枚目を★で指名した商品。返すのは2枚目の絵の場所。</summary>
    private static async Task<string> AddPinnedItemAsync(TestApp app, string itemId, string name)
    {
        var item = Make.Item(itemId, name);
        item = item with
        {
            Booth = item.Booth with
            {
                Images = [new BoothImage { OriginalUrl = $"https://sample.invalid/{itemId}/a.png" }, new BoothImage { OriginalUrl = $"https://sample.invalid/{itemId}/b.png" }],
            },
        };

        var directory = app.Services.Paths.ItemImagesDir(itemId);
        Directory.CreateDirectory(directory);
        var files = item.Booth.Images.Select(image => Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl))).ToArray();
        foreach (var file in files)
        {
            using var image = new Image<Rgba32>(4, 4);
            image.SaveAsWebp(file);
        }

        await app.AddItemAsync(item with { Local = item.Local with { ThumbnailImage = Path.GetFileName(files[1]) } });
        return files[1];
    }

    private static async Task<InboxViewModel> OpenAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main) { UnreadOnly = false };
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count > 0, "通知の束が並ぶ");
        return inbox;
    }

    private static NotificationRow Row(InboxViewModel inbox, string id)
        => inbox.Groups.SelectMany(group => group.Rows).Single(row => row.Record.Id == id);

    [Fact]
    public Task 商品の行には絵が付き_見えて読まれるまで決めず_星の指名で決まる() => TestApp.Run(async app =>
    {
        var pinnedFile = await AddPinnedItemAsync(app, Pinned, "作り物の衣装");
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Record("up", NotificationKind.ItemUpdated, Pinned, "作り物の衣装"));
            list.Add(Record("back", NotificationKind.ItemBackOnBooth, Pinned, "作り物の衣装"));
            return list;
        });

        var inbox = await OpenAsync(app);
        var picture = Row(inbox, "up").Picture!;

        // 画面が行を作って絵を読むまでは、商品の記録を読みに行かない（数百件の知らせで、見えない行は何も読まない）
        Assert.Null(picture.Path);

        _ = picture.Image;
        await UiThread.Until(() => picture.Path is not null, "指名した絵の場所が決まる");
        await app.SettleAsync();

        Assert.Equal(pinnedFile, picture.Path);
        Assert.Equal("作", picture.Initial);
        Assert.NotNull(Row(inbox, "back").Picture);
    });

    [Fact]
    public Task 絵の無い商品と消えた商品は_場所が無く頭文字が出る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(Plain, "【作り物】髪型"));
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Record("plain", NotificationKind.ItemUpdated, Plain, "【作り物】髪型"));
            list.Add(Record("gone", NotificationKind.ItemUpdated, Gone, "消えた商品"));
            return list;
        });

        var inbox = await OpenAsync(app);
        var plain = Row(inbox, "plain").Picture!;
        var gone = Row(inbox, "gone").Picture!;
        _ = plain.Image;
        _ = gone.Image;
        await app.SettleAsync();

        Assert.Null(plain.Path);
        Assert.Null(gone.Path);
        Assert.Null(plain.Image);
        Assert.Equal("作", plain.Initial);
        Assert.Equal("消", gone.Initial);
    });

    [Fact]
    public Task 商品を指さない知らせには_絵を付けない() => TestApp.Run(async app =>
    {
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Record("mismatch", NotificationKind.HandEditMismatch, null, "手で直したJSONに食い違いがあります"));
            list.Add(Record("unpacked", NotificationKind.UnpackedFilesImported, null, "展開したフォルダの中のファイルを取り込みました"));
            return list;
        });

        var inbox = await OpenAsync(app);

        Assert.Null(Row(inbox, "mismatch").Picture);
        Assert.Null(Row(inbox, "unpacked").Picture);
    });

    [Fact]
    public Task 読めない記録の行は_控えから絵を決め_控えが無ければ名前の代わりに疑問符() => TestApp.Run(async app =>
    {
        // 保存すると控え（items/.prev）ができるので、壊す前の版が控えに残る
        var pinnedFile = await AddPinnedItemAsync(app, Broken, "作り物の靴");
        File.WriteAllText(app.Services.Paths.ItemFile(Broken), "{\n  \"id\": \"" + Broken + "\",\n");
        File.WriteAllText(app.Services.Paths.ItemFile(Gone), "{\n");

        var inbox = await OpenAsync(app);
        await UiThread.Until(() => inbox.Groups.Any(group => group.Kind == NotificationKind.UnreadableItem), "読めない記録の束が並ぶ");
        var rows = inbox.Groups.Single(group => group.Kind == NotificationKind.UnreadableItem).Rows;
        var withCopy = rows.Single(row => row.Title == "作り物の靴").Picture!;
        var noCopy = rows.Single(row => row.Title == $"{Gone}.json").Picture!;

        _ = withCopy.Image;
        _ = noCopy.Image;
        await UiThread.Until(() => withCopy.Path is not null, "控えの絵の場所が決まる");
        await app.SettleAsync();

        Assert.Equal(pinnedFile, withCopy.Path);
        Assert.Equal("作", withCopy.Initial);
        Assert.Null(noCopy.Path);
        Assert.Equal("?", noCopy.Initial);
    });
}
