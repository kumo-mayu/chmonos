using System.IO;
using System.Net;
using System.Net.Http;
using Chmonos.Core.Storage;

namespace ViewShot;

/// <summary>
/// 台のプロセスを、本番の保存先・BOOTH・ほかのアプリの一時展開から切り離す。**アプリの型に触れる前に1回だけ呼ぶ。**
///
/// 台はアプリのサービス一式（<c>AppServiceContainer</c>）と主画面の ViewModel をそのまま組む。
/// そのままだと、(1) 保存先は本番（<c>location.json</c> が指す所）になり、(2) 起動時の裏の作業が BOOTH へ問い合わせ、
/// (3) 前回の消し残しとして、開いているアプリが一時展開したフォルダを消す。どれも場面を描くだけの道具がしてはいけない。
/// </summary>
internal static class Isolation
{
    /// <summary>この回の作業用フォルダ（保存先と一時フォルダの親）。終わりに消す。</summary>
    public static string WorkRoot { get; private set; } = string.Empty;

    /// <summary>
    /// 場面のファイル（作り物の zip など）を置く所。保存先の外。
    /// **場面ごとに決まった場所にする**（回ごとに変えない）。ファイルの場所は画面に出るので、回ごとに違うと、
    /// 前後の画像を比べたときに見た目を変えていない直しでも差が出る。中身は毎回同じ物を書くので、並んで走っても困らない
    /// </summary>
    public static string FilesRoot { get; private set; } = string.Empty;

    /// <summary>作り物の絵の控え（<see cref="Fake.Image"/>）。中身は名前と大きさだけで決まるので、場面と回をまたいで使い回す</summary>
    public static string ImageCacheRoot { get; private set; } = string.Empty;

    public static void Enter(string sceneName)
    {
        // 一時フォルダを差し替える前に、本来の場所で作業用フォルダを決める
        var parent = Path.Combine(Path.GetTempPath(), "chmonos-viewshot");
        SweepOld(Path.Combine(parent, "run"));

        FilesRoot = Path.Combine(parent, "files", sceneName);
        ImageCacheRoot = Path.Combine(parent, "image-cache");
        Directory.CreateDirectory(ImageCacheRoot);
        WorkRoot = TakeSlot(Path.Combine(parent, "run", sceneName));
        var store = Path.Combine(WorkRoot, "store");
        var temp = Path.Combine(WorkRoot, "temp");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(FilesRoot);

        // (1) 保存先。環境変数は location.json より先に見られる（StoreLocation.Resolve）。
        //     AppPaths.Default は最初に触れたときに1回だけ決まるので、アプリの型に触れる前に入れる
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, store);

        // (3) 一時展開の置き場（%TEMP%\Chmonos\unpacked）は一時フォルダから決まる。サービス一式を作ると
        //     「前回の消し残し」としてそこを消すので、開いているアプリの展開先を消さないよう、一時フォルダごと別にする
        Environment.SetEnvironmentVariable("TEMP", temp);
        Environment.SetEnvironmentVariable("TMP", temp);

        // 足跡（ui-check の CHMONOS_UITRACE）は、開いているアプリの確かめが読んでいる。台の分を混ぜない
        Environment.SetEnvironmentVariable("CHMONOS_UITRACE", null);

        // (2) 通信。届かない所を経由させて、どの HttpClient も外へ出られなくする（9番は捨てるだけのポートで、待ち受けが無い）。
        //     サービス一式は HttpClient を自分で作るので、差し替える口が無い。既定の経由先はプロセス全体に効く
        HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:9");

        var resolved = AppPaths.Default.Root;
        if (!string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(store), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"保存先を切り離せませんでした（{resolved}）。何も描かずに止めます。");
        }

        if (!Path.GetTempPath().StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"一時フォルダを切り離せませんでした（{Path.GetTempPath()}）。何も描かずに止めます。");
        }
    }

    private static FileStream? _slotLock;

    /// <summary>
    /// 作業用フォルダの場所を、プロセス番号ではなく「場面の名前＋空き番号」で決める。
    /// 保存先の場所は設定の画面の下に出るので、回ごとに違うと設定の 18 枚が毎回「違う」と出ていた（2026-10-05）。
    /// 同じ場面を同時に描く回（並べた2本の catalog）とぶつからないよう、番号の錠ファイルを排他で開けた者がその番号を使う。
    /// 空いている一番小さい番号を取るので、1本だけ走る通常の回は毎回同じ場所になる
    /// </summary>
    private static string TakeSlot(string sceneDirectory)
    {
        Directory.CreateDirectory(sceneDirectory);
        for (var slot = 1; ; slot++)
        {
            try
            {
                _slotLock = new FileStream(Path.Combine(sceneDirectory, $"{slot}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                continue;
            }

            var root = Path.Combine(sceneDirectory, slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(root))
            {
                // 落ちた回の消し残し。錠を取れたので、誰も使っていない
                Directory.Delete(root, recursive: true);
            }

            return root;
        }
    }

    public static void Leave()
    {
        try
        {
            if (WorkRoot.Length > 0 && Directory.Exists(WorkRoot))
            {
                Directory.Delete(WorkRoot, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 裏の作業がまだ書いている（ログ・辞書の控え）。次の回の錠を取った所で消える
        }

        _slotLock?.Dispose();
        _slotLock = null;
    }

    /// <summary>
    /// 落ちた回の消し残しを片付ける。場面の下の番号のフォルダは、錠が取れて（誰も使っていない）1時間より古い物だけ消す。
    /// 場面の入れ物と錠のファイルは残す（番号を同じ所に保つため）
    /// </summary>
    private static void SweepOld(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var sceneDirectory in Directory.EnumerateDirectories(parent))
        {
            foreach (var directory in Directory.EnumerateDirectories(sceneDirectory))
            {
                try
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) <= TimeSpan.FromHours(1))
                    {
                        continue;
                    }

                    using var probe = new FileStream(directory + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
