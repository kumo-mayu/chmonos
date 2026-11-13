using System.Text.Json;

namespace Chmonos.Core.Services;

/// <summary>
/// Unity プロジェクトの <c>Packages/</c> に入っているパッケージ1つ（plan 40・ユーザ判断 2026-11-11）。
/// </summary>
/// <param name="Name">パッケージの名前（<c>jp.lilxyzw.liltoon</c>）。</param>
/// <param name="DisplayName"><c>package.json</c> の表示名（<c>lilToon</c>）。読めなければ null。</param>
/// <param name="Version">版。フォルダがあれば <c>package.json</c> の版（実際に入っている物）、無ければ manifest に書かれた版。</param>
/// <param name="Folder"><c>Packages/</c> の下のフォルダの名前。フォルダが無ければ null（manifest にだけ書かれている）。</param>
/// <param name="InManifest"><c>vpm-manifest.json</c> の <c>dependencies</c> か <c>locked</c> に書かれているか。書かれていないのは、手で置いた埋め込みの物。</param>
public sealed record VpmPackage(string Name, string? DisplayName, string? Version, string? Folder, bool InManifest)
{
    /// <summary>人に見せる名前（表示名、無ければ名前）。計算で出せるので控えに書かない。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Shown => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
}

/// <summary>
/// Unity プロジェクトに VPM（VCC・ALCOM）で入っているパッケージを読む。**読むだけで、入れる・消す・版を変えるはしない。**
///
/// なぜ控えるか：VPM で入っている物と同じ GUID・違う中身の物を unitypackage で取り込むと、Unity は <c>Assets/</c> に作らず、
/// <c>Packages/</c> の中の VPM のファイルを黙って上書きする（2026-11-11 に確かめた。<c>docs/research/vpm-duplicate-guid.md</c>）。
/// 送る前に重なりを言うには、どのフォルダがどの VPM かを知っている必要がある。
///
/// 読む物は2つ：
/// - <c>Packages/vpm-manifest.json</c> の <c>dependencies</c>（人が入れた物）と <c>locked</c>（それが連れてきた物）。
///   **<c>locked</c> も読む**：確かめ用のプロジェクトで、Modular Avatar が連れてきた NDMF は <c>locked</c> にだけ書かれていた。
///   <c>dependencies</c> だけ見ると、それを「手で置いた物」と取り違える。
/// - <c>Packages/*/package.json</c>：manifest に無い、手で置いた埋め込みの物も拾う（unitypackage で上書きされるのは同じ）。
/// </summary>
public static class VpmPackages
{
    /// <summary>読み取りの上限。手元の manifest は 1.4KB・package.json は 1KB ほど。壊れた巨大なファイルで止まらないため、桁を大きく外れた物は読まない。</summary>
    private const long MaxJsonBytes = 1024 * 1024;

    /// <summary>ディスクから読む。フォルダが無い・読めない物は飛ばす（読めた分だけ返す）。</summary>
    public static IReadOnlyList<VpmPackage> Read(string projectPath) => ReadCore(projectPath).Packages;

    /// <summary>
    /// ディスクから読む。**全部を読めたと言えないときは null**：<c>Packages/</c> が無い（ドライブが外れた・プロジェクトでない）、
    /// 一覧の途中で読めなくなった、manifest が在るのに読めない。
    /// 控えの履歴（<see cref="VpmHistories"/>）は、欠けた読みを「全部消えた」と記録しないよう、null のときは前の控えを残す。
    /// 送る前の照らし（<see cref="Read"/>）は、読めた分だけで照らす方を取る（照らせない物は黙って送る決まり）。
    /// </summary>
    public static IReadOnlyList<VpmPackage>? TryRead(string projectPath)
        => ReadCore(projectPath) is { Complete: true } read ? read.Packages : null;

