using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>Unity プロジェクトを調べた結果の控え1つ（<c>/v1/unity-scans</c>）。</summary>
/// <param name="Project">調べたプロジェクトの場所（控えに書かれたまま）。</param>
/// <param name="SavedAt">控えを書いた日時。**答えの正本ではない**：プロジェクトの中はアプリの外で変わり、画面は見せた後に調べ直す。API は調べ直さない。</param>
/// <param name="Items">unitypackage で入っている商品（画面の並び＝入っている割合の高い順）。</param>
/// <param name="Images">手で置いた画像として見つかった物。</param>
/// <param name="Vpm">
/// <c>Packages/</c> に入っているパッケージ（plan 40。VPM と、manifest に無い埋め込みの物）。**null はまだ読んでいない**（この欄を足す前の控え）。
/// 照合と同じ時に読んだ物で、API は読み直さない
/// </param>
/// <param name="VpmHistory">
/// VPM ごとの履歴（ユーザ判断 2026-11-12）。外した物も残る。**null は履歴なし**（この欄を足す前の控えで、まだ調べ直していない）。
/// </param>
public sealed record UnityScanRef(
    string Project, DateTimeOffset SavedAt, IReadOnlyList<UnityScanItemRef> Items, IReadOnlyList<UnityScanImageRef> Images, IReadOnlyList<UnityScanVpmRef>? Vpm,
    IReadOnlyList<UnityScanVpmHistoryRef>? VpmHistory);

/// <param name="Name">パッケージの名前。</param>
/// <param name="DisplayName">最後に読めた表示名。</param>
/// <param name="Changes">古い順。最後の行が今の状態。終わりの時は次の行の <c>from</c>。</param>
public sealed record UnityScanVpmHistoryRef(string Name, string? DisplayName, IReadOnlyList<UnityScanVpmChangeRef> Changes);

/// <param name="From">Chmonos が調べて気付いた時（入れた時ではない）。</param>
/// <param name="Version">その時の版。外した行と、読めなかった物は null。</param>
/// <param name="Removed">プロジェクトから無くなった行か。</param>
public sealed record UnityScanVpmChangeRef(DateTimeOffset From, string? Version, bool Removed);

/// <param name="Name">パッケージの名前（<c>jp.lilxyzw.liltoon</c>）。</param>
/// <param name="DisplayName"><c>package.json</c> の表示名。読めなければ null。</param>
/// <param name="Version">フォルダがあれば <c>package.json</c> の版、無ければ manifest の版。</param>
/// <param name="Folder"><c>Packages/</c> の下のフォルダの名前。manifest にだけ書かれていてフォルダが無ければ null。</param>
/// <param name="InManifest"><c>vpm-manifest.json</c> の <c>dependencies</c> か <c>locked</c> にあるか（無いのは手で置いた埋め込みの物）。</param>
public sealed record UnityScanVpmRef(string Name, string? DisplayName, string? Version, string? Folder, bool InManifest);

/// <param name="ItemName">商品の表示名。商品がもう無ければ null。</param>
/// <param name="Present">プロジェクトに見つかったアセットの数（共有の部品は数えない。<see cref="UnityProjectMatcher"/>）。</param>
/// <param name="Total">その商品のアセットの数（共有の部品を除く）。</param>
/// <param name="Ratio">入っている割合（0〜1。画面の並べ方と同じ <see cref="UnityProjectMatch.Ratio"/>）。</param>
public sealed record UnityScanItemRef(string ItemId, string? ItemName, int Present, int Total, double Ratio);

/// <param name="ProjectPath">プロジェクトの中の場所（<c>Assets/…</c>）。</param>
/// <param name="SourceName">商品の側の画像の名前。</param>
/// <param name="Kind"><c>sameImage</c>（大きさと中身が同じ）・<c>sameName</c>（名前だけ同じ）。</param>
public sealed record UnityScanImageRef(string ItemId, string? ItemName, string ProjectPath, string SourceName, string Kind);

/// <summary>Unity プロジェクトを調べた結果の控え（<c>unityscans/</c>。<see cref="UnityScanStore"/>）を読む道（plan 32）。</summary>
public static class UnityScans
{
    /// <param name="project">渡せばその場所の物だけ（<see cref="UnityProjectPath.Normalize"/> で揃えて、大文字小文字を区別せずに照らす。条件 <c>unityProject</c> と同じ）。</param>
    public static IReadOnlyList<UnityScanRef> All(QuerySnapshot snapshot, string? project)
    {
        var wanted = string.IsNullOrWhiteSpace(project) ? null : UnityProjectPath.Normalize(project);
        string? NameOf(string itemId) => snapshot.ItemsById.GetValueOrDefault(itemId)?.DisplayName;

        return new UnityScanStore(snapshot.Paths).LoadAll()
            .Where(entry => wanted is null || string.Equals(UnityProjectPath.Normalize(entry.Project), wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Project, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new UnityScanRef(
                entry.Project,
                entry.SavedAt,
                entry.Scan.Packages.Select(match => new UnityScanItemRef(match.ItemId, NameOf(match.ItemId), match.Present, match.Total, match.Ratio)).ToList(),
                entry.Scan.Images
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .SelectMany(pair => pair.Value.Select(image => new UnityScanImageRef(
                        pair.Key, NameOf(pair.Key), image.ProjectPath, image.SourceName,
                        image.Kind == UnityImageMatchKind.SameImage ? "sameImage" : "sameName")))
                    .ToList(),
                entry.Scan.Vpm?.Select(package => new UnityScanVpmRef(package.Name, package.DisplayName, package.Version, package.Folder, package.InManifest)).ToList(),
                entry.Scan.VpmHistory?.Select(history => new UnityScanVpmHistoryRef(
                    history.Name, history.DisplayName,
                    history.Changes.Select(change => new UnityScanVpmChangeRef(change.From, change.Version, change.Removed)).ToList())).ToList()))
            .ToList();
    }
}
