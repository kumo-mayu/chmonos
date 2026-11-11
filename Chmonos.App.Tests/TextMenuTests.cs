using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの説明文の右クリック（計画 29）。何が並ぶか・押せるか・選んだ範囲から何を拾うか・窓に何が並ぶか。
/// メニューそのものは別の窓に出るので撮れない。ここでは組んだ項目を開かずに読む。番号・名前・URL は作り物。
/// </summary>
public class TextMenuTests
{
    private const string ItemLink = "https://booth.pm/ja/items/1000001";
    private const string ShopItemLink = "https://sample-shop.booth.pm/items/1000002";
    private const string ShopLink = "https://sample-shop.booth.pm/";
    private const string OtherLink = "https://example.com/page";

    /// <summary>説明文の形：商品のリンク2つ・ショップ・BOOTH の画像・BOOTH の外のリンク。</summary>
    private const string Description =
        "セットの中身\n"
        + "上着 " + ItemLink + "\n"
        + "スカート " + ShopItemLink + " と " + ShopLink + "\n"
        + "見本 https://booth.pximg.net/c/72x72/abc/i/1000009/def_base_resized.jpg\n"
        + "使い方 " + OtherLink;

    /// <summary>テストだけの相手。ライブラリにある商品と、押した結果を覚える。</summary>
    private sealed class Host(params string[] inLibrary) : ITextMenuHost
    {
        public List<string> Opened { get; } = [];

        public List<IReadOnlyList<string>> Registered { get; } = [];

        public bool IsInLibrary(string itemId) => inLibrary.Contains(itemId);

        public void OpenItem(string itemId) => Opened.Add(itemId);

        public void RegisterItems(IReadOnlyList<string> itemIds) => Registered.Add(itemIds);
    }

    /// <summary>並びを「見出し(押せない印)」で、区切りを「|」で書く。</summary>
    private static string Show(TextMenuPlan plan)
        => string.Join(" / ", plan.Entries.Select(entry => entry is null ? "|" : entry.IsEnabled ? entry.Header : $"({entry.Header})"));

    private static TextMenuPlan Plan(string? link, string selected, ITextMenuHost? host)
        => TextMenuPlan.Build(link, selected, selected.Length == 0 ? [] : [selected], host);

    // ---- 何が並ぶか ----

    [Fact]
    public void リンクの外で選んでいないときは_選んだ文の2つを押せなくして理由を言う()
    {
        var plan = Plan(null, string.Empty, new Host());

        Assert.Equal("(コピー) / (選んだ範囲の商品をまとめて登録…)", Show(plan));
        Assert.Equal("文字を選ぶとコピーできます。", plan.Entries[0]!.Tip);
        Assert.Equal("BOOTHの商品のリンクを含めて選ぶと登録できます。", plan.Entries[1]!.Tip);
    }

    [Fact]
    public void 選んだ範囲に商品があれば_まとめて登録を押せて_件数を言う()
    {
        var plan = Plan(null, $"上着 {ItemLink}\nスカート {ShopItemLink}", new Host());

        Assert.Equal("コピー / 選んだ範囲の商品をまとめて登録…", Show(plan));
        Assert.Equal("2 件の商品が見つかりました。", plan.Entries[1]!.Tip);
        Assert.Equal(["1000001", "1000002"], plan.SelectedItemIds);
    }

    [Fact]
    public void 選んだ範囲に商品が無ければ_コピーだけ押せる()
    {
        // ショップ・画像・BOOTH の外は商品として数えない（26 と同じ線）
        var plan = Plan(null, $"{ShopLink} https://booth.pximg.net/c/72x72/abc/i/1000009/x.jpg {OtherLink}", new Host());

        Assert.Equal("コピー / (選んだ範囲の商品をまとめて登録…)", Show(plan));
        Assert.Empty(plan.SelectedItemIds);
    }

    [Fact]
    public void 手元に無い商品のリンクでは_BOOTHで開く_リンクをコピー_商品として登録()
    {
        var plan = Plan(ItemLink, string.Empty, new Host());

        Assert.Equal("BOOTHで開く / リンクをコピー / | / 商品として登録… / | / (コピー) / (選んだ範囲の商品をまとめて登録…)", Show(plan));
        Assert.Equal("1000001", plan.LinkItemId);
        Assert.Equal("このリンクをブラウザで開きます。アプリの外へ出ます。", plan.Entries[0]!.Tip);
    }

    [Fact]
    public void 手元にある商品のリンクでは_登録の代わりに商品ページを開く()
    {
        var plan = Plan(ShopItemLink, string.Empty, new Host("1000002"));

        Assert.Equal("BOOTHで開く / リンクをコピー / | / 商品ページを開く / | / (コピー) / (選んだ範囲の商品をまとめて登録…)", Show(plan));
    }

