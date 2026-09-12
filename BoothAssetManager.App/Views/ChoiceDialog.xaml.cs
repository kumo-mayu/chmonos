using System.Windows;

namespace BoothAssetManager.App.Views;

/// <summary>どちらを選んだか。閉じるボタンや Esc はキャンセル。</summary>
public enum ChoiceDialogResult
{
    Cancel,
    First,
    Second,
}

public partial class ChoiceDialog : Window
{
    private ChoiceDialog(string title, string question, string detail, string first, string second)
    {
        InitializeComponent();
        Title = title;
        QuestionText.Text = question;
        DetailText.Text = detail;
        DetailText.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FirstButton.Content = first;
        SecondButton.Content = second;
        Owner = Application.Current?.MainWindow;
    }

    public ChoiceDialogResult Result { get; private set; } = ChoiceDialogResult.Cancel;

    /// <summary>
    /// 2つの選択肢とキャンセルで尋ねる。**既定のボタンは置かない**——
    /// どちらの意図か分からないから聞いているので、Enter で片方に倒さない。
    /// </summary>
    public static ChoiceDialogResult Ask(string title, string question, string detail, string first, string second)
    {
        var dialog = new ChoiceDialog(title, question, detail, first, second);
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

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
