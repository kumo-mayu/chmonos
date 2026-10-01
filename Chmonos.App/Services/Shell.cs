using System.Diagnostics;
using System.IO;

namespace Chmonos.App.Services;

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
    /// エクスプローラで開く（ユーザ指示 2026-09-14）。
    /// <list type="bullet">
    /// <item>フォルダは**そのフォルダ自体**を開く（中が見える状態）。A/B/C/D で B を開いたら C が見える。
    /// 前は親を開いてフォルダを選んでいたので、開いたつもりの中身が見えなかった</item>
    /// <item>zip は**中が見える状態**で開く。アーカイブでも、利用者の感覚ではフォルダなので</item>
    /// <item>それ以外のファイルは、含んでいるフォルダを開いてそのファイルを選ぶ</item>
    /// <item>消えている場合は親フォルダを開く（何も起きないよりは辿れる）</item>
    /// </list>
    /// 在るかは画面のスレッドの外で見る（技術的負債 4-2：外付けやネットワークだと確かめるだけで数秒かかる）。
    /// </summary>
    public static void Reveal(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Task.Run(() => RevealCore(path)).Forget();
        }
    }

    /// <summary>
    /// <see cref="Reveal"/> と同じ開き方で、開く先があったかを返す。
    /// 外付けを外すと、記録の場所も親フォルダも無く黙って何も起きない。押した人に「見つかりません」を言うための版
    /// </summary>
    public static Task<bool> TryRevealAsync(string? path)
        => string.IsNullOrWhiteSpace(path) ? Task.FromResult(false) : Task.Run(() => RevealCore(path));

    private static bool RevealCore(string path)
    {
        if (Directory.Exists(path))
        {
            OpenInExplorer($"\"{path}\"");
            return true;
        }

        if (File.Exists(path))
        {
            // zip は explorer に場所として渡すと、関連付け（7-Zip など）に関係なく中を開く
            OpenInExplorer(path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? $"\"{path}\"" : $"/select,\"{path}\"");
            return true;
        }

        var directory = Path.GetDirectoryName(path);
        if (Directory.Exists(directory))
        {
            OpenInExplorer($"\"{directory}\"");
            return true;
        }

        return false;
    }

    private static void OpenInExplorer(string arguments)
        => TryStart(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });

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
