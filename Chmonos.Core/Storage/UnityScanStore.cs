using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Services;

namespace Chmonos.Core.Storage;

/// <summary><c>unityscans/&lt;場所のハッシュ&gt;.json</c> の中身。</summary>
public sealed class UnityScanFile
{
    /// <summary>調べたプロジェクトの場所。</summary>
    public string? Project { get; init; }

    public IReadOnlyList<UnityScanPackageRow>? Packages { get; init; }

    /// <summary>商品ごとの、手で置いた画像。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<UnityImageMatch>>? Images { get; init; }
}

/// <summary>unitypackage で入っている商品1つ。</summary>
public sealed record UnityScanPackageRow
{
    public required string ItemId { get; init; }

    public required int Present { get; init; }

    public required int Total { get; init; }
}

/// <summary>
/// プロジェクトの中を調べた結果の控え（ユーザ判断 2026-11-02）。
/// 条件「Unityプロジェクト」は保存されて次の起動でも効くが、調べ直すまで結果が無いと、
/// 手元の測定で 782件 0.6〜4.7秒・2万件 6.6〜9.4秒のあいだ一覧が空になる。その間は前回の結果を見せる。
///
/// **答えの正本にはしない。**プロジェクトの中身はアプリの外で変わるので、見せた後に必ず調べ直して差し替える。
/// 消しても次に調べ直すだけで壊れない。バックアップにも入れない（<see cref="BackupArchive"/>）。
/// </summary>
public sealed class UnityScanStore(AppPaths paths)
{
    /// <summary>読めなければ null（無い・壊れている・別のプロジェクトの物）。</summary>
    public UnityProjectScan? Load(string project)
    {
        try
        {
            if (JsonStore.Read<UnityScanFile>(paths.UnityScanFile(FileKey(project))) is not { } file
                || !string.Equals(file.Project, project, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 手で直した控えの欠けた行は読まない（欠けた配列を空として受けるのと同じ）
            var packages = (file.Packages ?? [])
                .Where(row => row is { ItemId.Length: > 0 })
                .Select(row => new UnityProjectMatch(row.ItemId, row.Present, row.Total))
                .ToList();
            var images = (file.Images ?? new Dictionary<string, IReadOnlyList<UnityImageMatch>>())
                .Where(pair => pair.Value is not null)
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<UnityImageMatch>)pair.Value.Where(match => match is { ProjectPath.Length: > 0 }).ToList(),
                    StringComparer.Ordinal);
            return new UnityProjectScan(packages, images);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 控える。書けなくても調べた結果は使えるので、失敗は呼び手へ伝えない（次の起動で空から調べるだけ）。
    /// 同期なのは、呼び手が調べる裏のスレッドだから（画面のスレッドから呼ばない）。
    /// </summary>
    public void Save(string project, UnityProjectScan scan)
    {
        try
        {
            JsonStore.Write(paths.UnityScanFile(FileKey(project)), new UnityScanFile
            {
                Project = project,
                Packages = scan.Packages
                    .Select(match => new UnityScanPackageRow { ItemId = match.ItemId, Present = match.Present, Total = match.Total })
                    .ToList(),
                Images = scan.Images,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("プロジェクトを調べた結果の控え", exception);
        }
    }

    /// <summary>場所をそのまま名前にすると、区切りや長さでファイルにできない。16桁あれば手元のプロジェクトの数ではぶつからず、ぶつかっても中の場所を照らして読まない。</summary>
    private static string FileKey(string project)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ToUpperInvariant())))[..16];
}
