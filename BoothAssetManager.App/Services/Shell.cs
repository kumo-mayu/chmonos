using System.Diagnostics;
using System.IO;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 外部のアプリへ渡す操作をここに集める。
///
/// 同じ try/catch が7画面に散っていたのでまとめた。
/// 開けなくてもアプリは動き続けるべきなので、失敗は握り潰す
/// （ブラウザが無い・関連付けが壊れている、はこちらで直せない）。
/// </summary>
public static class Shell
{
    /// <summary>URLを既定のブラウザで開く。</summary>
    public static void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            TryStart(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }

    /// <summary>
    /// エクスプローラで開く。ファイルなら、そのファイルを選択した状態にする。
    /// 消えている場合は親フォルダを開く（何も起きないよりは辿れる）。
    /// </summary>
    public static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (Directory.Exists(directory))
        {
            TryStart(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
    }

    /// <summary>
    /// zipの中の <c>.unitypackage</c> をUnityへ送る。
    ///
    /// **展開しない。**渡すのは <c>&lt;zip&gt;\&lt;中のパス&gt;</c> という仮想パスで、
    /// zipを「フォルダ」として解決するのはWindowsの仕事。
    /// 関連付け（<c>Unity.exe -openfile</c>）が起動中のエディタへ転送し、
    /// 取り込みダイアログが出る。
    ///
    /// **開いていないと何も起きない**（Unity Hubの窓が出るだけ）。
    /// 呼ぶ側で <see cref="UnityEditors.Open"/> を見て言い分けること。
    /// </summary>
    public static void SendToUnity(string? virtualPath)
    {
        if (!string.IsNullOrWhiteSpace(virtualPath))
        {
            TryStart(new ProcessStartInfo { FileName = virtualPath, UseShellExecute = true });
        }
    }

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくてもアプリは動き続ける
        }
    }
}
