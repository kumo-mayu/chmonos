using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoothZipInspector;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 変換ツールのプロファイルの <c>avatar_data_{slug}.json</c> を、zip の中の unitypackage から読む（docs/spec/conversion.md「データの形」・推す手掛かり (b)）。
/// どの GUID がそのファイルかは中身の一覧の控え（<see cref="UnityPackagePathStore"/>）で分かっているので、tar を1周して、その GUID の本体だけを読む
/// （pathname の後先を待つ取っておきは要らない）。中身は写さずに流して読み、本体は 2MB まで（`UnityPackageInspector` と同じ上限）。
/// </summary>
public static class ProfileDataReader
{
    /// <summary>本体の上限。<c>avatar_data</c> はブレンドシェイプの一覧を含むが、数百 KB に収まる。</summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// <c>avatar_data_{slug}.json</c> の場所。同じアバターの控え（<c>avatar_data_{slug}_unitysource.json</c>）も同じ形なので拾うが、
    /// 使うのは config の slug と同じ名前の物だけ（呼び手が slug で引く）。
    /// </summary>
    private static readonly Regex AvatarDataPath = new(
        @"(?:^|/)OutfitRetargetingSystem/Editor/(?:.*/)?avatar_data_(?<slug>[^/]+)\.json$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>中身の一覧に <c>avatar_data</c> があるか（無ければ zip を開かない）。</summary>
    public static bool HasAvatarData(IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> stored)
        => stored.Values.Any(assets => assets.Any(asset => AvatarDataPath.IsMatch(asset.Path)));

    /// <summary>
    /// 読む。開けない・壊れているときは null（控えに書かず、次の見回りでまた読む）。読めて何も無ければ空。
    /// </summary>
    public static IReadOnlyDictionary<string, ProfileAvatarData>? Read(
        string zipPath,
        IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> stored)
    {
        var result = new Dictionary<string, ProfileAvatarData>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, ZipNameEncoding.Instance);
            foreach (var (entryPath, assets) in stored)
            {
                var wanted = assets
                    .Select(asset => (asset.Guid, Match: AvatarDataPath.Match(asset.Path)))
                    .Where(pair => pair.Match.Success)
                    .ToDictionary(pair => pair.Guid, pair => pair.Match.Groups["slug"].Value, StringComparer.OrdinalIgnoreCase);
                if (wanted.Count == 0 || archive.GetEntry(entryPath) is not { } entry)
                {
                    continue;
                }

                ReadPackage(entry, wanted, result);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        return result;
    }

    private static void ReadPackage(ZipArchiveEntry entry, Dictionary<string, string> wanted, Dictionary<string, ProfileAvatarData> result)
    {
        var left = wanted.Count;
        try
        {
            using var entryStream = entry.Open();
            using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (left > 0 && tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                var parts = tarEntry.Name.TrimStart('.', '/').Split('/');
                if (parts.Length < 2 || parts[^1] != "asset" || tarEntry.DataStream is null || tarEntry.Length > MaxBytes
                    || !wanted.TryGetValue(parts[0], out var slug))
                {
                    continue;
                }

                left--;
                if (Parse(tarEntry.DataStream) is { } data)
                {
                    result[slug] = data;
                }
            }
        }
        // 壊れた unitypackage は、そこまでに読めた分だけ使う（UnityPackageInspector と同じ受け方）
        catch (Exception exception) when (exception is InvalidDataException or IOException or FormatException
            or ArgumentException or ArithmeticException or InvalidOperationException)
        {
        }
    }

    /// <summary>欄のうち <c>name</c> と <c>defaultFBXPath</c> だけを読む。JSON として読めなければ null。</summary>
    internal static ProfileAvatarData? Parse(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Text(string name)
                => document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text
                        ? text
                        : null;

            return new ProfileAvatarData { Name = Text("name"), DefaultFbxPath = Text("defaultFBXPath")?.Replace('\\', '/') };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
