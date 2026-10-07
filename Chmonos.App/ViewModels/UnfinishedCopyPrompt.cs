using Chmonos.Core.Storage;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 引越し・戻すの途中で止まった写しかけ（<see cref="UnfinishedCopy"/>）を選んだときに聞く・片付ける（実機の確かめ 2026-10-07）。
/// 設定の「場所を変える」「バックアップから戻す」と初回の画面で同じ問いを出す。
///
/// 片付けは <c>UiCommand</c> を通さない：書くのは保存先の外（選んだ写しかけの中）だけで、商品や設定の錠が守る物に触れない。
/// 初回の画面はサービス一式を組む前に出るので、命令の口がまだ無い
/// </summary>
internal static class UnfinishedCopyPrompt
{
    public const string Title = "コピーが途中で止まった場所です";
    public const string CleanAnswer = "途中のコピーを削除する";
    public const string OpenAnswer = "エクスプローラで開く";

    /// <summary>
    /// 写しかけを片付けてよいかを聞き、よければ片付ける。片付け終えて、続けて選んだ場所を使えるなら true。
    /// やめた・フォルダを開いた・片付けきれなかったなら false（呼び手は何もせずに返る）。
    /// </summary>
    public static async Task<bool> ResolveAsync(string folder)
    {
        // 数えるだけでも写しかけの全ファイルを舐めるので、画面のスレッドで回さない
        var plan = await Task.Run(() => UnfinishedCopy.Plan(folder));
        if (plan.Marker is null)
        {
            Services.Notice.Show(
                $"選んだ場所は、引越しかバックアップから戻す途中で止まったコピーです。\n\n{folder}\n\n"
                + "中身を確かめてから、フォルダごと削除してください。",
                "この場所は使えません",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var answer = ChoiceQuestion.Ask(Request(folder, plan));
        if (answer == Views.ChoiceDialogResult.Second)
        {
            OpenFolder(folder);
            return false;
        }

        if (answer != Views.ChoiceDialogResult.First)
        {
            return false;
        }

        var cleanup = await Task.Run(() => UnfinishedCopy.Clean(folder));
        if (cleanup.Left > 0)
        {
            Services.Notice.Show(
                $"途中のコピーの一部を削除できませんでした。\n\n{folder}\n\n"
                + "ほかのアプリで開いているファイルを閉じてから、もう一度選んでください。",
                "削除できませんでした",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    internal static ChoiceRequest Request(string folder, UnfinishedCopyPlan plan)
    {
        var marker = plan.Marker!;
        var parked = plan.ParkedAt is null ? string.Empty : "置き換える前にあったデータは、元の場所に戻します。\n";

        return new ChoiceRequest(
            Title,
            $"選んだ場所は、{What(marker.Kind)}の途中で止まったコピーです。",
            $"{folder}\n{marker.StartedAt.LocalDateTime:yyyy/MM/dd HH:mm} に始めたコピーです。ライブラリとしては使えません。\n\n"
            + $"「{CleanAnswer}」\n"
            + $"このコピーで作ったファイル {plan.Files.Count:N0} 個・{Core.Models.DisplayText.Size(plan.Bytes)} を削除します。"
            + "元のデータと、コピーの前からあったファイルは残ります。\n"
            + parked
            + $"\n「{OpenAnswer}」\n中身を表示します。何も変えません。",
            CleanAnswer,
            OpenAnswer);
    }

    /// <summary>
    /// 起動しようとした保存先が写しかけだったときの文。開かずに終える（ふつうは起きない。手で location.json を直したときなど）。
    /// 開くと商品の大半が欠けたライブラリとして動き、写し元と見比べられなくなる
    /// </summary>
    internal static string StartupText(string root, UnfinishedCopyMarker? marker)
    {
        var what = marker is null ? "引越しかバックアップから戻す" : What(marker.Kind);
        var origin = marker?.Kind switch
        {
            UnfinishedCopyKind.Move => $"元のデータは「{marker.From}」に残っています。\n\n",
            UnfinishedCopyKind.Restore => $"戻す元のバックアップは「{marker.From}」です。\n\n",
            _ => string.Empty,
        };

        return $"保存先が、{what}の途中で止まったコピーです。ライブラリとしては開けません。\n\n{root}\n\n"
            + origin
            + "location.jsonの保存先を元の場所に直してから、開き直してください。";
    }

    private static string What(UnfinishedCopyKind kind)
        => kind == UnfinishedCopyKind.Restore ? "前にバックアップから戻す処理" : "前の引越し";

    private static void OpenFolder(string folder)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Error("写しかけのフォルダを開く", exception);
        }
    }
}
