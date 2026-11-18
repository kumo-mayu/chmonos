using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 設定の入力欄の右クリック「既定に戻す（既定：…）」（open.md 170・ユーザ判断 2026-11-19）。
/// 欄に <c>controls:SettingDefault.Key="欄の名前"</c> を付けると、右クリックのたびにメニューを組み直す
/// （括弧の中の既定の言い方と押せるかが、今の値と並べる物で変わるため）。
///
/// 数の欄（TextBox）は、メニューを付けると WPF の既定のメニュー（切り取り・コピー・貼り付け）が消えるので、
/// その3つを先に並べ直し、区切りの下に「既定に戻す」を置く。
/// </summary>
public static class SettingDefault
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(SettingDefault), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.ContextMenuOpening -= OnOpening;
        if (e.NewValue is string)
        {
            // 開く出来事は、メニューが付いていない欄でも来ない作りがあるので、空でないメニューを先に付けておく（開くときに組み直す）
            element.ContextMenu ??= new ContextMenu { Items = { new MenuItem { Header = "既定に戻す", IsEnabled = false } } };
            element.ContextMenuOpening += OnOpening;
        }
    }

    private static void OnOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingsViewModel settings } element && GetKey(element) is { } key)
        {
            element.ContextMenu = Build(element, settings, key);
        }
    }

    /// <summary>メニューを組む。試験から直に呼ぶ。</summary>
    internal static ContextMenu Build(FrameworkElement element, SettingsViewModel settings, string key)
    {
        var menu = new ContextMenu();
        AutomationProperties.SetName(menu, "設定の操作");

        if (element is TextBox box)
        {
            menu.Items.Add(TextCommand("切り取り", ApplicationCommands.Cut, box));
            menu.Items.Add(TextCommand("コピー", ApplicationCommands.Copy, box));
            menu.Items.Add(TextCommand("貼り付け", ApplicationCommands.Paste, box));
            menu.Items.Add(new Separator());
        }

        var atDefault = settings.IsAtDefault(key);
        var reset = new MenuItem
        {
            Header = settings.DefaultMenuHeader(key),
            IsEnabled = !atDefault,
        };

        // 初期に戻す項目は赤（ui-input.md・V4）。押せないときは訳を吹き出しで言う（右クリックの決まり：項目は隠さない）
        if (element.TryFindResource("DangerMenuItem") is Style danger)
        {
            reset.Style = danger;
        }

        if (atDefault)
        {
            reset.ToolTip = "今の値が既定です";
            ToolTipService.SetShowOnDisabled(reset, true);
        }
        AutomationProperties.SetAutomationId(reset, $"Settings.Menu.ResetToDefault.{key}");
        reset.Click += (_, _) =>
        {
            // 数の欄は、打ちかけの文字が残っていると、欄を離れたときにそれで上書きされる。戻す前に欄の文字を捨てる
            if (element is TextBox text)
            {
                text.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            }

            settings.ResetToDefault(key);
        };
        menu.Items.Add(reset);
        return menu;
    }

    private static MenuItem TextCommand(string header, ICommand command, TextBox target)
    {
        var item = new MenuItem { Header = header, Command = command, CommandTarget = target };
        AutomationProperties.SetAutomationId(item, $"Settings.Menu.{((RoutedUICommand)command).Name}");
        return item;
    }
}
