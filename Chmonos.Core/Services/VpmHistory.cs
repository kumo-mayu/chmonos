using System.Text.Json.Serialization;

namespace Chmonos.Core.Services;

/// <summary>
/// VPM 1つの、ある時からの状態（ユーザ判断 2026-11-12「作りましょう」）。
/// **終わりの時は持たない**：次の行の <see cref="From"/> で分かる（計算で出せる値は控えに書かない）。最後の行が今の状態。
/// </summary>
/// <param name="From">
/// Chmonos が調べて、この状態に気付いた時（秒まで・手元の時刻帯）。**入れた時ではない**：<c>package.json</c> の更新日時は展開し直しで変わるので使わない。
/// 日時で持つのは、改変の記録（<c>addedAt</c> は日時）と照らして「その時の版」を引けるようにするため。画面は日付だけを見せる。
/// </param>
/// <param name="Version">その時の版。読めなかった物は null。</param>
/// <param name="Removed">プロジェクトから無くなった（外した）印。版の null とは分ける（版の読めない物が入っていることがある）。</param>
public sealed record VpmHistoryEntry(DateTimeOffset From, string? Version, bool Removed);

/// <summary>VPM 1つの履歴。<see cref="Changes"/> は古い順で、1行以上ある。</summary>
/// <param name="Name">パッケージの名前（<c>jp.lilxyzw.liltoon</c>）。照らすときは大文字小文字を区別しない（読みと同じ）。</param>
/// <param name="DisplayName">最後に読めた表示名。外した後も名前で見せるために持つ。</param>
public sealed record VpmPackageHistory(string Name, string? DisplayName, IReadOnlyList<VpmHistoryEntry> Changes)
{
    /// <summary>人に見せる名前（<see cref="VpmPackage.Shown"/> と同じ）。</summary>
    [JsonIgnore]
    public string Shown => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    /// <summary>今の状態（最後の行）。</summary>
    [JsonIgnore]
    public VpmHistoryEntry Current => Changes[^1];
}

/// <summary>
/// プロジェクトの VPM の履歴を、調べるたびに進める（ディスクを見ない。時刻は呼び手が渡す）。
///
/// 控え（<c>unityscans/</c>）の <c>vpm</c> は調べるたびに今の姿で書き直すが、履歴だけは前の控えから引き継いで足す。
/// 版が上がった・下がった・消えた・また入った、をそれぞれ1行にする。上限は置かない（1プロジェクト数十個で、変わるのはまれ）。
/// </summary>
public static class VpmHistories
{
    /// <summary>
    /// 前の履歴に、今読んだ姿を足す。<paramref name="previous"/> が null（この欄の無い前の控え・初めて調べた）なら、今の姿を最初の1行にする。
    /// それより前の行は作らない（いつ入ったかは分からない）。
    ///
    /// 版は文字列の違いだけで見る（上下は解釈しない。VPM の版の書き方は物ごとに揃っていない）。
    /// 入っている物の版が読めない（null）ときは、前の版を変えない（展開し直しの途中などで一時的に読めないことがある）。
    /// <paramref name="current"/> はフォルダの無い manifest だけの行も「入っている」に数える（VCC の入れ替えの途中に当たっても「消えた」にしない）。
    /// </summary>
    public static IReadOnlyList<VpmPackageHistory> Advance(
        IReadOnlyList<VpmPackageHistory>? previous, IReadOnlyList<VpmPackage> current, DateTimeOffset now)
    {
        var present = new Dictionary<string, VpmPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in current)
        {
            present.TryAdd(package.Name, package);
        }

        var result = new List<VpmPackageHistory>();
        foreach (var history in previous ?? [])
        {
            var last = history.Current;
            if (present.Remove(history.Name, out var package))
            {
                var displayName = package.DisplayName ?? history.DisplayName;
                var changed = last.Removed || (package.Version is not null && !string.Equals(package.Version, last.Version, StringComparison.Ordinal));
                result.Add(changed
                    ? history with { DisplayName = displayName, Changes = [.. history.Changes, new VpmHistoryEntry(now, package.Version, Removed: false)] }
                    : history with { DisplayName = displayName });
            }
            else
            {
                result.Add(last.Removed
                    ? history
                    : history with { Changes = [.. history.Changes, new VpmHistoryEntry(now, Version: null, Removed: true)] });
            }
        }

        // 初めて見た物（前の控えに無い物）。前の控えが無いときは全部がここに来る
        result.AddRange(present.Values.Select(package =>
            new VpmPackageHistory(package.Name, package.DisplayName, [new VpmHistoryEntry(now, package.Version, Removed: false)])));

        // 控えを人が読むときに探しやすいよう、名前の順に並べる
        return result.OrderBy(history => history.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// ある時の状態（その時までに気付いた最後の行）。履歴の最初の行より前なら null（その頃の事は分からない）。
    /// 改変ごとに「使った物を記録した時の VPM の版」を引くために使える。
    /// </summary>
    public static VpmHistoryEntry? StateAt(VpmPackageHistory history, DateTimeOffset at)
        => history.Changes.LastOrDefault(entry => entry.From <= at);

    /// <summary>ある時に入っていた VPM と版（名前で引く）。外していた物・その時より後に初めて見た物は入れない。</summary>
    public static IReadOnlyDictionary<string, string?> VersionsAt(IEnumerable<VpmPackageHistory> histories, DateTimeOffset at)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var history in histories)
        {
            if (StateAt(history, at) is { Removed: false } state)
            {
                result.TryAdd(history.Name, state.Version);
            }
        }

        return result;
    }

    /// <summary>手で直した控えの欠けた行を除いて、古い順に揃える（欠けた配列は空として受ける決まり）。行の残らない物は除く。</summary>
    internal static IReadOnlyList<VpmPackageHistory> Clean(IReadOnlyList<VpmPackageHistory?> histories)
        => histories
            .Where(history => history is { Name.Length: > 0 })
            .Select(history => history! with
            {
                Changes = (history.Changes ?? []).Where(entry => entry is not null).OrderBy(entry => entry.From).ToList(),
            })
            .Where(history => history.Changes.Count > 0)
            .ToList();
}
