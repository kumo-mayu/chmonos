using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Controls;

/// <summary>
/// 左の一覧を畳む・広げるつまみと、畳んだ姿の帯（open.md 51）。覚え方と幅は <see cref="PaneColumn"/>（<c>remembersCollapse</c>）が持つ。
/// </summary>
public partial class PaneCollapseToggle : UserControl
{
    public static readonly DependencyProperty PaneProperty = DependencyProperty.Register(
        nameof(Pane), typeof(PaneColumn), typeof(PaneCollapseToggle));

    /// <summary>畳んだ帯に縦に書く見出し（「改変」「フォルダ」など。左の一覧が何かが、畳んでも分かるように）。</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PaneCollapseToggle), new PropertyMetadata(string.Empty));

    /// <summary>つまみの自動操作の名前（AutomationId）。画面ごとに分ける（確かめの道具が画面を取り違えないように）。</summary>
    public static readonly DependencyProperty AutomationKeyProperty = DependencyProperty.Register(
        nameof(AutomationKey), typeof(string), typeof(PaneCollapseToggle), new PropertyMetadata(string.Empty));

    public PaneCollapseToggle()
    {
        InitializeComponent();
    }

    public PaneColumn? Pane
    {
        get => (PaneColumn?)GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string AutomationKey
    {
        get => (string)GetValue(AutomationKeyProperty);
        set => SetValue(AutomationKeyProperty, value);
    }
}
