using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Controls;

/// <summary>除外の一覧の共通部品（<see cref="ViewModels.ExclusionListViewModel"/> を DataContext に渡す）。</summary>
public partial class ExclusionList : UserControl
{
    /// <summary>一覧の高さの上限。設定の欄では決めた高さ（外の流す枠の中で仮想化を効かせるため）、窓では窓に合わせて伸ばす。</summary>
    public static readonly DependencyProperty ListMaxHeightProperty = DependencyProperty.Register(
        nameof(ListMaxHeight), typeof(double), typeof(ExclusionList), new PropertyMetadata(double.PositiveInfinity));

    /// <summary>読み上げの一覧の名前（「管理対象から除外したファイル」など）。</summary>
    public static readonly DependencyProperty ListNameProperty = DependencyProperty.Register(
        nameof(ListName), typeof(string), typeof(ExclusionList), new PropertyMetadata(string.Empty));

    public ExclusionList()
    {
        InitializeComponent();
    }

    public double ListMaxHeight
    {
        get => (double)GetValue(ListMaxHeightProperty);
        set => SetValue(ListMaxHeightProperty, value);
    }

    public string ListName
    {
        get => (string)GetValue(ListNameProperty);
        set => SetValue(ListNameProperty, value);
    }
}
