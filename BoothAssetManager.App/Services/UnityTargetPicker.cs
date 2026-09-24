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
            Notice.Show(
                "送り先は、開いているUnityになります。\n\nいまUnityが開いていないので送れません。プロジェクトを開いてから、もう一度押してください。",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        // 同じ名前のプロジェクトが複数開いていて見分けられないときは、黙って決めずに選ばせる（別の方へ送りかねない）
        if (preferProjectName is not null
            && editors.FirstOrDefault(editor => !editor.IsAmbiguous
                && string.Equals(editor.ProjectName, preferProjectName, StringComparison.OrdinalIgnoreCase)) is { } linked)
        {
            return linked;
        }

        return editors.Count == 1 ? editors[0] : Choose(title, editors);
    }

    /// <summary>複数開いているとき、どれへ送るかを選ばせる。選べなければ null。</summary>
    private static OpenUnityEditor? Choose(string title, IReadOnlyList<OpenUnityEditor> editors)
    {
        // 一覧から選ばせる窓はファイル・unitypackage を選ぶのと共用（ListChoice）
        var picked = ListChoice.Ask(
            title,
            $"Unityが {editors.Count} つ開いています。どれを対象にしますか？\n選んだUnityの窓にだけ働くので、ほかのプロジェクトには入りません。",
            [.. editors.Select(editor => new ListChoiceItem(
                editor.ProjectName is { } name
                    ? editor.IsAmbiguous ? $"{name}（プロセス {editor.ProcessId}）" : name
                    : $"名前の分からないプロジェクト（プロセス {editor.ProcessId}）",
                editor.IsAmbiguous ? "同じ名前のプロジェクトが複数開いていて、場所を見分けられません" : editor.ProjectPath))],
            "このUnityにする");

        return picked is { } index ? editors[index] : null;
    }
}
