using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using BoothZipInspector;
using Chmonos.Core.Booth;
using Chmonos.Core.Services;

namespace Chmonos.App.Controls;

/// <summary>
/// 説明文の右クリックから、リンクの商品を開く・登録する相手（計画 29）。商品ページ（<see cref="ViewModels.ItemViewModel"/>）が持つ。
/// 左クリックの行き先（<see cref="IInAppLinkNavigator"/>）と分けたのは、編集画面の説明文では左クリックの動きを変えずに、メニューだけ付けたいため。
/// </summary>
public interface ITextMenuHost
{
    /// <summary>ライブラリにあるか。ある商品のリンクは「商品ページを開く」にする。</summary>
    bool IsInLibrary(string itemId);

    void OpenItem(string itemId);

    /// <summary>見つけた商品を、まとめて登録する窓（計画 26 と同じ窓）に並べる。</summary>
    void RegisterItems(IReadOnlyList<string> itemIds);
}

/// <summary>メニューの項目の種類。押したときの動きを分ける。</summary>
internal enum TextMenuAction
{
    OpenLink,
    CopyLink,
    OpenItem,
    RegisterLink,
    Copy,
    RegisterSelection,
}

/// <summary>項目1つ。押せないときは <see cref="Tip"/> が理由（押せるときは何が起きるかの補い）。</summary>
internal sealed record TextMenuEntry(TextMenuAction Action, string Header, bool IsEnabled, string? Tip = null)
{
    public string AutomationId => $"TextMenu.{Action}";
}

/// <summary>
/// 説明文の右クリックに何を並べるか（計画 29・ユーザ案 2026-11-11）。画面の部品から切り離して、試験で確かめる。
///
/// 並びは3つの群：リンクの外へ出る物（開く・コピー）/ リンクの商品をアプリで扱う物 / 選んだ文の物。
/// 商品のカードの右クリックと同じく「BOOTH へ出る物」を先に、「アプリの中」を後に置く。
/// リンクの上でないときはリンクの群を出さない（押せる物が無い群を並べても、どのリンクの話か分からない）。
/// 選んだ文の群は、選んでいなくても出したまま押せなくして理由を言う（右クリックの決まり。ユーザ判断 2026-10-04）。
/// </summary>
internal sealed class TextMenuPlan
{
    /// <summary>区切り。<see cref="Entries"/> の中の null。</summary>
    public const TextMenuEntry? Separator = null;

    private TextMenuPlan(string? link, string? linkItemId, string selectedText, IReadOnlyList<string> selectedItemIds, IReadOnlyList<TextMenuEntry?> entries)
    {
        Link = link;
        LinkItemId = linkItemId;
        SelectedText = selectedText;
        SelectedItemIds = selectedItemIds;
        Entries = entries;
    }

    public string? Link { get; }

    /// <summary>リンクが BOOTH の商品ページなら、その商品ID（26 と同じ線：<see cref="BoothUrlExtractor.TryExtractItemPageId"/>）。</summary>
    public string? LinkItemId { get; }

    public string SelectedText { get; }

    public IReadOnlyList<string> SelectedItemIds { get; }

    public IReadOnlyList<TextMenuEntry?> Entries { get; }

    /// <param name="link">右クリックした所のリンクの URL。リンクの上でなければ null。</param>
    /// <param name="selectedText">選んだ字そのまま（「コピー」で写す物）。</param>
    /// <param name="selectedFragments">選んだ範囲を、リンクは URL の全体に直して並べた物（<see cref="SelectedFragments"/>）。商品を拾う元。</param>
    /// <param name="host">商品を開く・登録する相手。無ければその項目を出さない（開く先も登録の窓も無い）。</param>
    public static TextMenuPlan Build(string? link, string? selectedText, IReadOnlyList<string> selectedFragments, ITextMenuHost? host)
    {
        selectedText ??= string.Empty;
        var selectedIds = ItemIdsIn(selectedFragments);
        var linkItemId = link is null ? null : BoothUrlExtractor.TryExtractItemPageId(link);
        var entries = new List<TextMenuEntry?>();

        if (link is not null)
        {
            // 外へ出る項目の吹き出しは、ほかの「BOOTHで開く」と同じ言い方（ui-terms「決めた呼び方」）
            entries.Add(new TextMenuEntry(TextMenuAction.OpenLink, BoothLinks.IsBoothPage(link) ? "BOOTHで開く" : "ブラウザで開く", true,
                "このリンクをブラウザで開きます。アプリの外へ出ます。"));
            entries.Add(new TextMenuEntry(TextMenuAction.CopyLink, "リンクをコピー", true));

            if (host is not null)
            {
                entries.Add(Separator);

                // ライブラリにある商品は、登録しても何も変わらない。灰色にするより、そこへ行ける方が次の手になる
                // （左クリックでも開くが、右クリックで「登録」を探した人に、もうあることを名前で言う）
                entries.Add(linkItemId is not null && host.IsInLibrary(linkItemId)
                    ? new TextMenuEntry(TextMenuAction.OpenItem, "商品ページを開く", true)
                    : new TextMenuEntry(TextMenuAction.RegisterLink, "商品として登録…", linkItemId is not null,
                        linkItemId is null ? "BOOTHの商品のリンクだけ登録できます。" : null));
            }

            entries.Add(Separator);
        }

        var hasSelection = selectedText.Trim().Length > 0;
        entries.Add(new TextMenuEntry(TextMenuAction.Copy, "コピー", hasSelection, hasSelection ? null : "文字を選ぶとコピーできます。"));

        if (host is not null)
        {
            entries.Add(selectedIds.Count > 0
                ? new TextMenuEntry(TextMenuAction.RegisterSelection, "選んだ範囲の商品をまとめて登録…", true, $"{selectedIds.Count} 件の商品が見つかりました。")
                : new TextMenuEntry(TextMenuAction.RegisterSelection, "選んだ範囲の商品をまとめて登録…", false, "BOOTHの商品のリンクを含めて選ぶと登録できます。"));
        }

        return new TextMenuPlan(link, linkItemId, selectedText, selectedIds, entries);
    }

