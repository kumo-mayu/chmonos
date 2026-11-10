using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 保管した通知を読む見方（ユーザ案 2026-11-10「その形で良いです」）：通知の画面の上の段のボタンで同じ画面の中で切り替え、
/// 月ごとに新しい順に100件ずつ読む。読むだけで、既読・行のボタンは出さない。「商品名で絞る」欄。
/// </summary>
public sealed class InboxArchiveViewTests
{
    private static DateTimeOffset At(int month, int day, int minute = 0)
        => new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.FromHours(9)).AddMinutes(minute);

    private static NotificationRecord Record(string id, DateTimeOffset createdAt, string title = "作り物の衣装", NotificationKind kind = NotificationKind.ItemUpdated) => new()
    {
        Id = id,
        Kind = kind,
        ItemId = "9900601",
        Title = title,
        Detail = "価格：500 → 800",
        CreatedAt = createdAt,
        IsRead = true,
    };

    private static async Task<(InboxViewModel Inbox, InboxArchiveViewModel Archive)> OpenArchiveAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);
        await app.SettleAsync();
        inbox.ShowArchiveCommand.Execute(null);
        var archive = inbox.Archive!;
        await archive.Pending;
        return (inbox, archive);
    }

    private static List<string> Ids(InboxArchiveViewModel archive) => archive.Rows.Select(row => row.Record.Id).ToList();

    [Fact]
    public Task 新しい月を新しい順に100件ずつ読み_続きを読める() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync(Enumerable.Range(0, 250).Select(i => Record($"oct-{i:D3}", At(10, 1, i))).ToList());
        await app.Store.NotificationArchive.AppendAsync([Record("sep", At(9, 1))]);

        var (inbox, archive) = await OpenArchiveAsync(app);

        Assert.True(inbox.IsArchiveMode);
        Assert.Equal(["2026年10月", "2026年9月"], archive.Months.Select(month => month.Label));
        Assert.Equal("2026-10", archive.SelectedMonth!.Month);
        Assert.Equal("250 件", archive.CountText);
        Assert.Equal(Enumerable.Range(150, 100).Reverse().Select(i => $"oct-{i:D3}"), Ids(archive));
        Assert.IsType<ArchiveHeadLine>(archive.Lines[0]);
        Assert.True(((InboxLine)archive.Lines[^1]).IsLast);
        Assert.True(archive.HasMore);

        await archive.LoadMoreAsync();
        await archive.LoadMoreAsync();

        Assert.Equal(Enumerable.Range(0, 250).Reverse().Select(i => $"oct-{i:D3}"), Ids(archive));
        Assert.False(archive.HasMore);
        Assert.Single(archive.Lines.OfType<InboxLine>(), line => line.IsLast);

        // 残りが無ければ何も読まない
        await archive.LoadMoreAsync();
        Assert.Equal(250, archive.Rows.Count);

        archive.SelectedMonth = archive.Months[1];
        await archive.Pending;
        Assert.Equal(["sep"], Ids(archive));
    });

    [Fact]
    public Task 行は読むだけで_既読の丸と行のボタンを出さず_種類を言う() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync([Record("back", At(10, 1), kind: NotificationKind.ItemBackOnBooth)]);

        var (_, archive) = await OpenArchiveAsync(app);

        var row = Assert.Single(archive.Rows);
        Assert.True(row.IsArchived);
        Assert.False(row.HasAction);
        Assert.Null(row.ActionCommand);
        Assert.Equal("非公開商品の復活", row.KindText);
    });

    [Fact]
    public Task 商品名で絞ると当てはまる物だけを新しい順に_消すと全部に戻る() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync(
            Enumerable.Range(0, 150).Select(i => Record($"dress-{i:D3}", At(10, 1, i), "ふわふわドレス"))
                .Concat(Enumerable.Range(0, 30).Select(i => Record($"hair-{i:D2}", At(10, 2, i), "作り物の髪型")))
                .ToList());

        var (_, archive) = await OpenArchiveAsync(app);

        archive.NameFilter = "フワフワ";
        await archive.Pending;

        Assert.Equal("180 件中 150 件", archive.CountText);
        Assert.Equal(Enumerable.Range(50, 100).Reverse().Select(i => $"dress-{i:D3}"), Ids(archive));
        await archive.LoadMoreAsync();
        Assert.Equal(150, archive.Rows.Count);

        archive.NameFilter = "靴";
        await archive.Pending;
        Assert.True(archive.IsEmpty);
        Assert.Empty(archive.Lines);
        Assert.Equal("当てはまる通知はありません", archive.EmptyTitle);
        Assert.Equal("商品名の言葉を変えるか、別の月を選ぶか、絞り込みを消してください。", archive.EmptyNote);

        archive.NameFilter = string.Empty;
        await archive.Pending;
        Assert.Equal("180 件", archive.CountText);
        Assert.Equal("hair-29", Ids(archive)[0]);
    });

    [Fact]
    public Task 同じ知らせが2回保管されていても1行() => TestApp.Run(async app =>
    {
        var records = Enumerable.Range(0, 3).Select(i => Record($"r{i}", At(10, 1, i))).ToList();
        await app.Store.NotificationArchive.AppendAsync(records);
        await app.Store.NotificationArchive.AppendAsync(records);

        var (_, archive) = await OpenArchiveAsync(app);

        Assert.Equal(["r2", "r1", "r0"], Ids(archive));
        Assert.Equal("3 件", archive.CountText);
    });

    [Fact]
    public Task 壊れたかたまりは飛ばし_読めない所があったことを言う() => TestApp.Run(async app =>
    {
        var archiveStore = app.Store.NotificationArchive;
        var path = archiveStore.FileFor("2026-10");
        var starts = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            starts.Add(File.Exists(path) ? new FileInfo(path).Length : 0);
            await archiveStore.AppendAsync([Record($"r{i}", At(10, 1, i)) with { Detail = Guid.NewGuid().ToString("N") }]);
        }

        // 真ん中のかたまりの中ほどを1バイト壊す
        var bytes = File.ReadAllBytes(path);
        bytes[(int)(starts[2] + (starts[3] - starts[2]) / 2)] ^= 0x55;
        File.WriteAllBytes(path, bytes);

        var (_, archive) = await OpenArchiveAsync(app);

        Assert.Equal(["r4", "r3", "r1", "r0"], Ids(archive));
        Assert.True(archive.HasDamage);
        Assert.Equal("この月の一部を読めなかったため、その分は表示していません。", archive.DamageText);
    });

    [Fact]
    public Task 保管した月が無ければ_並ぶようにする方法を言う() => TestApp.Run(async app =>
    {
        var (_, archive) = await OpenArchiveAsync(app);

        Assert.True(archive.IsEmpty);
        Assert.Empty(archive.Months);
        Assert.False(archive.HasMonths);
        Assert.Equal("保管した通知はありません", archive.EmptyTitle);
        Assert.Equal("通知に残す件数を超えた古い既読の通知が、ここに月ごとに保管されます。", archive.EmptyNote);
    });

    [Fact]
    public Task 保管しない設定で月が無ければ_容量を0より大きくすることを言う() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { NotificationArchiveMegabytes = 0 });

        var (_, archive) = await OpenArchiveAsync(app);

        Assert.Equal("保管した通知はありません", archive.EmptyTitle);
        Assert.Equal("設定の「通知の保管に使う容量」を0より大きくすると、古い通知をここに保管します。", archive.EmptyNote);
    });

    [Fact]
    public Task 通知に戻ると今の通知の見方に戻り_ナビの通知でも戻る() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync([Record("old", At(10, 1))]);
        var main = await app.StartAsync();
        main.ShowInbox();
        var inbox = Assert.IsType<InboxViewModel>(main.CurrentViewModel);

        inbox.ShowArchiveCommand.Execute(null);
        Assert.True(inbox.IsArchiveMode);
        inbox.LeaveArchiveCommand.Execute(null);
        Assert.False(inbox.IsArchiveMode);

        inbox.ShowArchiveCommand.Execute(null);
        main.ShowInboxCommand.Execute(null);
        Assert.Same(inbox, main.CurrentViewModel);
        Assert.False(inbox.IsArchiveMode);
    });

    /// <summary>保管した通知から商品ページを開いて戻ると、保管した通知の同じ月と絞りに戻る（今の通知へ落ちない）。</summary>
    [Fact]
    public Task 商品ページから戻ると_保管した通知の同じ月と絞りに戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900601", "作り物の衣装"));
        await app.Store.NotificationArchive.AppendAsync([Record("oct", At(10, 1))]);
        await app.Store.NotificationArchive.AppendAsync([Record("sep", At(9, 1))]);
        var main = await app.StartAsync();
        main.ShowInbox();
        var inbox = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
        inbox.ShowArchiveCommand.Execute(null);
        await inbox.Archive!.Pending;
        inbox.Archive.SelectedMonth = inbox.Archive.Months[1];
        inbox.Archive.NameFilter = "衣装";
        await inbox.Archive.Pending;

        inbox.Archive.Rows[0].OpenItemCommand!.Execute(null);
        await UiThread.Until(() => main.CurrentViewModel is not InboxViewModel, "商品ページが開く");
        main.GoBack();
        await app.SettleAsync();

        var back = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
        Assert.True(back.IsArchiveMode);
        await back.Archive!.Pending;
        Assert.Equal("2026-09", back.Archive.SelectedMonth!.Month);
        Assert.Equal("衣装", back.Archive.NameFilter);
        Assert.Equal(["sep"], Ids(back.Archive));
    });
}
