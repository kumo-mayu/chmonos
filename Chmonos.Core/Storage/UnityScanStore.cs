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

    /// <summary>
    /// <c>Packages/</c> に入っているパッケージ（plan 40）。**欠けていれば読んでいない**（この欄を足す前の控え）。
    /// 足しただけで形式の版は上げない：控えは消しても調べ直すだけの写しで、欠けた欄は「まだ読んでいない」と読めば足りる。
    /// </summary>
    public IReadOnlyList<VpmPackage>? Vpm { get; init; }
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
            // 手で直した控えの名前の無い行は読まない
            var vpm = file.Vpm?.Where(package => package is { Name.Length: > 0 }).ToList();
            return new UnityProjectScan(packages, images) { Vpm = vpm };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 読める控えの全部（外から使う API が並べる。plan 32）。1つずつは <see cref="Load"/> と同じ確かめ（中の場所とファイルの名前が合うか）を通す。
    /// <paramref name="SavedAt"/> はファイルを書いた日時（控えは答えの正本ではないので、いつ調べた物かを添える）。
    /// </summary>
    public IReadOnlyList<(string Project, UnityProjectScan Scan, DateTimeOffset SavedAt)> LoadAll()
    {
        var result = new List<(string, UnityProjectScan, DateTimeOffset)>();
        var directory = new DirectoryInfo(Path.Combine(paths.Root, AppPaths.UnityScansDirName));
        try
        {
            if (!directory.Exists)
            {
                return result;
            }

            foreach (var file in directory.EnumerateFiles("*.json"))
            {
                string? project;
                try
                {
                    project = JsonStore.Read<UnityScanFile>(file.FullName)?.Project;
                }
                catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                // 別の場所の名前で写された控え（手で写した）は Load と同じく読まない
                if (project is null || !string.Equals(FileKey(project) + ".json", file.Name, StringComparison.OrdinalIgnoreCase) || Load(project) is not { } scan)
                {
                    continue;
                }

                result.Add((project, scan, new DateTimeOffset(file.LastWriteTime)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return result;
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
                Vpm = scan.Vpm,
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("プロジェクトを調べた結果の控え", exception);
        }
    }

    /// <summary>
    /// プロジェクトの場所を付け替えたとき、控えを新しい場所の物にする（ユーザ判断 2026-11-10）。返すのは写したか。
    ///
    /// **消さずに写す。**中身は商品 ID・数と、プロジェクトの中の相対パス（<c>Assets/…</c>）だけで、置き場所に依らない。
    /// 移した直後に条件「Unityプロジェクト」や使っている商品の見張りを開いても、調べ終わるまで空にならない。
    /// 写した控えも答えの正本ではない（見せた後に必ず調べ直して差し替える）ので、古くても壊れない。
    /// 新しい場所に読める控えが既にあれば写さない：その場所を調べた物の方が正しい（別の改変が使うプロジェクトへ統合したとき）。
    /// 古い場所の控えは残す：右クリックから付け替えたときは古いフォルダがまだ在り、条件や見張りがそちらを見ていれば使う。
    /// 指す物が無くなれば読まれないだけで、1ファイル数KB。保存先から消しても壊れない（<see cref="UnityScanStore"/>）
    /// </summary>
    public bool CopyTo(string from, string to)
    {
        if (Load(to) is not null || Load(from) is not { } scan)
        {
            return false;
        }

        Save(to, scan);
        return true;
    }

    /// <summary>場所をそのまま名前にすると、区切りや長さでファイルにできない。16桁あれば手元のプロジェクトの数ではぶつからず、ぶつかっても中の場所を照らして読まない。</summary>
    private static string FileKey(string project)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ToUpperInvariant())))[..16];
}