    /// <summary>
    /// 選んだ範囲の商品ID。26 の拾い方（<see cref="PastedItemIds.Collect"/>：商品ページの URL だけ・出てきた順・重ねない）をそのまま使う。
    /// 欠片は空白でつなぐ（リンクの URL の後ろに字が続くと、URL の続きとして読まれる）。
    /// </summary>
    internal static IReadOnlyList<string> ItemIdsIn(IReadOnlyList<string> selectedFragments)
        => PastedItemIds.Collect(string.Join(" ", selectedFragments), null);

    /// <summary>
    /// 選んだ範囲を欠片に分ける。リンクは、範囲が掛かっていれば**URL の全体**にする。
    /// 範囲の端が URL の途中で切れたとき、見えている字のまま拾うと、別の番号の商品を登録してしまう
    /// （<c>…/items/12345</c> の頭の「1234」だけを選んだ形。登録は取り消せない）。
    /// リンクでない字は、選んだ所だけ。
    /// </summary>
    internal static IReadOnlyList<string> SelectedFragments(FlowDocument document, TextPointer start, TextPointer end)
    {
        var fragments = new List<string>();
        if (start.CompareTo(end) >= 0)
        {
            return fragments;
        }

        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (inline.ContentEnd.CompareTo(start) <= 0 || inline.ContentStart.CompareTo(end) >= 0)
                {
                    continue;
                }

                switch (inline)
                {
                    case Hyperlink { NavigateUri: { } uri }:
                        fragments.Add(uri.OriginalString);
                        break;

                    case Run run:
                        var from = run.ContentStart.CompareTo(start) < 0 ? start : run.ContentStart;
                        var to = run.ContentEnd.CompareTo(end) > 0 ? end : run.ContentEnd;
                        fragments.Add(new TextRange(from, to).Text);
                        break;

                    case LineBreak:
                        fragments.Add("\n");
                        break;
                }
            }

            fragments.Add("\n");
        }

        return fragments;
    }

    /// <summary>
    /// 位置にあるリンク。文字の上でない・リンクでない所は null。
    /// </summary>
    internal static Hyperlink? LinkAt(TextPointer? position)
    {
        for (DependencyObject? node = position?.Parent; node is not null and not FlowDocument; node = (node as TextElement)?.Parent)
        {
            if (node is Hyperlink link)
            {
                return link;
            }
        }

        return null;
    }
}

/// <summary>
/// 説明文の右クリックのメニューを組む（計画 29）。<see cref="SelectableText"/> がリンクにする本文（説明文・見出しの本文・メモ・編集画面の説明文）に付ける。
///
/// 見た目はほかの右クリックと同じ物を使う：既定の <see cref="MenuItem"/> の見た目（Themes/Controls.xaml。押せない項目にも吹き出しを出す）・
/// <see cref="Separator"/>。組み方と吹き出しの置き方は <see cref="SortDividerBorder.BuildMenu"/> と同じく、コードで項目を作り ToolTip に直に書く。
/// </summary>
internal static class TextMenu
{
    /// <summary>リンクにする本文に、メニューを付ける。中身は開くたびに組み直す（右クリックした所と選んだ範囲で変わる）。</summary>
    public static void Attach(RichTextBox box)
    {
        // RichTextBox は ContextMenu が無いと、自分の既定のメニュー（切り取り・コピー・貼り付け）を出す。
        // 置いておくと、読み取り専用のときはそちらを使う（中身はここで差し替える）
        if (box.ContextMenu is null)
        {
            var menu = new ContextMenu();
            AutomationProperties.SetName(menu, "本文の操作");

            // 空のメニューは開かない作りがあり得るので、開く前から押せない「コピー」だけは入れておく（開くときに組み直す）
            Fill(menu, TextMenuPlan.Build(null, null, [], null), null);
            box.ContextMenu = menu;
        }

        // 済んだ印の付いた出来事でも受ける。RichTextBox の中の処理（文字の編集の部品）が先に受けて済んだ印を付ける枝があると、
        // 組み直しが呼ばれず、使い回すメニューに前回のリンク・前回の範囲の項目が残る（押すと前の商品を登録の窓に出す）
        box.RemoveHandler(FrameworkElement.ContextMenuOpeningEvent, OpeningHandler);
        box.AddHandler(FrameworkElement.ContextMenuOpeningEvent, OpeningHandler, handledEventsToo: true);
    }

