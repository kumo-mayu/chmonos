using System.Windows;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

public partial class PickMemberFilesDialog : Window
{
    /// <param name="repick">記録した使ったファイルを選び直す窓として出すか（ボタンと前提の文を替え、今の記録が一覧に無いときの1行を出す）。</param>
    public PickMemberFilesDialog(MemberFilePickViewModel model, string title, string headingText, bool repick = false)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        HeadingText.Text = headingText;
        Picker.DataContext = model;
        if (repick)
        {
            CommitButton.Content = "変更";
            // 取り返しがつくかを1行で言う（ui-dialogs.md の D4）。手元に無い記録は、替えると一覧から選び直せない
            ContextText.Text = model.HasNotice
                ? "「選ばない」にすると記録を削除します。今の記録は、替えると戻せません。"
                : "「選ばない」にすると記録を削除します。同じ手順で選び直せます。";
            NoticeText.Text = model.Notice;
            NoticeText.Visibility = model.HasNotice ? Visibility.Visible : Visibility.Collapsed;
        }

        // 主の窓が無い所（窓を出さずに描く台）では、最初に作った窓＝自分が主の窓になるので、持ち主にしない
        if (Application.Current?.MainWindow is { } main && main != this)
        {
            Owner = main;
        }
        Services.DialogFit.Prepare(this);
    }

    private void OnCommit(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
