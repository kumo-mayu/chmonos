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
