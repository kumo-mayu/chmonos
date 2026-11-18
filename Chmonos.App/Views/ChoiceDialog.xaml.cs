using System.Windows;

namespace Chmonos.App.Views;

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

    /// <summary>
    /// 2択で広い幅にする、2つのボタンの名前の字数の和。540 の窓にキャンセル（90）と余白を引くと、2つのボタンに使えるのは約 400。
    /// 12px の字で約 30 字を超えると収まらない（「中断する」＋「中断して、この取り込みで…件を削除する」の 34 字ではみ出した）
    /// </summary>
    private const int LongChoiceChars = 30;

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
        else if (first.Length + second.Length > LongChoiceChars)
        {
            // 2択でも、ボタンの名前が長いと540では左のボタンが窓の外へはみ出す（取り込みの中断の「…n 件を削除する」・open 119）
            Width = WideWidth;
        }

        Owner = Application.Current?.MainWindow;
        Services.DialogFit.Prepare(this);
    }

    public ChoiceDialogResult Result { get; private set; } = ChoiceDialogResult.Cancel;

    /// <summary>
    /// 2つの選択肢とキャンセルで尋ねる。**既定のボタンは置かない**——
    /// どちらの意図か分からないから聞いているので、Enter で片方に倒さない。
    /// </summary>
    public static ChoiceDialogResult Ask(string title, string question, string detail, string first, string second)
        => Show(new ChoiceDialog(title, question, detail, first, second, third: null));

    /// <summary>
    /// 2つの選択肢だけで尋ねる（キャンセルを並べない）。2つ目が「やめる」側の答えのとき（閉じる／閉じない）に使う。
    /// キャンセルを並べると「閉じない」と同じ意味のボタンが2つになる。Esc と × は2つ目と同じに扱う。
    /// </summary>
    public static ChoiceDialogResult AskTwo(string title, string question, string detail, string first, string second)
    {
        var dialog = new ChoiceDialog(title, question, detail, first, second, third: null);
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.SecondButton.IsCancel = true;
        var answer = Show(dialog);
        return answer == ChoiceDialogResult.Cancel ? ChoiceDialogResult.Second : answer;
    }

    /// <summary>
    /// 3つの選択肢だけで尋ねる（キャンセルを並べない）。3つ目が「やめる」側の答えのとき（送る・これを飛ばす・残りすべてを中止）に使う。
    /// Esc と × は3つ目と同じに扱う（<see cref="AskTwo"/> と同じ考え）。
    /// </summary>
    public static ChoiceDialogResult AskThree(string title, string question, string detail, string first, string second, string third)
    {
        var dialog = new ChoiceDialog(title, question, detail, first, second, third);
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.ThirdButton.IsCancel = true;
        var answer = Show(dialog);
        return answer == ChoiceDialogResult.Cancel ? ChoiceDialogResult.Third : answer;
    }

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
