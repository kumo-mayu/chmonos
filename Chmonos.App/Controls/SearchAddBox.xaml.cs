using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Chmonos.App.Controls;

/// <summary>
/// 検索と追加を1本にした欄（タグの管理・属性の管理の左。ユーザ指示 2026-10-02）。
/// 打つと一覧が絞られ、<see cref="AddRowText"/> が空でないときだけ、欄の下に追加の行を出す。
/// 押すか、欄で Enter を押すと <see cref="AddCommand"/>／<see cref="SubmitCommand"/> が動く
/// </summary>
public partial class SearchAddBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SearchAddBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchAddBox),
            new PropertyMetadata(string.Empty, (d, e) => ((SearchAddBox)d).Watermark.Text = e.NewValue as string ?? string.Empty));

    /// <summary>追加の行の文。空なら行を出さない（足せないとき）。</summary>
    public static readonly DependencyProperty AddRowTextProperty =
        DependencyProperty.Register(nameof(AddRowText), typeof(string), typeof(SearchAddBox),
            new PropertyMetadata(string.Empty, (d, e) =>
            {
                var box = (SearchAddBox)d;
                var text = e.NewValue as string ?? string.Empty;
                box.AddRow.Content = text;
                box.AddRow.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }));

    /// <summary>追加の行を押したとき。</summary>
    public static readonly DependencyProperty AddCommandProperty =
        DependencyProperty.Register(nameof(AddCommand), typeof(ICommand), typeof(SearchAddBox));

    /// <summary>欄で Enter を押したとき（足す・選ぶのどちらかは画面の側が決める）。</summary>
    public static readonly DependencyProperty SubmitCommandProperty =
        DependencyProperty.Register(nameof(SubmitCommand), typeof(ICommand), typeof(SearchAddBox));

    public SearchAddBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string AddRowText
    {
        get => (string)GetValue(AddRowTextProperty);
        set => SetValue(AddRowTextProperty, value);
    }

    public ICommand? AddCommand
    {
        get => (ICommand?)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand? SubmitCommand
    {
        get => (ICommand?)GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    /// <summary>
    /// 読み上げ・自動操作が見る欄の名前。部品に付けた名前は中の欄まで届かないので写す（SuggestBox と同じ）
    /// </summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (Input is not null && e.Property == System.Windows.Automation.AutomationProperties.NameProperty)
        {
            System.Windows.Automation.AutomationProperties.SetName(Input, e.NewValue as string ?? string.Empty);
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
        => Watermark.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        // 打ってすぐ Enter を押すと、結び付けの待ち（Delay=200）で画面の側の文字がまだ前の物のまま。先に流し込む
        Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (SubmitCommand?.CanExecute(null) == true)
        {
            SubmitCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (AddCommand?.CanExecute(null) == true)
        {
            AddCommand.Execute(null);
        }
    }
}
