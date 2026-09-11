using System.Text.Json;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// どこに保存するかを覚えておくファイル（<c>location.json</c>）の中身。
/// </summary>
public sealed record StoreLocationFile
{
    /// <summary>保存先の絶対パス。</summary>
    public required string Root { get; init; }
}

/// <summary>保存先がどこから来たか。見つからなかったときの出し方を変えるために持つ。</summary>
public enum StoreRootSource
{
    /// <summary>既定（<c>%LOCALAPPDATA%\BoothAssetManager</c>）。</summary>
    Default,

    /// <summary>設定画面で選ばれた場所（<c>location.json</c>）。</summary>
    Configured,

    /// <summary>環境変数で差し替えられた場所。</summary>
    Environment,
}

public sealed record StoreRoot(string Path, StoreRootSource Source);

/// <summary>
/// 保存先の場所そのものを覚えておく仕組み。
///
/// この1件だけは保存先の中に置けない（場所を知るために場所を読む必要が出る）ので、
/// 既定の場所に置く。データをDドライブへ移しても
/// <c>%LOCALAPPDATA%\BoothAssetManager\</c> はこのファイルのためだけに残る。
///
/// レジストリを使わないのは「アプリを介さず開いて読める」方針に合わせるため。
/// </summary>
public static class StoreLocation
{
    /// <summary>既定の保存先。<c>location.json</c> もここに置く。</summary>
    public static string DefaultRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BoothAssetManager");

    public static string LocationFile => System.IO.Path.Combine(DefaultRoot, "location.json");

    /// <summary>
    /// 保存先を決める。優先順位は 環境変数 &gt; <c>location.json</c> &gt; 既定。
    ///
    /// 環境変数を一番上にしているのは、動作確認のときに本物へ触らないための逃げ道だから。
    /// 設定より弱いと、その逃げ道が塞がれてしまう。
    /// </summary>
    public static StoreRoot Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(AppPaths.RootVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new StoreRoot(System.IO.Path.GetFullPath(fromEnvironment.Trim()), StoreRootSource.Environment);
        }

        if (Read() is { } configured)
        {
            return new StoreRoot(configured, StoreRootSource.Configured);
        }

        return new StoreRoot(DefaultRoot, StoreRootSource.Default);
    }

    /// <summary>設定された保存先。無ければ null（＝既定を使う）。</summary>
    public static string? Read()
    {
        try
        {
            if (!File.Exists(LocationFile))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize<StoreLocationFile>(
                File.ReadAllText(LocationFile), JsonStore.Options);

            return string.IsNullOrWhiteSpace(parsed?.Root)
                ? null
                : System.IO.Path.GetFullPath(parsed.Root.Trim());
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 読めなければ既定に落とす。ここで止めると、手で壊したときに起動できなくなる
            return null;
        }
    }

    /// <summary>保存先を覚える。既定と同じ場所なら覚えずに消す（余計なファイルを残さない）。</summary>
    public static void Save(string root)
    {
        var full = System.IO.Path.GetFullPath(root);

        if (string.Equals(full.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                DefaultRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        Directory.CreateDirectory(DefaultRoot);
        JsonStore.Write(LocationFile, new StoreLocationFile { Root = full });
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(LocationFile))
            {
                File.Delete(LocationFile);
            }
        }
        catch (IOException)
        {
            // 消せなくても動作は変わらない（次に Save で上書きされる）
        }
    }

    /// <summary>
    /// そこに既にライブラリがあるか。
    /// 商品が1件も無くても、こちらの管理ファイルがあれば「使われている場所」と見る。
    /// </summary>
    public static bool LooksLikeStore(string root)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        var paths = new AppPaths(root);

        return Directory.Exists(paths.ItemsDir)
            || File.Exists(paths.SettingsFile)
            || File.Exists(paths.UserTagsFile);
    }

    /// <summary>保存先を選んだときに、その中に作るフォルダの名前。</summary>
    public const string FolderName = "BoothAssetManager";

    /// <summary>
    /// 選んだ場所から、実際に使う保存先を決める。
    ///
    /// **選んだ場所そのものではなく、その中の「BoothAssetManager」を使う。**そのまま使うと、
    /// ドキュメントやドライブの直下を選んだとき、そこに十数個のフォルダとJSONが散らばる。
    /// 友人は「選んだフォルダの中に1階層作ってくれる」と思って選んでいた。
    /// 既にライブラリがある場所と、名前が既に「BoothAssetManager」の場所は、そのまま使う。
    /// </summary>
    public static string RootFor(string picked)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(picked);
        return LooksLikeStore(trimmed)
            || string.Equals(System.IO.Path.GetFileName(trimmed), FolderName, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : System.IO.Path.Combine(trimmed, FolderName);
    }

    /// <summary>そのフォルダが空か（引越しの提案を出すかの判断に使う）。</summary>
    public static bool IsEmpty(string root)
        => !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any();
}
