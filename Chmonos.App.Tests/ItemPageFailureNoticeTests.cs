using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページで押した所がある操作の失敗は、押した所の下に出す（B8・2026-11-10・ui-empty-and-errors.md「知らせの置き場」）。
/// 「既読にする」と、ファイルの行の「一時的に展開して開く」は、前は窓で出していた
/// </summary>
public class ItemPageFailureNoticeTests
{
    private const string ItemId = "1000001";

    [Fact]
    public Task 既読にするが書けなかったら_帯の下に出して窓は出さない() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var item = Make.Item(ItemId, "作り物の衣装 改");
        await app.AddItemAsync(item);
        var diffs = new[] { new NotificationDiff { Field = BoothChanges.NameField, Before = "作り物の衣装", After = "作り物の衣装 改" } };
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(new NotificationRecord
            {
                Id = $"item-updated:{ItemId}",
                Kind = NotificationKind.ItemUpdated,
                ItemId = ItemId,
                Title = "作り物の衣装",
                Detail = BoothChanges.Summarize(diffs),
                Diffs = diffs,
                CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)),
            });
            return list;
        });
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.True(page.HasUnreadChanges);

        // 知らせのファイルを誰にも触らせずに掴んで、既読の書き込み（置き換え）を失敗させる
        using (new FileStream(app.Services.Paths.NotificationsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            page.MarkChangesReadCommand.Execute(null);
            await UiThread.Until(() => page.ChangesNotice.HasText, "失敗が帯の下に出る");
        }

        Assert.True(page.ChangesNotice.IsWarning);
        Assert.True(page.HasUnreadChanges);
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 一時的に展開できなかったら_ファイルの欄の下に出して窓は出さない() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;

        // zip の名前で中身が zip でない物（壊れた書庫）。展開は失敗で返る
        var zip = app.NewFile("broken.zip", [1, 2, 3, 4]);
        var item = Make.Item(ItemId, "作り物の衣装").WithFiles(Make.File(zip));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var row = Assert.Single(page.LocalFiles);
        await UiThread.Until(() => row.CanUnpack, "zip が手元にあると分かる");

        try
        {
            page.UnpackCommand.Execute(row);
            await UiThread.Until(() => page.FilesNotice.HasText, "失敗がファイルの欄の下に出る");
        }
        finally
        {
            new TemporaryUnpacker(TemporaryUnpacker.RootFor(app.Services.Paths.Root)).CleanUp();
        }

        Assert.True(page.FilesNotice.IsWarning);
        Assert.Empty(app.Notices);
    });
}
