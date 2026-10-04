using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 並べ替えの区切りの札（カードの升に入る方・リストの行の中身）の枠。見た目は Border のまま、読み上げ・自動操作には1つの部品として出す。
///
/// 素の Border は窓口を持たないので、カードの一覧の窓口（<see cref="CardRowsListBox"/>）が中を下りて、
/// 札の中の文字（項目の名前・親・名前・件数）を4つの別々の文字として並べてしまう。名前（「カテゴリ：3Dモデル / 衣装、12 件」）を持つ1つにまとめ、中は出さない。
///
/// **ショップの札だけ押せる**（ユーザ判断 2026-10-02・メモ2-⑤）：左クリックでアプリのショップの画面、中クリックで BOOTH のショップのページ、
/// 右クリックでその2つのメニュー。読み上げ・自動操作の「押す」は左クリックと同じ。ほかの札は今までどおり押しても何も起きない文字。
/// **どの札もキーボードでは止まらない**（2026-10-01 の判断。止まるとカードからカードへの矢印が札で止まる。
/// キーボードからは商品のカードの右クリックの「ショップを開く」とショップの一覧から同じ所へ届く）
/// </summary>
public sealed class SortDividerBorder : Border
{
    private MouseButton? _pressed;

    public SortDividerBorder()
    {
        Focusable = false;
        DataContextChanged += (_, _) => Refresh();
    }

    private SortDivider? Divider => DataContext as SortDivider;

    /// <summary>押せる札かで、マウスの形と右クリックのメニューを付け替える（札は一覧の中で使い回される）。</summary>
    private void Refresh()
    {
        if (Divider is not { IsPressable: true } divider)
        {
            ClearValue(CursorProperty);
            ClearValue(ContextMenuProperty);
            ClearValue(ToolTipProperty);
            return;
        }

        Cursor = Cursors.Hand;
        ToolTip = divider.PressHint;
        ContextMenu = BuildMenu(divider);
    }

    /// <summary>
    /// 右クリックのメニュー。文言は商品のカードの右クリックに揃える（「ショップを開く」はアプリのショップの画面、「BOOTHで開く」は BOOTH のページ）。
    /// BOOTH にページの無い手元だけのショップでも、BOOTH の行は出したまま押せなくして理由を言う（項目をいつも同じに並べる。ユーザ判断 2026-10-04）。
    /// </summary>
    internal static ContextMenu BuildMenu(SortDivider divider)
    {
        var menu = new ContextMenu();
        System.Windows.Automation.AutomationProperties.SetName(menu, $"{divider.Label}の操作");
        menu.Items.Add(Item("ショップを開く", "SortDivider.Menu.OpenShop", divider.OpenShopCommand));
        var booth = Item("BOOTHで開く", "SortDivider.Menu.OpenInBooth", divider.OpenInBoothCommand);
        if (divider.OpenInBoothCommand is null)
        {
            booth.IsEnabled = false;
            booth.ToolTip = "BOOTHに無いショップなので開けません";
        }

        menu.Items.Add(booth);

        return menu;

        static MenuItem Item(string header, string id, ICommand? command)
        {
            var item = new MenuItem { Header = header, Command = command };
            System.Windows.Automation.AutomationProperties.SetName(item, header);
            System.Windows.Automation.AutomationProperties.SetAutomationId(item, id);
            return item;
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (Divider is { IsPressable: true } && e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            // 押した所で離したときだけ動く（ボタンと同じ。押したまま外へ出たらやめられる）
            _pressed = e.ChangedButton;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressed != e.ChangedButton)
        {
            return;
        }

        _pressed = null;
        ReleaseMouseCapture();
        var inside = e.GetPosition(this) is var at && at.X >= 0 && at.Y >= 0 && at.X <= ActualWidth && at.Y <= ActualHeight;
        if (inside && Divider is { } divider)
        {
            var command = e.ChangedButton == MouseButton.Middle ? divider.OpenInBoothCommand : divider.OpenShopCommand;
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pressed = null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(SortDividerBorder owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        private SortDivider? Divider => owner.DataContext as SortDivider;

        protected override AutomationControlType GetAutomationControlTypeCore()
            => Divider is { IsPressable: true } ? AutomationControlType.Button : AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(SortDividerBorder);

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsKeyboardFocusableCore() => false;

        public override object? GetPattern(PatternInterface patternInterface)
            => patternInterface == PatternInterface.Invoke && Divider is { IsPressable: true } ? this : base.GetPattern(patternInterface);

        public void Invoke()
        {
            if (Divider?.OpenShopCommand is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }
}
