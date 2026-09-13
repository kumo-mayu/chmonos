using System.Windows;
using System.Windows.Controls;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 送り先の Unity を決める（#69）。
///
/// **窓を名指しして送る道なので、複数開いていても選べば送れる。**
/// 以前のシェル（関連付け）で渡す道は、どれに入るかを保証できず、複数なら断っていた（§9-1）。
/// 今は商品ページの1件送りも含めて全部こちらを通り、狙った窓にしか入らないので、断らずに選ばせる。
/// </summary>
public static class UnityTargetPicker
{
    /// <param name="preferProjectName">
    /// 改変に紐付けたプロジェクトの名前。それが開いていれば黙ってそちらに決める。
    /// </param>
    public static OpenUnityEditor? Pick(string title, string? preferProjectName = null)
    {
        var editors = UnityEditors.Open();
        if (editors.Count == 0)
        {
            MessageBox.Show(
                "送り先は、開いているUnityになります。\n\nいまUnityが開いていないので送れません。プロジェクトを開いてから、もう一度押してください。",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (preferProjectName is not null
            && editors.FirstOrDefault(editor =>
                string.Equals(editor.ProjectName, preferProjectName, StringComparison.OrdinalIgnoreCase)) is { } linked)
        {
            return linked;
        }

        return editors.Count == 1 ? editors[0] : Choose(title, editors);
    }

    /// <summary>複数開いているとき、どれへ送るかを選ばせる。選べなければ null。</summary>
    private static OpenUnityEditor? Choose(string title, IReadOnlyList<OpenUnityEditor> editors)
    {
        var list = new ListBox { Margin = new Thickness(0, 10, 0, 12), MinHeight = 90 };
        foreach (var editor in editors)
        {
            list.Items.Add(editor.ProjectName ?? $"名前の分からないプロジェクト（プロセス {editor.ProcessId}）");
        }

        list.SelectedIndex = 0;

        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
        };

        var ok = new Button { Content = "このUnityへ送る", IsDefault = true, Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "やめる", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        ok.Click += (_, _) => window.DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = $"Unityが {editors.Count} つ開いています。どれへ送りますか？\n選んだUnityの窓にだけ送るので、ほかのプロジェクトには入りません。",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(list);
        body.Children.Add(buttons);
        window.Content = body;

        return window.ShowDialog() == true && list.SelectedIndex >= 0 ? editors[list.SelectedIndex] : null;
    }
}
