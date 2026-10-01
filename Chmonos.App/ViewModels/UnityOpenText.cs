using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// プロジェクトを開いた結果の文。改変の画面と改変の右側の「Unityを開く」で同じことを言う。
/// **結果を必ず言う**（開いていたら手前に出るだけで、何も起きなかったように見える）。
/// </summary>
internal static class UnityOpenText
{
    /// <summary>
    /// 結果を文にする。Hub が入っているかが要るのは、開けなかったとき（<see cref="UnityOpenResult.Failed"/>）だけ。
    /// </summary>
    /// <remarks>
    /// 前はここでレジストリを直に読んでいて、試験で「Hub が無い」側を確かめられず、画面のスレッドでレジストリを読んでいた。
    /// Hub の有無は呼ぶ側が渡す（<see cref="ForAsync"/>）。
    /// </remarks>
    public static string For(UnityOpenResult result, string name, bool hasHub) => result switch
    {
        UnityOpenResult.BroughtToFront => $"「{name}」は既に開いています。そのUnityを手前に出しました。",
        UnityOpenResult.AlreadyOpenNotFront =>
            $"「{name}」は既に開いています。手前に出せなかったので、タスクバーのUnityを押して切り替えてください。",
        UnityOpenResult.AlreadyOpenUnknownWindow =>
            $"「{name}」は既に開いています（読み込み中のようです）。読み込みが終わったら、タスクバーのUnityから切り替えてください。",
        UnityOpenResult.Launched => $"「{name}」をUnityで開いています。少し時間がかかります。",
        UnityOpenResult.HandedToHub =>
            "このプロジェクトのUnityが手元に無いので、Unity Hubに渡しました。Hubが入れるか聞いてくれます。",
        UnityOpenResult.HandedToHubWithoutVersion =>
            $"「{name}」のUnityのバージョンが読めなかったので、Unity Hubを開きました。Hubのプロジェクトの一覧から開いてください。",

        // 「入っていない」と「どうすれば開けるか」の2つだけを言う（ui-writing.md：多くても2つ）。
        // Hub が無いことは「Hubを入れると」で伝わるので、「Hubも見つかりませんでした」の1文は削った
        UnityOpenResult.NoEditorNoHub =>
            $"「{name}」のUnityが入っていません。Unity Hubを入れると、このバージョンを入れて開けます。",
        UnityOpenResult.Missing => $"「{name}」のフォルダが見つかりません。",

        // Hub が無い人に Hub を勧めない（ユーザ判断 2026-09-13）
        _ => hasHub
            ? "Unityを開けませんでした。Unity Hubから開いてみてください。"
            : "Unityを開けませんでした。Unityを起動して、このプロジェクトのフォルダを開いてみてください。",
    };

    /// <summary>
    /// 結果を文にする。開けなかったときだけ、Hub が入っているかを**その場で**裏で調べる。
    /// </summary>
    /// <remarks>
    /// 画面を開いたときに調べた結果（改変の画面の <c>Tools</c>）を使わないのは、改変の右側を単独で開いたときは
    /// 窓が手前に戻っても調べ直さず、Hub を入れる・消すをした後に古い答えで言ってしまうため（前のその場で読む動きを変えない）。
    /// 調べる口は試験で差し替えられる <see cref="AppServiceContainer.DetectUnityTools"/>。
    /// </remarks>
    public static async Task<string> ForAsync(AppServiceContainer services, UnityOpenResult result, string name)
    {
        var hasHub = result == UnityOpenResult.Failed && (await Task.Run(() => services.DetectUnityTools())).HasHub;
        return For(result, name, hasHub);
    }
}
