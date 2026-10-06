using System.ComponentModel;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// まとめて選ぶ・外す間は1件ごとに知らせず、最後に1回だけ知らせる（外部の点検 2026-10-07）。
/// 前は1件ごとに全部のカードへ「選ぶ操作中か」を配り直し、件数も数え直したので、2万件で約4億回になり画面が止まった。
/// 回数は時計に左右されないので、知らせの数で確かめる
/// </summary>
public sealed class SelectionBatchTests
{
    private static int CountSelectedCountNotices(INotifyPropertyChanged model, Action act)
    {
        var count = 0;
        void Handler(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "SelectedCount")
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
}