    private static (IReadOnlyList<VpmPackage> Packages, bool Complete) ReadCore(string projectPath)
    {
        var packages = Path.Combine(projectPath, "Packages");
        var folders = new List<(string Folder, string? PackageJson)>();
        var complete = true;
        try
        {
            if (!Directory.Exists(packages))
            {
                return ([], false);
            }

            foreach (var folder in Directory.EnumerateDirectories(packages))
            {
                var name = Path.GetFileName(folder);

                // 隠しフォルダ（.git など）は Unity もパッケージとして読まない
                if (name.StartsWith('.'))
                {
                    continue;
                }

                var packageJsonPath = Path.Combine(folder, "package.json");
                var packageJson = ReadText(packageJsonPath);

                // 在るのに読めない・壊れている（展開の途中など）。manifest に無い埋め込みの物が、その回だけ「外した」と記録されないように
                if (packageJson is null ? File.Exists(packageJsonPath) : ParsePackageJson(packageJson) is null)
                {
                    complete = false;
                }

                folders.Add((name, packageJson));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 一覧の途中で読めなくなっても、読めた分は使う
            complete = false;
        }

        var manifestPath = Path.Combine(packages, "vpm-manifest.json");
        var manifest = ReadText(manifestPath);
        var entries = manifest is null ? null : ParseManifest(manifest);

        // VCC が書いている最中などで在るのに読めない・途中で切れている・ルートの形が違うと、manifest にだけある物が「消えた」と見える。
        // 読めた文字だけで完全とすると、壊れた manifest を「全部外した」と履歴に書き、改変の控えも空にする（2026-11-13 の点検 件5）
        complete &= manifest is null ? !File.Exists(manifestPath) : entries is not null;
        return (Build(entries ?? [], folders), complete);
    }

    /// <summary>
    /// 読んだ中身から組む（ディスクを見ない。試験はここを使う）。
    /// <paramref name="folders"/> は <c>Packages/</c> の下のフォルダの名前と、その <c>package.json</c> の中身（無ければ null）。
    /// <c>package.json</c> を持たないフォルダはパッケージではない（Unity も読まない）ので並べない。
    /// 壊れた JSON・欠けた欄は無いのと同じに扱う（読めた分だけ返す。壊れたことを知りたいときは <see cref="TryRead"/>）。並びは見せる名前の順。
    /// </summary>
    public static IReadOnlyList<VpmPackage> Parse(string? manifestJson, IReadOnlyList<(string Folder, string? PackageJson)> folders)
        => Build((manifestJson is null ? null : ParseManifest(manifestJson)) ?? [], folders);

    private static IReadOnlyList<VpmPackage> Build(IReadOnlyList<(string Name, string? Version)> manifest, IReadOnlyList<(string Folder, string? PackageJson)> folders)
    {
        // フォルダの名前はふつうパッケージの名前と同じだが、手で置いた物は違うことがある。package.json の名前を正とする
        var onDisk = new Dictionary<string, (string Folder, string? DisplayName, string? Version)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, packageJson) in folders)
        {
            if (packageJson is null || ParsePackageJson(packageJson) is not { } info)
            {
                continue;
            }

            onDisk.TryAdd(string.IsNullOrWhiteSpace(info.Name) ? folder : info.Name, (folder, info.DisplayName, info.Version));
        }

        var result = new List<VpmPackage>();
        foreach (var (name, version) in manifest)
        {
            result.Add(onDisk.Remove(name, out var disk)
                ? new VpmPackage(name, disk.DisplayName, disk.Version ?? version, disk.Folder, InManifest: true)
                : new VpmPackage(name, null, version, null, InManifest: true));
        }

        result.AddRange(onDisk.Select(pair => new VpmPackage(pair.Key, pair.Value.DisplayName, pair.Value.Version, pair.Value.Folder, InManifest: false)));
        return result
            .OrderBy(package => package.Shown, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// manifest の名前と版（<c>locked</c> の版が先。無ければ <c>dependencies</c> の版）。
    /// **壊れた JSON・ルートがオブジェクトでない物は null**：読めた空の一覧と分け、<see cref="TryRead"/> を null にするため。
    /// 欄が無い・欄の形が違うのは null にしない（書かれていないのと同じに読み、版の読めない物は版を null にする）。
    /// </summary>
    private static IReadOnlyList<(string Name, string? Version)>? ParseManifest(string json)
    {
        var versions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // dependencies を先に並べ、locked で版を上書きする（locked が実際に解決した版）
            foreach (var section in new[] { "dependencies", "locked" })
            {
                if (!document.RootElement.TryGetProperty(section, out var entries) || entries.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    var version = entry.Value.ValueKind == JsonValueKind.Object ? StringOf(entry.Value, "version") : null;
                    if (!versions.ContainsKey(entry.Name))
                    {
                        order.Add(entry.Name);
                        versions[entry.Name] = version;
                    }
                    else if (version is not null)
                    {
                        versions[entry.Name] = version;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return order.Select(name => (name, versions[name])).ToList();
    }

    private static (string? Name, string? DisplayName, string? Version)? ParsePackageJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? (StringOf(document.RootElement, "name"), StringOf(document.RootElement, "displayName"), StringOf(document.RootElement, "version"))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text.Trim()
            : null;

    private static string? ReadText(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxJsonBytes ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
