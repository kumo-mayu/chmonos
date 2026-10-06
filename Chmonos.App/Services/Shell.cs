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
    /// <summary>
    /// URLを既定のブラウザで開く。**http と https の URL だけを開く**（2026-10-06 外部の点検・L106）。
    ///
    /// 開き方は OS の関連付けに任せる（UseShellExecute）ので、渡した文字列が実行ファイルの場所・<c>file:</c>・
    /// 独自の形（<c>ms-settings:</c> など）なら、それがそのまま起動される。URL は手で直せる JSON や説明文からも来るので、
    /// ここ1か所で形を見て、ほかは断る。渡すのは解釈し直した形（<see cref="Uri.AbsoluteUri"/>）で、元の文字列ではない。
    /// </summary>
    /// <returns>開くよう渡したか（形が合わなければ false）。</returns>
    public static bool OpenUrl(string? url)
    {
        if (ToWebUrl(url) is not { } web)
        {
            return false;
        }

        TryStart(new ProcessStartInfo { FileName = web, UseShellExecute = true });
        return true;
    }

    /// <summary>ブラウザへ渡してよい URL なら、解釈し直した形。http/https で、ホストのある絶対 URL だけ。</summary>
    internal static string? ToWebUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.IsUnc
            || uri.Host.Length == 0)
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>
    /// 外のアプリを起こす所の差し替え（試験だけが使う）。試験が本物のブラウザを開かずに、何が渡されたかを見るため。
    /// 流れごとに持つので、並んで走るほかの試験の起動は拾わない
    /// </summary>
    internal static readonly AsyncLocal<Action<ProcessStartInfo>?> StartOverride = new();

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

    /// <summary>
    /// ファイルを含んでいるフォルダを開き、そのファイルを選んだ状態にする。zip でも中には入らない
    /// （<see cref="TryRevealAsync"/> は zip の中を見せる。書き出したバックアップの zip は「どこにできたか」を見せたいので、こちら）。
    /// ファイルが消えていれば親フォルダを開く。どちらも無ければ false。
    /// </summary>
    public static Task<bool> TrySelectAsync(string? path)
        => string.IsNullOrWhiteSpace(path) ? Task.FromResult(false) : Task.Run(() => SelectCore(path));

    private static bool SelectCore(string path)
    {
        if (File.Exists(path))
        {
            OpenInExplorer($"/select,\"{path}\"");
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
        if (StartOverride.Value is { } replaced)
        {
            replaced(startInfo);
            return;
        }

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