    [Fact]
    public void BOOTHの商品でないリンクでは_登録を灰色にして理由を言う()
    {
        var shop = Plan(ShopLink, string.Empty, new Host());
        Assert.Equal("BOOTHで開く / リンクをコピー / | / (商品として登録…) / | / (コピー) / (選んだ範囲の商品をまとめて登録…)", Show(shop));
        Assert.Equal("BOOTHの商品のリンクだけ登録できます。", shop.Entries[3]!.Tip);

        // BOOTH の外は行き先がブラウザだと名前で言う（「BOOTHで開く」は BOOTH のページだけ）
        var other = Plan(OtherLink, string.Empty, new Host());
        Assert.Equal("ブラウザで開く / リンクをコピー / | / (商品として登録…) / | / (コピー) / (選んだ範囲の商品をまとめて登録…)", Show(other));
    }

    [Fact]
    public void 商品を扱う相手が無ければ_開く登録の項目を出さず_区切りも続けない()
    {
        Assert.Equal("BOOTHで開く / リンクをコピー / | / (コピー)", Show(Plan(ItemLink, string.Empty, null)));
        Assert.Equal("コピー", Show(Plan(null, "上着", null)));
    }

    [Fact]
    public void どの組み合わせでも_区切りは先頭と末尾に来ず_続かない()
    {
        foreach (var link in new[] { null, ItemLink, ShopLink, OtherLink })
        {
            foreach (var selected in new[] { string.Empty, "上着", ItemLink })
            {
                foreach (var host in new ITextMenuHost?[] { null, new Host(), new Host("1000001") })
                {
                    var entries = Plan(link, selected, host).Entries;
                    Assert.NotNull(entries[0]);
                    Assert.NotNull(entries[^1]);
                    Assert.DoesNotContain(entries.Zip(entries.Skip(1)), pair => pair.First is null && pair.Second is null);
                }
            }
        }
    }

    [Fact]
    public void 英字と日本語の間に空白を入れない()
    {
        var headers = new[] { ItemLink, ShopLink, OtherLink, null }
            .SelectMany(link => Plan(link, ItemLink, new Host()).Entries.Concat(Plan(link, string.Empty, new Host()).Entries))
            .Where(entry => entry is not null)
            .SelectMany(entry => new[] { entry!.Header, entry.Tip ?? string.Empty });

        Assert.DoesNotContain(headers, text => System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z] [ぁ-んァ-ヶ一-龠]|[ぁ-んァ-ヶ一-龠] [A-Za-z]"));
    }

    // ---- 選んだ範囲から何を拾うか（本物の本文の組み方で）----

    private static TextPointer At(FlowDocument document, string needle, int offset = 0)
    {
        for (var pointer = document.ContentStart; pointer is not null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text
                && pointer.GetTextInRun(LogicalDirection.Forward).IndexOf(needle, StringComparison.Ordinal) is var index and >= 0)
            {
                return pointer.GetPositionAtOffset(index + offset)!;
            }
        }

