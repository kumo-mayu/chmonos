using System.Text.Json;

namespace Chmonos.Core.Services;

/// <summary>どこで見つけたプロジェクトか。人に見せるのと、同じものを2度出さないために持つ。</summary>
[Flags]
public enum UnityProjectSource
{
    None = 0,

    /// <summary>Unity Hub の一覧に載っている。</summary>
    Hub = 1,

    /// <summary>VRChat Creator Companion の一覧に載っている。</summary>
    Vcc = 2,
}

/// <summary>
/// 紐付けの候補に出すUnityプロジェクト1件。
/// </summary>
public sealed record UnityProjectCandidate
{
    public required string Path { get; init; }

    /// <summary>人に見せる名前。**フォルダ名を使う。**Hubの表示名は後から変えられる</summary>
    public required string Name { get; init; }

    /// <summary>置き場所（親フォルダ）。同名のプロジェクトを見分けるために出す。</summary>
    public required string Folder { get; init; }

    /// <summary>
    /// Unityのバージョン。<c>ProjectSettings/ProjectVersion.txt</c> から読む。
    /// **読めなければ null のまま。**Hubの一覧の値は古くなることがある
    /// （プロジェクトを別のバージョンで開いても一覧はすぐには追いつかない）
    /// </summary>
    public string? Version { get; init; }

    /// <summary>実在するか。消えていても候補から黙って外さない（指し直す導線を出す）。</summary>
    public required bool Exists { get; init; }

    /// <summary>いま開いているか。<c>Temp/UnityLockfile</c> の有無で見る。</summary>
    public required bool IsOpen { get; init; }

    public required UnityProjectSource Source { get; init; }

    /// <summary>最後に触った時刻。並び順に使う。読めなければ null。</summary>
    public DateTimeOffset? LastWrite { get; init; }
}

