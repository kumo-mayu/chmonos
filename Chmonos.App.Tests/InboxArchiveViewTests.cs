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
        main.ShowInbox();
        await app.SettleAsync();
        ((InboxViewModel)main.CurrentViewModel!).ShowArchiveCommand.Execute(null);
        var inbox = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
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

    /// <summary>「戻る」（Alt+←・マウスの戻る）で今の通知へ戻る（ユーザ案 2026-11-10）。「← 通知に戻る」とナビの「通知」でも今の通知が開く。</summary>
    [Fact]
    public Task 戻るで今の通知へ戻り_通知に戻るとナビの通知でも今の通知が開く() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync([Record("old", At(10, 1))]);
        var main = await app.StartAsync();
        main.ShowInbox();
        var inbox = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
        Assert.False(inbox.IsArchiveMode);

        inbox.ShowArchiveCommand.Execute(null);
        var archive = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
        Assert.True(archive.IsArchiveMode);
        Assert.Equal("← 通知に戻る", main.BackButtonText);
        main.GoBack();
        Assert.False(Assert.IsType<InboxViewModel>(main.CurrentViewModel).IsArchiveMode);

        ((InboxViewModel)main.CurrentViewModel!).ShowArchiveCommand.Execute(null);
        ((InboxViewModel)main.CurrentViewModel!).LeaveArchiveCommand.Execute(null);
        Assert.False(Assert.IsType<InboxViewModel>(main.CurrentViewModel).IsArchiveMode);

        ((InboxViewModel)main.CurrentViewModel!).ShowArchiveCommand.Execute(null);
        main.ShowInboxCommand.Execute(null);
        Assert.False(Assert.IsType<InboxViewModel>(main.CurrentViewModel).IsArchiveMode);

        // 今の通知でナビの「通知」を押しても開き直さない
        var current = main.CurrentViewModel;
        main.ShowInboxCommand.Execute(null);
        Assert.Same(current, main.CurrentViewModel);
    });

    /// <summary>保管した通知から商品ページを開いて戻ると、保管した通知の同じ月と絞りに戻る（今の通知へ落ちない）。</summary>
    [Fact]
    public Task 商品ページから戻ると_保管した通知の同じ月と絞りに戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900601", "作り物の衣装"));
        await app.Store.NotificationArchive.AppendAsync([Record("oct", At(10, 1))]);
        await app.Store.NotificationArchive.AppendAsync([Record("sep", At(9, 1))]);
        var (_, opened) = await OpenArchiveAsync(app);
        var main = app.Main;
        var inbox = Assert.IsType<InboxViewModel>(main.CurrentViewModel);
        Assert.Same(opened, inbox.Archive);
        inbox.Archive!.SelectedMonth = inbox.Archive.Months[1];
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

    /// <summary>
    /// 商品の見方から商品ページを開いて戻ると、商品の見方と選んでいた商品で戻る（open 146 の続き・ユーザ判断 2026-11-20。
    /// 見方を控えずに年月の見方に戻っていた）。商品名は欄にも出し、年月へ戻すと前に見ていた月で出す
    /// </summary>
    [Fact]
    public Task 商品の見方から商品ページへ移って戻ると_同じ商品の見方で戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900603", "作り物の髪型"));
        await app.Store.NotificationArchive.AppendAsync([Record("sep-a", At(9, 1)), Record("sep-b", At(9, 2), "作り物の髪型") with { ItemId = "9900603" }]);
        await app.Store.NotificationArchive.AppendAsync([Record("oct-a", At(10, 1)), Record("oct-b", At(10, 2), "作り物の髪型") with { ItemId = "9900603" }]);
        var (inbox, archive) = await OpenArchiveAsync(app);
        archive.SelectedMonth = archive.Months[1];
        await archive.Pending;
        archive.IsItemLevel = true;
        await archive.Pending;
        archive.PickItemCommand.Execute("作り物の髪型");
        await archive.Pending;
        Assert.Equal(["oct-b", "sep-b"], Ids(archive));

        var main = app.Main;
        archive.Rows[0].OpenItemCommand!.Execute(null);
        await UiThread.Until(() => main.CurrentViewModel is not InboxViewModel, "商品ページが開く");
        main.GoBack();
        await app.SettleAsync();

        var back = Assert.IsType<InboxViewModel>(main.CurrentViewModel).Archive!;
        Assert.NotSame(archive, back);
        await back.Pending;
        Assert.True(back.IsItemLevel);
        Assert.Equal("作り物の髪型", back.SelectedItem);
        Assert.Equal("作り物の髪型", back.ItemText);
        Assert.Equal("作り物の髪型", back.HeadLabel);
        Assert.Equal(["oct-b", "sep-b"], Ids(back));

        back.IsMonthLevel = true;
        await back.Pending;
        Assert.Equal("2026-09", back.SelectedMonth!.Month);
        Assert.Equal(["sep-b", "sep-a"], Ids(back));
    });

    /// <summary>変更の種類の見方から商品ページへ移って戻ると、同じ種類（最初の種類ではない物）と絞りで戻る（open 146 の続き）。</summary>
    [Fact]
    public Task 種類の見方から商品ページへ移って戻ると_同じ種類と絞りで戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900601", "作り物の衣装"));
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("sep-update", At(9, 1)),
            Record("sep-back", At(9, 2), kind: NotificationKind.ItemBackOnBooth),
        ]);
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("oct-update", At(10, 1)),
            Record("oct-update2", At(10, 2), "作り物の髪型"),
            Record("oct-back", At(10, 3), "作り物の髪型", NotificationKind.ItemBackOnBooth),
        ]);
        var (_, archive) = await OpenArchiveAsync(app);
        archive.IsKindLevel = true;
        await archive.Pending;
        archive.SelectedKind = archive.Kinds.Single(choice => choice.Kind == NotificationKind.ItemBackOnBooth);
        await archive.Pending;
        archive.NameFilter = "衣装";
        await archive.Pending;
        Assert.Equal(["sep-back"], Ids(archive));

        var main = app.Main;
        archive.Rows[0].OpenItemCommand!.Execute(null);
        await UiThread.Until(() => main.CurrentViewModel is not InboxViewModel, "商品ページが開く");
        main.GoBack();
        await app.SettleAsync();

        var back = Assert.IsType<InboxViewModel>(main.CurrentViewModel).Archive!;
        await back.Pending;
        Assert.True(back.IsKindLevel);
        Assert.Equal(NotificationKind.ItemBackOnBooth, back.SelectedKind!.Kind);
        Assert.Same(back.SelectedKind, back.Kinds.Single(choice => choice.Kind == NotificationKind.ItemBackOnBooth));
        Assert.Equal("衣装", back.NameFilter);
        Assert.Equal(["sep-back"], Ids(back));
        Assert.Equal("2 件中 1 件", back.CountText);
    });

    /// <summary>
    /// 一番上を「変更の種類」にすると、すべての月からその種類の通知を新しい順に並べる（open 146・ユーザ判断 2026-11-19）。
    /// 種類の欄は数の多い順に件数付きで、最初の種類を選んで開く。「商品名で絞る」も効く
    /// </summary>
    [Fact]
    public Task 変更の種類で切り替えると全部の月から並べる() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("sep-update", At(9, 1)),
            Record("sep-tag", At(9, 2), "タグ「作り物」", NotificationKind.OrphanTag) with { ItemId = null },
        ]);
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("oct-update", At(10, 1), "作り物の髪型"),
            Record("oct-update2", At(10, 2)),
        ]);
        var (_, archive) = await OpenArchiveAsync(app);

        archive.IsKindLevel = true;
        await archive.Pending;

        Assert.Equal(["商品ページの変更（3）", "タグの参照切れ（1）"], archive.Kinds.Select(kind => kind.Label));
        Assert.Equal(NotificationKind.ItemUpdated, archive.SelectedKind!.Kind);
        Assert.Equal(["oct-update2", "oct-update", "sep-update"], Ids(archive));
        Assert.Equal("3 件", archive.CountText);
        Assert.Equal("商品ページの変更（3）", archive.HeadLabel);
        Assert.True(archive.ShowsNameFilter);

        archive.NameFilter = "髪型";
        await archive.Pending;
        Assert.Equal(["oct-update"], Ids(archive));
        Assert.Equal("3 件中 1 件", archive.CountText);

        archive.SelectedKind = archive.Kinds[1];
        await archive.Pending;
        Assert.Empty(Ids(archive));

        archive.NameFilter = string.Empty;
        await archive.Pending;
        Assert.Equal(["sep-tag"], Ids(archive));

        // 年月へ戻すと、選んでいた月の見方に戻る
        archive.IsMonthLevel = true;
        await archive.Pending;
        Assert.Equal(["oct-update2", "oct-update"], Ids(archive));
    });

    /// <summary>
    /// 初めて「商品」へ切り替えて全部の月の目録を作っている間に「変更の種類」へ切り替えても、種類の欄と一覧が食い違わない
    /// （2026-11-19 の Codex の点検 3回目の 2：後の読みが、最初の種類を選ぶ前に全部の種類で絞っていた）。順は gate で決める
    /// </summary>
    [Fact]
    public Task 目録を作っている間に種類へ切り替えても_選んだ種類だけを並べる() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("sep-update", At(9, 1)),
            Record("sep-tag", At(9, 2), "タグ「作り物」", NotificationKind.OrphanTag) with { ItemId = null },
        ]);
        await app.Store.NotificationArchive.AppendAsync([Record("oct-update", At(10, 1))]);
        var (_, archive) = await OpenArchiveAsync(app);

        var entered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        archive.CatalogBuiltForTests = () =>
        {
            entered.TrySetResult();
            return gate.Task;
        };

        archive.IsItemLevel = true;
        var itemLoad = archive.Pending;
        await entered.Task;
        archive.IsKindLevel = true;
        var kindLoad = archive.Pending;
        gate.SetResult();
        await itemLoad;
        await kindLoad;

        Assert.Equal(NotificationKind.ItemUpdated, archive.SelectedKind!.Kind);
        Assert.Equal(["oct-update", "sep-update"], Ids(archive));
        Assert.Equal("2 件", archive.CountText);
    });

    /// <summary>
    /// 一番上を「商品」にすると、商品を選ぶまでは何も並べず、選ぶとすべての月からその商品の通知を新しい順に並べる。
    /// 候補は商品の知らせの題（商品名）。同じ名前の別の商品は ID を添えて分ける
    /// </summary>
    [Fact]
    public Task 商品で切り替えると選んだ商品の通知を全部の月から並べる() => TestApp.Run(async app =>
    {
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("sep-a", At(9, 1)),
            Record("sep-twin", At(9, 2)) with { ItemId = "9900602" },
        ]);
        await app.Store.NotificationArchive.AppendAsync(
        [
            Record("oct-a", At(10, 1)),
            Record("oct-b", At(10, 2), "作り物の髪型") with { ItemId = "9900603" },
        ]);
        var (_, archive) = await OpenArchiveAsync(app);

        archive.IsItemLevel = true;
        await archive.Pending;

        Assert.Empty(Ids(archive));
        Assert.Equal("商品を選んでください", archive.EmptyTitle);
        Assert.False(archive.ShowsNameFilter);
        Assert.Equal(
            ["作り物の衣装（9900601）", "作り物の衣装（9900602）", "作り物の髪型"],
            archive.ItemCandidates.Order(StringComparer.Ordinal));

        archive.PickItemCommand.Execute("作り物の衣装（9900601）");
        await archive.Pending;

        Assert.Equal(["oct-a", "sep-a"], Ids(archive));
        Assert.Equal("作り物の衣装（9900601）", archive.HeadLabel);
        Assert.Equal("2 件", archive.CountText);
    });
}