        throw new InvalidOperationException($"本文に「{needle}」がありません");
    }

    [Fact]
    public Task 全部を選ぶと_商品のリンクだけを出てきた順に拾う() => UiThread.Run(() =>
    {
        var document = SelectableText.Build(Description, navigator: null);

        var ids = TextMenuPlan.ItemIdsIn(TextMenuPlan.SelectedFragments(document, document.ContentStart, document.ContentEnd));

        Assert.Equal(["1000001", "1000002"], ids);
    });

    /// <summary>範囲の端が URL の途中で切れても、見えている字の番号（別の商品）ではなく、リンクの商品を拾う。登録は取り消せない。</summary>
    [Fact]
    public Task 範囲の端がリンクの途中で切れても_リンクの全体から拾う() => UiThread.Run(() =>
    {
        var document = SelectableText.Build(Description, navigator: null);

        // 「着 https://booth.pm/ja/items/1000」までと、「/items/1000002」の途中から
        var head = TextMenuPlan.SelectedFragments(document, At(document, "上着", 1), At(document, ItemLink, ItemLink.Length - 3));
        Assert.Equal(["1000001"], TextMenuPlan.ItemIdsIn(head));

        var tail = TextMenuPlan.SelectedFragments(document, At(document, ShopItemLink, ShopItemLink.Length - 2), At(document, " と ", 2));
        Assert.Equal(["1000002"], TextMenuPlan.ItemIdsIn(tail));
    });

    [Fact]
    public Task リンクでない字だけを選ぶと_何も拾わない() => UiThread.Run(() =>
    {
        var document = SelectableText.Build(Description, navigator: null);

        Assert.Empty(TextMenuPlan.ItemIdsIn(TextMenuPlan.SelectedFragments(document, At(document, "セット"), At(document, "上着", 2))));
        Assert.Empty(TextMenuPlan.SelectedFragments(document, document.ContentStart, document.ContentStart));
    });

    [Fact]
    public Task 右クリックした所のリンクを見分ける() => UiThread.Run(() =>
    {
        var document = SelectableText.Build(Description, navigator: null);

        Assert.Equal(ItemLink, TextMenuPlan.LinkAt(At(document, ItemLink, 5))?.NavigateUri.OriginalString);
        Assert.Null(TextMenuPlan.LinkAt(At(document, "セット", 1)));
        Assert.Null(TextMenuPlan.LinkAt(null));
    });

    // ---- 作りをほかの右クリックと共有している ----

    /// <summary>
    /// 項目はほかの右クリックと同じ部品で組む：既定の MenuItem の見た目（自分で Style・Template を持たない）・区切りは Separator・
    /// 押せないときの吹き出しは ToolTip に直に書き、既定の見た目の ShowOnDisabled で出す（SortDividerBorder と同じ）。読み上げの名前と AutomationId も付ける。
    /// </summary>
    [Fact]
    public Task メニューは既定の見た目のMenuItemと区切りで組み_押せない項目にも理由を出す() => UiThread.Run(async () =>
    {
        var menu = new ContextMenu();
        var host = new Host();
        TextMenu.Fill(menu, Plan(ShopLink, string.Empty, host), host);

        Assert.Equal(7, menu.Items.Count);
        Assert.IsType<Separator>(menu.Items[2]);
        Assert.IsType<Separator>(menu.Items[4]);

        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.All(items, item =>
        {
            Assert.Equal(DependencyProperty.UnsetValue, item.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Equal(DependencyProperty.UnsetValue, item.ReadLocalValue(Control.TemplateProperty));
            Assert.Equal(item.Header, AutomationProperties.GetName(item));
        });

        var register = items.Single(item => AutomationProperties.GetAutomationId(item) == "TextMenu.RegisterLink");
        Assert.False(register.IsEnabled);

        // 見た目は並べたときに当たるので、窓に載せずに並べる（メニューは開かない：開くと画面に出る）
        menu.Measure(new Size(600, 600));
        menu.Arrange(new Rect(0, 0, 600, 600));
        menu.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        // 既定の見た目（Themes/Controls.xaml）が当たっていて、押せない項目にも吹き出しを出す
        Assert.NotNull(register.Style);
        Assert.True(ToolTipService.GetShowOnDisabled(register));
        Assert.Equal("BOOTHの商品のリンクだけ登録できます。", register.ToolTip);
    });

    /// <summary>リンクにする本文（説明文・メモ）にだけ付け、名前やパスの短い字は RichTextBox の既定のまま。</summary>
    [Fact]
    public Task リンクにする本文にだけメニューを付ける() => UiThread.Run(() =>
    {
        var body = new RichTextBox();
        SelectableText.SetText(body, Description);
        Assert.NotNull(body.ContextMenu);

        var name = new RichTextBox();
        SelectableText.SetPlainText(name, true);
        SelectableText.SetText(name, "作り物の上着");
        Assert.Null(name.ContextMenu);
    });

    /// <summary>商品ページの形：左クリックの行き先がメニューの相手を兼ねる。</summary>
    private sealed class PageHost : IInAppLinkNavigator, ITextMenuHost
    {
        public bool CanNavigate(Uri uri) => false;

        public void Navigate(Uri uri)
        {
        }

        public bool IsInLibrary(string itemId) => itemId == "1000002";

        public void OpenItem(string itemId)
        {
        }

        public void RegisterItems(IReadOnlyList<string> itemIds)
        {
        }
    }

    [Fact]
    public Task 開くときは_箱の選んだ範囲と右クリックしたリンクと相手から組む() => UiThread.Run(() =>
    {
        // 商品ページの形：Navigator だけを結ぶ
        var page = new PageHost();
        var body = new RichTextBox();
        SelectableText.SetNavigator(body, page);
        SelectableText.SetText(body, Description);
        body.Selection.Select(At(body.Document, "上着"), At(body.Document, " と "));

        var (plan, host) = TextMenu.Compose(body, TextMenuPlan.LinkAt(At(body.Document, ShopItemLink, 3)));
        Assert.Same(page, host);
        Assert.Equal("BOOTHで開く / リンクをコピー / | / 商品ページを開く / | / コピー / 選んだ範囲の商品をまとめて登録…", Show(plan));
        Assert.Equal(["1000001", "1000002"], plan.SelectedItemIds);
        Assert.StartsWith("上着 https://booth.pm/ja/items/1000001", plan.SelectedText, StringComparison.Ordinal);

        // 編集画面の形：MenuHost だけを結ぶ（左クリックの行き先は無いまま）
        var edit = new RichTextBox();
        var editHost = new Host();
        SelectableText.SetMenuHost(edit, editHost);
        SelectableText.SetText(edit, Description);
        var (editPlan, editTarget) = TextMenu.Compose(edit, null);
        Assert.Same(editHost, editTarget);
        Assert.Null(SelectableText.GetNavigator(edit));
        Assert.Equal("(コピー) / (選んだ範囲の商品をまとめて登録…)", Show(editPlan));
    });

    [Fact]
    public Task 押された字からリンクをたどる() => UiThread.Run(() =>
    {
        var document = SelectableText.Build(Description, navigator: null);
        var link = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<Hyperlink>().First();

        Assert.Same(link, TextMenu.LinkFrom(link.Inlines.FirstInline));
        Assert.Same(link, TextMenu.LinkFrom(link));
        Assert.Null(TextMenu.LinkFrom(document.Blocks.FirstBlock));
        Assert.Null(TextMenu.LinkFrom(new RichTextBox()));
        Assert.Null(TextMenu.LinkFrom(null));
    });

    // ---- 押したとき ----

    [Fact]
    public void 押すと_相手に商品を渡す()
    {
        var host = new Host("1000002");

        TextMenu.Run(TextMenuAction.RegisterLink, Plan(ItemLink, string.Empty, host), host);
        TextMenu.Run(TextMenuAction.OpenItem, Plan(ShopItemLink, string.Empty, host), host);
        TextMenu.Run(TextMenuAction.RegisterSelection, Plan(null, $"{ItemLink} {ShopItemLink}", host), host);

        // 押せない並び（商品の無いリンク・商品の無い範囲）では、押されても何もしない
        TextMenu.Run(TextMenuAction.RegisterLink, Plan(ShopLink, string.Empty, host), host);
        TextMenu.Run(TextMenuAction.RegisterSelection, Plan(null, "上着", host), host);

        Assert.Equal(["1000002"], host.Opened);
        Assert.Equal(2, host.Registered.Count);
        Assert.Equal(["1000001"], host.Registered[0]);
        Assert.Equal(["1000001", "1000002"], host.Registered[1]);
    }

    // ---- 商品ページから 26 の窓へ ----

    [Fact]
    public Task 商品ページで選んだ範囲を登録すると_26の窓に並び_手元にある物はチェックを外す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000005", "作り物のセット"));
        await app.AddItemAsync(Make.Item("1000002", "作り物のスカート"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("1000005"))!);
        ITextMenuHost page = main.CurrentItemPage!;

        TextMenu.Run(TextMenuAction.RegisterSelection, Plan(null, $"{ItemLink}\n{ShopItemLink}", page), page);
        await UiThread.Until(() => app.PastedItemDialogs.Count == 1, "一覧の窓が出る");

        var dialog = app.PastedItemDialogs[0];
        Assert.Equal(["1000001", "1000002"], dialog.Rows.Select(row => row.ItemId));
        Assert.Equal([true, false], dialog.Rows.Select(row => row.IsChecked));
        Assert.Equal("2 件の商品が見つかりました。登録する商品を選んでください。", dialog.HeadingText);

        // キャンセル（既定の答え）なら BOOTH に問い合わせない
        await app.SettleAsync();
        Assert.Empty(app.Booth.Requests);
    });

    /// <summary>1件でも窓を通す（通信と取り消せないことを、押す前に言う窓）。見出しは1件の言い方にする。</summary>
    [Fact]
    public Task リンク1つの商品として登録も_同じ窓を1件で通し_登録は26と同じ道() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の上着");
        app.AnswerPastedItems = _ => true;
        await app.AddItemAsync(Make.Item("1000005", "作り物のセット"));
        var main = await app.StartAsync();
        main.ShowItem((await app.Store.Items.LoadAsync("1000005"))!);
        ITextMenuHost page = main.CurrentItemPage!;

        TextMenu.Run(TextMenuAction.RegisterLink, Plan(ItemLink, string.Empty, page), page);
        await UiThread.Until(() => app.PastedItemDialogs.Count == 1, "一覧の窓が出る");
        await UiThread.Until(() => main.RegisteredNoticeText is not null, "登録が済む");
        await app.SettleAsync();

        var dialog = app.PastedItemDialogs[0];
        Assert.Equal("商品を登録", dialog.Title);
        Assert.Equal("この商品を登録します。", dialog.HeadingText);
        Assert.Equal("1 件を登録", dialog.CommitText);
        Assert.NotNull(await app.Store.Items.LoadAsync("1000001"));
        Assert.All(app.Booth.Requests, url => Assert.DoesNotContain("1000005", url, StringComparison.Ordinal));
    });

    [Fact]
    public void 一件で手元にあるときの見出し()
    {
        var model = new RegisterPastedItemsDialogViewModel([("1000002", "作り物のスカート")]);

        Assert.Equal("この商品は、ライブラリにあります。", model.HeadingText);
        Assert.False(model.CanCommit);
    }
}
