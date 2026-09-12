using System.Windows;

namespace BoothAssetManager.App.Views;

/// <summary>どれを選んだか。閉じるボタンや Esc はキャンセル。</summary>
public enum ChoiceDialogResult
{
    Cancel,
    First,
    Second,
    Third,
}

public partial class ChoiceDialog : Window
{
    /// <summary>ボタンが4つ（キャンセル＋3択）並ぶときの幅。540では長い名前のボタンが収まらない。</summary>
    private const double WideWidth = 660;

    private ChoiceDialog(string title, string question, string detail, string first, string second, string? third)
    {
        InitializeComponent();
        Title = title;
        QuestionText.Text = question;
        DetailText.Text = detail;
        DetailText.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FirstButton.Content = first;
        SecondButton.Content = second;
        if (third is not null)
        {
            ThirdButton.Content = third;
            ThirdButton.Visibility = Visibility.Visible;
            Width = WideWidth;
        }

        Owner = Application.Current?.MainWindow;
    }

    public ChoiceDialogResult Result { get; private set; } = ChoiceDialogResult.Cancel;

    /// <summary>
    /// 2つの選択肢とキャンセルで尋ねる。**既定のボタンは置かない**——
    /// どちらの意図か分からないから聞いているので、Enter で片方に倒さない。
    /// </summary>
    public static ChoiceDialogResult Ask(string title, string question, string detail, string first, string second)
        => Show(new ChoiceDialog(title, question, detail, first, second, third: null));

    /// <summary>3つの選択肢とキャンセルで尋ねる。既定のボタンを置かないのは2択と同じ。</summary>
    public static ChoiceDialogResult Ask(
        string title, string question, string detail, string first, string second, string third)
        => Show(new ChoiceDialog(title, question, detail, first, second, third));

    private static ChoiceDialogResult Show(ChoiceDialog dialog)
    {
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void OnFirst(object sender, RoutedEventArgs e)
    {
        Result = ChoiceDialogResult.First;
        DialogResult = true;
    }

    private void OnSecond(object sender, RoutedEventArgs e)
    {
        Result = ChoiceDialogResult.Second;
        DialogResult = true;
    }

    private void OnThird(object sender, RoutedEventArgs e)
    {
        Result = ChoiceDialogResult.Third;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
