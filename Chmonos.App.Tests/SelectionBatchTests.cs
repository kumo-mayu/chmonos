using System.ComponentModel;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// まとめて選ぶ・外す間は1件ごとに知らせず、最後に1回だけ知らせる（外部の点検 2026-10-07）。
/// 前は1件ごとに全部のカードへ「選ぶ操作中か」を配り直し、件数も数え直したので、2万件で約4億回になり画面が止まった。
/// 回数は時計に左右されないので、知らせの数で確かめる
/// </summary>
public sealed class SelectionBatchTests
{
    private static int CountSelectedCountNotices(INotifyPropertyChanged model, Action act, string property = "SelectedCount")
    {
        var count = 0;
        void Handler(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == property)
            {
                count++;
            }
        }

        model.PropertyChanged += Handler;
        try
        {
            act();
        }
        finally
        {
            model.PropertyChanged -= Handler;
        }

        return count;
    }

    [Fact]
    public Task 検索で全部を選ぶのと選択を外すのは_件数の知らせが1回だけ() => TestApp.Run(async app =>
    {
        for (var index = 1; index <= 30; index++)
        {
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}"));
        }

        var search = (await app.StartAsync()).Search;
        await app.SettleAsync();

        Assert.Equal(1, CountSelectedCountNotices(search, () => search.SelectAllCommand.Execute(null)));
        Assert.Equal(30, search.SelectedCount);
        Assert.True(search.HasSelection);

        Assert.Equal(1, CountSelectedCountNotices(search, search.ClearSelection));
        Assert.Equal(0, search.SelectedCount);
        Assert.False(search.HasSelection);
    });

    [Fact]
    public Task 未確定で全部を選ぶのと選択を外すのは_件数の知らせが1回だけ() => TestApp.Run(async app =>
    {
        await app.Store.Unresolved.SaveAsync([.. Enumerable.Range(1, 30).Select(index =>
        {
            var path = app.NewFile($"file{index:00}.zip");
            return new UnresolvedFile
            {
                Hash = Make.HashOf(path),
                Paths = [path],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            };
        })]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);

        Assert.Equal(1, CountSelectedCountNotices(resolve, () => resolve.SelectAllCommand.Execute(null), "CheckedCount"));
        Assert.Equal(30, resolve.CheckedCount);

        Assert.Equal(1, CountSelectedCountNotices(resolve, () => resolve.ClearChecksCommand.Execute(null), "CheckedCount"));
        Assert.Equal(0, resolve.CheckedCount);
    });

    [Fact]
    public Task 取り込みで展開したフォルダを全部選ぶのと外すのは_件数の知らせが1回だけ() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var import = main.Import;
        for (var index = 1; index <= 30; index++)
        {
            import.AddUnpackedRow(new Chmonos.Core.Scanning.UnpackedFolder
            {
                Path = System.IO.Path.Combine(app.Root, $"unpacked{index:00}"),
                ArchivePath = System.IO.Path.Combine(app.Root, $"unpacked{index:00}.zip"),
                FileCount = 1,
                TotalBytes = 3,
            });
        }

        Assert.Equal(1, CountSelectedCountNotices(import, () => import.SelectAllUnpackedCommand.Execute(null), "SelectedUnpackedCount"));
        Assert.Equal(30, import.SelectedUnpackedCount);

        // もう一度押すと全部外す（押すたびに切り替える）
        Assert.Equal(1, CountSelectedCountNotices(import, () => import.SelectAllUnpackedCommand.Execute(null), "SelectedUnpackedCount"));
        Assert.Equal(0, import.SelectedUnpackedCount);
    });
}