/// <summary>
/// Unityプロジェクトを探す。
///
/// **依存を増やさない。**必要な材料は全部、素のJSONとファイルで手に入る
/// （調べた結果は <c>docs/history/modifications.md</c> の5章）。
/// </summary>
public static class UnityProjects
{
    /// <summary>Unity Hub が持っている一覧。</summary>
    public static string HubProjectsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnityHub",
        "projects-v1.json");

    /// <summary>VCC が持っている一覧。</summary>
    public static string VccSettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatCreatorCompanion",
        "settings.json");

    /// <summary>
    /// Unity Hub の <c>projects-v1.json</c> からパスを取り出す。
    ///
    /// 形は <c>{"data":{"&lt;パス&gt;":{"path":"&lt;パス&gt;",…}}}</c>。
    /// **キーではなく <c>path</c> を見る。**キーはパスと同じだが、
    /// そう決まっているとは書かれていない。
    /// </summary>
    public static IReadOnlyList<string> PathsFromHubJson(string json)
    {
        var found = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return found;
            }

            foreach (var entry in data.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var path = entry.Value.TryGetProperty("path", out var value)
                    && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : entry.Name;

                if (!string.IsNullOrWhiteSpace(path))
                {
                    found.Add(path);
                }
            }
        }
        catch (JsonException)
        {
            // 他のアプリが書いたファイル。形が変わっていたら候補が減るだけで済ませる
        }

        return found;
    }

    /// <summary>VCC の <c>settings.json</c> の <c>userProjects</c> からパスを取り出す。</summary>
    public static IReadOnlyList<string> PathsFromVccSettings(string json)
    {
        var found = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("userProjects", out var projects)
                || projects.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (var entry in projects.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String
                    && entry.GetString() is { Length: > 0 } path)
                {
                    found.Add(path);
                }
            }
        }
        catch (JsonException)
        {
        }

        return found;
    }

    /// <summary>
    /// <c>ProjectVersion.txt</c> からバージョンを読む。
    ///
    /// 中身は <c>m_EditorVersion: 2022.3.22f1</c> の2行。
    /// <c>m_EditorVersionWithRevision</c> の方はハッシュが付くので使わない。
    /// </summary>
    public static string? VersionFromProjectVersionText(string text)
    {
        const string key = "m_EditorVersion:";

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            // 版の形でなければ版の分からないプロジェクトと同じに扱う。版はエディタの場所の名前
            // （Hub の置き場所\{版}\Editor\Unity.exe）と Hub へのリンクになるので、区切りや .. を含む値を通すと
            // 指定外の実行ファイルを選べた（2026-10-06 外部の点検・L106）
            var value = trimmed[key.Length..].Trim();
            return IsEditorVersion(value) ? value : null;
        }

        return null;
    }

    // Unity の版の形：年（か 6000 のような大きい番号）.小.修正 と、種別（a 開発・b 試験・f 正式・p 修正・x 試作）の番号。
    // 中国版は後ろに c と番号が付く（2022.3.22f1c1）。例 2022.3.22f1・6000.0.23f1・5.6.7f1
    private static readonly System.Text.RegularExpressions.Regex EditorVersionPattern = new(
        @"\A[0-9]{1,4}\.[0-9]{1,3}\.[0-9]{1,3}[abfpx][0-9]{1,3}(?:c[0-9]{1,3})?\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Unity の版の形か（<c>2022.3.22f1</c>）。版から場所やリンクを組む前に見る。</summary>
    public static bool IsEditorVersion([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? version)
        => version is not null && EditorVersionPattern.IsMatch(version);

    /// <summary>
    /// いま開いているか。
    ///
    /// Unityは開いている間 <c>Temp/UnityLockfile</c> を置く。**落ちると残る**ので
    /// 「開いている」の断定には使わない——手前に出そうとして見つからなければ、
    /// そのとき開き直せばよい（<see cref="UnityHandoff"/> 側の判断）。
    /// </summary>
    /// <summary>
    /// Hub と VCC の一覧に載っているプロジェクトの場所だけを返す（<see cref="Discover"/> と違い、中のファイルは見ない）。
    /// 開いている Unity の窓の題から、どのプロジェクトかを言い当てるのに使う（呼ばれる回数が多いので軽くしておく）
    /// </summary>
    public static IReadOnlyList<string> KnownPaths(string? hubProjectsFile = null, string? vccSettingsFile = null)
    {
        var paths = new List<string>();
        Read(string.IsNullOrWhiteSpace(hubProjectsFile) ? HubProjectsFile : hubProjectsFile, PathsFromHubJson);
        Read(string.IsNullOrWhiteSpace(vccSettingsFile) ? VccSettingsFile : vccSettingsFile, PathsFromVccSettings);
        return [.. paths
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        void Read(string file, Func<string, IReadOnlyList<string>> parse)
        {
            try
            {
                if (File.Exists(file))
                {
                    paths.AddRange(parse(File.ReadAllText(file)));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 読めない一覧は無いものとして扱う（題の先頭で読む元の方法に戻るだけ）
            }
        }
    }

    public static bool IsProjectOpen(string projectPath) => IsProjectOpen(projectPath, IsAnyEditorRunning());

    /// <summary>
    /// 印（<c>Temp/UnityLockfile</c>）があり、**Unity が1つでも起動しているときだけ**開いているとみる。
    /// 落ちて残った印だけで「開いています」と出し、開いてもいないのに開けなくしていた
    /// （ユーザ指摘 2026-09-14。Unity が1つも起動していないのに kip01 に印が残っていた）。
    /// 別のプロジェクトの Unity が起動していて印も残っている場合は見分けられないが、それは稀
    /// </summary>
    public static bool IsProjectOpen(string projectPath, bool anyEditorRunning)
    {
        if (!anyEditorRunning)
        {
            return false;
        }

        try
        {
            return File.Exists(Path.Combine(projectPath, "Temp", "UnityLockfile"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Unity のエディタが1つでも起動しているか。</summary>
    public static bool IsAnyEditorRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Unity");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Unityプロジェクトの体裁になっているか。<c>Assets</c> と <c>ProjectSettings</c> で見る。</summary>
    public static bool LooksLikeProject(string projectPath)
    {
        try
        {
            return Directory.Exists(Path.Combine(projectPath, "Assets"))
                && Directory.Exists(Path.Combine(projectPath, "ProjectSettings"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>1件を、画面に出せる形にする。ディスクを読むのはここだけ。</summary>
    /// <param name="anyEditorRunning">Unity が起動しているか。まとめて作るときは呼ぶ側が1回だけ調べて渡す（省けばここで調べる）。</param>
    public static UnityProjectCandidate Describe(string projectPath, UnityProjectSource source, bool? anyEditorRunning = null)
    {
        var path = projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exists = false;
        string? version = null;
        DateTimeOffset? lastWrite = null;

        try
        {
            exists = Directory.Exists(path);
            if (exists)
            {
                var versionFile = Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
                if (File.Exists(versionFile))
                {
                    version = VersionFromProjectVersionText(File.ReadAllText(versionFile));
                }

                lastWrite = new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めないものは「読めなかった」まま出す。推定で埋めない
        }

        return new UnityProjectCandidate
        {
            Path = path,
            Name = Path.GetFileName(path) is { Length: > 0 } name ? name : path,
            Folder = Path.GetDirectoryName(path) ?? string.Empty,
            Version = version,
            Exists = exists,
            IsOpen = exists && IsProjectOpen(path, anyEditorRunning ?? IsAnyEditorRunning()),
            Source = source,
            LastWrite = lastWrite,
        };
    }

    /// <summary>
    /// HubとVCCの一覧を合わせて候補を作る。
    ///
    /// **開いているものを先に、次に新しく触ったものを先に出す。**
    /// 紐付けたいのは大抵いま作業しているプロジェクトなので、
    /// 名前順に並べると毎回探すことになる。
    /// </summary>
    public static IReadOnlyList<UnityProjectCandidate> Discover(
        string? hubProjectsFile = null,
        string? vccSettingsFile = null,
        bool? anyEditorRunning = null)
    {
        var sources = new Dictionary<string, UnityProjectSource>(StringComparer.OrdinalIgnoreCase);

        Collect(Or(hubProjectsFile, HubProjectsFile), UnityProjectSource.Hub, PathsFromHubJson);
        Collect(Or(vccSettingsFile, VccSettingsFile), UnityProjectSource.Vcc, PathsFromVccSettings);

        static string Or(string? given, string fallback) =>
            string.IsNullOrWhiteSpace(given) ? fallback : given;

        // 試験では明示する（このマシンで Unity が動いているかで「開いている」の判定が変わり、結果が揺れた）
        var running = anyEditorRunning ?? IsAnyEditorRunning();
        return [.. sources
            .Select(pair => Describe(pair.Key, pair.Value, running))
            .OrderByDescending(candidate => candidate.IsOpen)
            .ThenByDescending(candidate => candidate.LastWrite ?? DateTimeOffset.MinValue)
            .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)];

        void Collect(string file, UnityProjectSource source, Func<string, IReadOnlyList<string>> parse)
        {
            string json;
            try
            {
                if (!File.Exists(file))
                {
                    return;
                }

                json = File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var path in parse(json))
            {
                var key = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                sources[key] = sources.TryGetValue(key, out var already) ? already | source : source;
            }
        }
    }
}
