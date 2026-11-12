using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索・フォルダの右側・ショップの、キーでの全選択（Ctrl+A）・全解除（Ctrl+Shift+A）・反転（Ctrl+Shift+I）（ユーザ判断 2026-11-12）。
/// どのキーで何をするか・入力欄で譲るか・何が選ばれるかは計算で決まるので、窓のキーの処理が呼ぶ <see cref="SelectionKeys"/> を直に呼んで確かめる。
/// 窓が実際のキーを受けて呼ぶ所（MainWindow.TrySelectionKey）は、実機のキーで確かめる
/// </summary>
public sealed class CardKeySelectionTests
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

    private static bool Press(ICardClickSelectionHost host, Key key, ModifierKeys modifiers)
        => SelectionKeys.Run(SelectionKeys.Classify(key, modifiers), host);

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

    // ---- キーの読み方と、譲る所 ----

    [Fact]
    public void キーは_CtrlAで全選択_CtrlShiftAで全解除_CtrlShiftIで反転_ほかは受けない()
    {
        Assert.Equal(SelectionKey.SelectAll, SelectionKeys.Classify(Key.A, ModifierKeys.Control));
        Assert.Equal(SelectionKey.ClearAll, SelectionKeys.Classify(Key.A, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(SelectionKey.Invert, SelectionKeys.Classify(Key.I, ModifierKeys.Control | ModifierKeys.Shift));

        // Ctrl+D（Explorer のごみ箱へ送る）・Ctrl+I（斜体・情報）は使わない。Alt が混じる組み合わせは AltGr の文字入力にもなる
        Assert.Equal(SelectionKey.None, SelectionKeys.Classify(Key.D, ModifierKeys.Control));
        Assert.Equal(SelectionKey.None, SelectionKeys.Classify(Key.I, ModifierKeys.Control));
        Assert.Equal(SelectionKey.None, SelectionKeys.Classify(Key.A, ModifierKeys.Control | ModifierKeys.Alt));
        Assert.Equal(SelectionKey.None, SelectionKeys.Classify(Key.A, ModifierKeys.None));
        Assert.Equal(SelectionKey.None, SelectionKeys.Classify(Key.A, ModifierKeys.Shift));
    }

    [Fact]
    public Task 入力欄と選べる文の中では_文字の操作に譲る() => UiThread.Run(() =>
    {
        Assert.True(SelectionKeys.IsInTextInput(new TextBox()));
        Assert.True(SelectionKeys.IsInTextInput(new PasswordBox()));
        Assert.True(SelectionKeys.IsInTextInput(new ComboBox()));
        Assert.True(SelectionKeys.IsInTextInput(new SelectableLink()));

        // 説明などの選べる文（読み取り専用の RichTextBox）と、その中のリンク（部品でなく文の一部）
        var link = new Hyperlink(new Run("https://sample.invalid/guide"));
        var body = new RichTextBox(new FlowDocument(new Paragraph(link))) { IsReadOnly = true };
        Assert.True(SelectionKeys.IsInTextInput(body));
        Assert.True(SelectionKeys.IsInTextInput(link));

        // 開いた選ぶ欄の項目
        var combo = new ComboBox();
        var item = new ComboBoxItem();
        combo.Items.Add(item);
        Assert.True(SelectionKeys.IsInTextInput(item));

        // 候補付きの欄（SuggestBox）は中の TextBox に焦点が行く
        Assert.True(SelectionKeys.IsInTextInput(new SuggestBox().Input));

        var panel = new StackPanel();
        panel.Children.Add(new Button());

        Assert.False(SelectionKeys.IsInTextInput(new Button()));
        Assert.False(SelectionKeys.IsInTextInput(panel));
        Assert.False(SelectionKeys.IsInTextInput(null));
    });

    // ---- 検索の画面 ----

    [Fact]
    public Task CtrlAは並び全部を1回の知らせで選び_帯が出て_起点は動かない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 30);
        var cards = search.ListItems;
        search.SelectByClick(cards[10], control: true, shift: false);
        search.SelectByClick(cards[10], control: true, shift: false);
        Assert.False(search.HasSelection);

        // 仮想化で作られていない先のカードも、並び（全件）で取るので入る
        Assert.Equal(1, CountNotices(search, () => Assert.True(Press(search, Key.A, ModifierKeys.Control))));
        Assert.Equal([.. Enumerable.Range(0, 30)], SelectedIndexes(cards));
        Assert.True(search.ShowsSelectionBar);
        Assert.All(cards, card => Assert.True(card.IsSelectionMode));

        // 起点は全選択の前の cards[10] のまま（Explorer の Ctrl+A も焦点を動かさない）
        search.SelectByClick(cards[12], control: false, shift: true);
        Assert.Equal([10, 11, 12], SelectedIndexes(cards));

        // 起点が無いまま全選択したときは、次の Shift+クリックは押した1件だけ（全部外した後の Shift と同じ）
        search.ClearSelection();
        Press(search, Key.A, ModifierKeys.Control);
        search.SelectByClick(cards[20], control: false, shift: true);
        Assert.Equal([20], SelectedIndexes(cards));
    });

    [Fact]
    public Task CtrlShiftAは全部外して帯を消し_起点も忘れる_選んでいなければ譲る() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 8);
        var cards = search.ListItems;

        // 選んでいないときは何もせず譲る（Esc と同じ）
        Assert.False(Press(search, Key.A, ModifierKeys.Control | ModifierKeys.Shift));

        search.SelectByClick(cards[2], control: true, shift: false);
        search.SelectByClick(cards[5], control: true, shift: false);
        Assert.True(Press(search, Key.A, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(search.HasSelection);
        Assert.False(search.ShowsSelectionBar);

        search.SelectByClick(cards[6], control: false, shift: true);
        Assert.Equal([6], SelectedIndexes(cards));
    });

    [Fact]
    public Task CtrlShiftIは並びの選び方を1回の知らせで反転し_選んでいなければ全部を選ぶ() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 6);
        var cards = search.ListItems;

        search.SelectByClick(cards[1], control: true, shift: false);
        search.SelectByClick(cards[3], control: true, shift: false);
        Assert.Equal(1, CountNotices(search, () => Assert.True(Press(search, Key.I, ModifierKeys.Control | ModifierKeys.Shift))));
        Assert.Equal([0, 2, 4, 5], SelectedIndexes(cards));

        // 起点は反転の前の cards[3] のまま
        search.SelectByClick(cards[5], control: false, shift: true);
        Assert.Equal([3, 4, 5], SelectedIndexes(cards));

        search.ClearSelection();
        Assert.True(Press(search, Key.I, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal([.. Enumerable.Range(0, 6)], SelectedIndexes(cards));
    });

    [Fact]
    public Task 反転で1件も残らなければ_起点を忘れる() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 6);
        var cards = search.ListItems;

        Press(search, Key.A, ModifierKeys.Control);
        search.SelectByClick(cards[4], control: true, shift: false);
        search.SelectByClick(cards[4], control: true, shift: false);
        Press(search, Key.I, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.False(search.HasSelection);

        search.SelectByClick(cards[1], control: false, shift: true);
        Assert.Equal([1], SelectedIndexes(cards));
    });

    [Fact]
    public Task 絞り込みで隠れて選ばれている物は_全選択も反転も触らない() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 5);
        var hidden = search.ListItems[1];
        search.SelectByClick(hidden, control: true, shift: false);

        search.QueryText = "作り物の商品1";
        await UiThread.Until(() => search.ListItems.Count == 1, "絞り込みが効く");
        var shown = search.ListItems[0];

        Press(search, Key.I, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(shown.IsSelected);
        Assert.True(hidden.IsSelected);

        Press(search, Key.I, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.False(shown.IsSelected);
        Assert.True(hidden.IsSelected);

        Press(search, Key.A, ModifierKeys.Control);
        Assert.True(shown.IsSelected);
        Assert.True(hidden.IsSelected);

        // 外すのは隠れた物も（帯の「選択を解除」と同じ）
        Press(search, Key.A, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.False(hidden.IsSelected);
    });

    [Fact]
    public Task 帯のすべて選択も_キーと同じ入口を通る() => TestApp.Run(async app =>
    {
        var search = await SearchWithAsync(app, 4);

        Assert.Equal(1, CountNotices(search, () => search.SelectAllCommand.Execute(null)));
        Assert.Equal([0, 1, 2, 3], SelectedIndexes(search.ListItems));
    });

    // ---- 同じ帯を使うほかの画面 ----

    [Fact]
    public Task ショップの中でも_全選択と反転と全解除が効く() => TestApp.Run(async app =>
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
        var host = SelectionKeys.HostOf(main.CurrentViewModel);
        Assert.Same(shop, host);

        Assert.Equal(1, CountNotices(shop, () => Press(host!, Key.A, ModifierKeys.Control)));
        Assert.Equal(6, shop.SelectedCount);
        Assert.True(shop.ShowsSelectionBar);

        shop.SelectByClick(cards[0], control: true, shift: false);
        shop.SelectByClick(cards[3], control: true, shift: false);
        Assert.Equal(1, CountNotices(shop, () => Press(host!, Key.I, ModifierKeys.Control | ModifierKeys.Shift)));
        Assert.Equal([0, 3], SelectedIndexes(cards));

        Assert.True(Press(host!, Key.A, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(shop.HasSelection);
        Assert.False(shop.ShowsSelectionBar);
    });

    [Fact]
    public Task フォルダの表示では右の欄の商品に効く() => TestApp.Run(async app =>
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
        var host = SelectionKeys.HostOf(folders);
        Assert.Same(detail, host);

        Assert.Equal(1, CountNotices(detail, () => Press(host!, Key.A, ModifierKeys.Control)));
        Assert.Equal(5, detail.SelectedCount);

        detail.SelectByClick(cards[1], control: true, shift: false);
        Press(host!, Key.I, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal([1], SelectedIndexes(cards));

        Assert.True(Press(host!, Key.A, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(detail.HasSelection);
    });

    [Fact]
    public Task 帯の無い画面では受けない_未確定の画面のチェックもキーでは変えない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        // 未確定の画面も Esc で外せる（ISelectionScreen）が、カードの帯ではないのでキーの3つは決めていない（ユーザ判断は3つの画面）
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        Assert.Null(SelectionKeys.HostOf(main.CurrentViewModel));

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Null(SelectionKeys.HostOf(main.CurrentViewModel));
        Assert.Null(SelectionKeys.HostOf(null));
    });
}
