using BoothAssetManager.App.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// プロジェクトを開いた結果の文。改変の画面と改変の右側の「Unityを開く」で同じことを言う。
/// **結果を必ず言う**（開いていたら手前に出るだけで、何も起きなかったように見える）。
/// </summary>
internal static class UnityOpenText
{
    public static string For(UnityOpenResult result, string name) => result switch
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
        UnityOpenResult.NoEditorNoHub =>
            $"「{name}」のUnityが手元に入っていません。Unity Hubも見つかりませんでした。Unity Hubを入れると、このバージョンのUnityを入れて開けます。",
        UnityOpenResult.Missing => $"「{name}」のフォルダが見つかりません。",

        // Hub が無い人に Hub を勧めない（ユーザ判断 2026-09-13）。失敗したときだけ調べる
        _ => UnityLaunch.HasHub()
            ? "Unityを開けませんでした。Unity Hubから開いてみてください。"
            : "Unityを開けませんでした。Unityを起動して、このプロジェクトのフォルダを開いてみてください。",
    };
}