    private static readonly ContextMenuEventHandler OpeningHandler = OnOpening;

    private static void OnOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not RichTextBox { ContextMenu: { } menu } box)
        {
            return;
        }

        // キーボード（Shift+F10）から開いたときは、マウスの位置ではなく選んだ所の頭で見る。
        // マウスのときは、押された物（リンクの中の字）から先にたどる。位置から引くと、リンクの端の半文字で外の位置を返すことがある
        var link = e.CursorLeft < 0
            ? TextMenuPlan.LinkAt(box.Selection.Start)
            : LinkFrom(e.OriginalSource as DependencyObject) ?? TextMenuPlan.LinkAt(box.GetPositionFromPoint(Mouse.GetPosition(box), snapToText: false));
        var (plan, host) = Compose(box, link);
        Fill(menu, plan, host);
    }

    /// <summary>
    /// 開く箱と右クリックしたリンクから、並びと相手を決める。相手は <see cref="SelectableText.MenuHostProperty"/>、無ければ左クリックの行き先（商品ページ）が兼ねる。
    /// </summary>
    internal static (TextMenuPlan Plan, ITextMenuHost? Host) Compose(RichTextBox box, Hyperlink? link)
    {
        var fragments = TextMenuPlan.SelectedFragments(box.Document, box.Selection.Start, box.Selection.End);
        var host = SelectableText.GetMenuHost(box) ?? SelectableText.GetNavigator(box) as ITextMenuHost;
        return (TextMenuPlan.Build(link?.NavigateUri?.OriginalString, box.Selection.Text, fragments, host), host);
    }

    /// <summary>押された物からたどったリンク。本文の字（Run）の親をたどる。</summary>
    internal static Hyperlink? LinkFrom(DependencyObject? source)
    {
        for (var node = source; node is TextElement element; node = element.Parent)
        {
            if (element is Hyperlink link)
            {
                return link;
            }
        }

        return null;
    }

    /// <summary>組んだ並びで、メニューの中身を入れ替える。試験は開かずにここで組んだ項目を読む。</summary>
    internal static void Fill(ContextMenu menu, TextMenuPlan plan, ITextMenuHost? host)
    {
        menu.Items.Clear();
        foreach (var entry in plan.Entries)
        {
            menu.Items.Add(entry is null ? new Separator() : Item(entry, plan, host));
        }
    }

    private static MenuItem Item(TextMenuEntry entry, TextMenuPlan plan, ITextMenuHost? host)
    {
        var item = new MenuItem { Header = entry.Header, IsEnabled = entry.IsEnabled };
        AutomationProperties.SetName(item, entry.Header);
        AutomationProperties.SetAutomationId(item, entry.AutomationId);

        // 吹き出しは直に書く（SortDividerBorder と同じ）。MenuTips は既定の見た目の結び（(controls:MenuTips.Disabled)）で引くが、
        // 試験の中ではその結びが道筋を解けず（PathError。XAML で書いた項目も同じ）、出ることを確かめられなかった。
        // 直に書けば押せないときも出る（ShowOnDisabled は既定の見た目が持つ）
        if (entry.Tip is not null)
        {
            item.ToolTip = entry.Tip;
        }

        item.Click += (_, _) => Run(entry.Action, plan, host);
        return item;
    }

    internal static void Run(TextMenuAction action, TextMenuPlan plan, ITextMenuHost? host)
    {
        switch (action)
        {
            case TextMenuAction.OpenLink:
                // 説明文は出品者が書いた物。開くのは Shell の守り（http/https だけ）を通す（左クリックと同じ）
                Services.Shell.OpenUrl(plan.Link);
                return;

            case TextMenuAction.CopyLink when plan.Link is not null:
                Services.ClipboardText.TrySet(plan.Link);
                return;

            case TextMenuAction.OpenItem when plan.LinkItemId is not null:
                host?.OpenItem(plan.LinkItemId);
                return;

            case TextMenuAction.RegisterLink when plan.LinkItemId is not null:
                host?.RegisterItems([plan.LinkItemId]);
                return;

            case TextMenuAction.Copy:
                // Ctrl+C（ApplicationCommands.Copy）は OLE を通り、写せているのに失敗を返すことがある（ClipboardText に理由）
                Services.ClipboardText.TrySet(plan.SelectedText);
                return;

            case TextMenuAction.RegisterSelection when plan.SelectedItemIds.Count > 0:
                host?.RegisterItems(plan.SelectedItemIds);
                return;
        }
    }
}
