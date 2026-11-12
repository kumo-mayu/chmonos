using System.ComponentModel;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// Ctrl・Shift+クリックでの選び方を Explorer に揃える（ユーザ指示 2026-11-12「Ctrl-クリック・Shift-クリックで複数選択に移れていない」）。
/// どれを選ぶかは計算で決まるので、画面の入口（<see cref="ICardClickSelectionHost.SelectByClick"/>）を直に押して確かめる。
/// 修飾キーを読むのはカードのクリックの処理（ItemCardResources）だけで、そこはマウスで確かめる
/// </summary>
public sealed class CardClickSelectionTests
{
    private static async Task<SearchViewModel> SearchWithAsync(TestApp app, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}"));
        }

        var search = (await app.StartAsync()).Search;
        await app.SettleAsync();
        Assert.Equal(count, search.ListItems.Count);
        return search;
    }

    private static List<int> SelectedIndexes(IReadOnlyList<ItemCardViewModel> order)
        => [.. order.Select((card, index) => (card, index)).Where(pair => pair.card.IsSelected).Select(pair => pair.index)];

    private static int CountNotices(INotifyPropertyChanged model, Action act)
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

    // ---- 範囲の決め方 ----

    [Fact]
    public Task 範囲は起点から押した物までで_上下どちらからでも両端を含む() => TestApp.Run(async app =>
    {
        var order = (await SearchWithAsync(app, 6)).ListItems.ToList();
        Assert.Equal(order[1..5], CardClickSelection.Range(order, order[1], order[4]));
        Assert.Equal(order[1..5], CardClickSelection.Range(order, order[4], order[1]));
        Assert.Equal([order[3]], CardClickSelection.Range(order, order[3], order[3]));
    });

    [Fact]
    public Task 起点が無い_または並びに居ないときは押した1件だけ() => TestApp.Run(async app =>
    {
        var all = (await SearchWithAsync(app, 6)).ListItems.ToList();
        var order = all[..5];
        Assert.Equal([order[2]], CardClickSelection.Range(order, null, order[2]));

        // 絞り込みで並びから消えた起点
        Assert.Equal([order[2]], CardClickSelection.Range(order, all[5], order[2]));
    });

    // ---- 検索の画面 ----

    [Fact]
    public Task 修飾キーが無ければ何もしない_呼ぶ側が今までどおり商品ページを開く() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 5);

        Assert.False(search.SelectByClick(search.ListItems[1], control: false, shift: false));
        Assert.False(search.HasSelection);
    });

    [Fact]
    public Task Ctrlクリックは選んでいなくても選び始め_もう一度で外す() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 5);
        var cards = search.ListItems;

        Assert.True(search.SelectByClick(cards[1], control: true, shift: false));
        Assert.True(search.HasSelection);
        Assert.All(cards, card => Assert.True(card.IsSelectionMode));

        Assert.True(search.SelectByClick(cards[3], control: true, shift: false));
        Assert.Equal([1, 3], SelectedIndexes(cards));

        // 外しても起点は移る（Explorer と同じ）。続く Shift はそこから
        Assert.True(search.SelectByClick(cards[3], control: true, shift: false));
        Assert.Equal([1], SelectedIndexes(cards));
        search.SelectByClick(cards[4], control: false, shift: true);
        Assert.Equal([3, 4], SelectedIndexes(cards));
    });

    [Fact]
    public Task Shiftクリックは起点からの範囲だけにし_起点は動かず範囲を縮められる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 30);
        var cards = search.ListItems;

        search.SelectByClick(cards[0], control: true, shift: false);
        search.SelectByClick(cards[10], control: true, shift: false);

        // 前に選んだ cards[0] は外れる。仮想化で作られていない先のカードも、並び（全件）で取るので入る
        Assert.Equal(1, CountNotices(search, () => search.SelectByClick(cards[25], control: false, shift: true)));
        Assert.Equal([.. Enumerable.Range(10, 16)], SelectedIndexes(cards));

        // 起点は cards[10] のまま。上へ押せば上へ伸び、下の分は外れる
        search.SelectByClick(cards[7], control: false, shift: true);
        Assert.Equal([7, 8, 9, 10], SelectedIndexes(cards));
    });

    [Fact]
    public Task CtrlとShiftを押しながらは_今の選択に範囲を足す() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 12);
        var cards = search.ListItems;

        search.SelectByClick(cards[1], control: true, shift: false);
        search.SelectByClick(cards[3], control: false, shift: true);
        search.SelectByClick(cards[8], control: true, shift: false);
        search.SelectByClick(cards[10], control: true, shift: true);

        Assert.Equal([1, 2, 3, 8, 9, 10], SelectedIndexes(cards));
    });

    [Fact]
    public Task 起点が無いShiftクリックはその1件だけを選び_そこが起点になる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 8);
        var cards = search.ListItems;

        search.SelectByClick(cards[5], control: false, shift: true);
        Assert.Equal([5], SelectedIndexes(cards));

        search.SelectByClick(cards[2], control: false, shift: true);
        Assert.Equal([2, 3, 4, 5], SelectedIndexes(cards));
    });

    [Fact]
    public Task 選択を全部外すと起点も忘れる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 8);
        var cards = search.ListItems;

        search.SelectByClick(cards[1], control: true, shift: false);
        search.ClearSelection();

        search.SelectByClick(cards[6], control: false, shift: true);
        Assert.Equal([6], SelectedIndexes(cards));
    });

    [Fact]
    public Task チェックや選んでいる最中のクリックで選んだ物も_Shiftの起点になる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 8);
        var cards = search.ListItems;

        // 左上のチェック（バインドで IsSelected が変わり、押した知らせで起点を覚える）
        cards[2].IsSelected = true;
        search.NoteSelectionAnchor(cards[2]);

        search.SelectByClick(cards[5], control: false, shift: true);
        Assert.Equal([2, 3, 4, 5], SelectedIndexes(cards));
    });

    [Fact]
    public Task 選べないカードは扱わず_呼ぶ側に任せる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 3);
        var card = search.ListItems[1];
        card.CanSelect = false;

        Assert.False(search.SelectByClick(card, control: true, shift: false));
        Assert.False(search.HasSelection);
    });

    // ---- 同じカードと帯を使うほかの画面 ----

    [Fact]
    public Task ショップの中でも同じに選べる() => TestApp.Run(async app =>
    {
        for (var index = 1; index <= 6; index++)
        {
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}"));
        }

        var main = await app.StartAsync();
        await main.ShowShopAsync("sample-shop");
        var shop = Assert.IsType<ShopViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => shop.ListItems.OfType<ItemCardViewModel>().Count() == 6, "ショップの商品が並ぶ");
        var cards = shop.ListItems.OfType<ItemCardViewModel>().ToList();

        Assert.True(shop.SelectByClick(cards[1], control: true, shift: false));
        Assert.True(shop.HasSelection);
        shop.SelectByClick(cards[4], control: false, shift: true);
        Assert.Equal([1, 2, 3, 4], SelectedIndexes(cards));
        Assert.Equal(4, shop.SelectedCount);
    });

    [Fact]
    public Task フォルダの表示の右の欄でも同じに選べる() => TestApp.Run(async app =>
    {
        for (var index = 1; index <= 5; index++)
        {
            var file = app.NewFile($@"shop\作り物{index}.zip");
            await app.AddItemAsync(Make.Item($"10000{index:00}", $"作り物の商品{index}").WithFiles(Make.File(file)));
        }

        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.Rows.Count > 0, "フォルダビューの読み込みが済む");
        var shopFolder = System.IO.Path.Combine(app.Root, "files", "shop");
        folders.Selected = folders.Rows.FirstOrDefault(row => string.Equals(row.Path.TrimEnd('\\'), shopFolder, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(string.Join(" | ", folders.Rows.Select(row => $"{row.Kind}:{row.Path}")));
        await UiThread.Until(
            () => folders.Detail is FolderViewDetail detail && detail.ListItems.OfType<ItemCardViewModel>().Count() == 5,
            "フォルダの右の欄に商品が並ぶ");
        var detail = (FolderViewDetail)folders.Detail!;
        var cards = detail.ListItems.OfType<ItemCardViewModel>().ToList();

        Assert.True(detail.SelectByClick(cards[0], control: true, shift: false));
        detail.SelectByClick(cards[2], control: false, shift: true);
        Assert.Equal([0, 1, 2], SelectedIndexes(cards));
        Assert.Equal(3, detail.SelectedCount);
    });
}
