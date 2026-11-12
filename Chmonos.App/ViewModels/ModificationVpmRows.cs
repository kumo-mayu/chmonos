using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>改変の詳細の「記録した時のVPM」の1行。</summary>
/// <param name="Text">「表示名 版」。</param>
/// <param name="NowText">今と違うときの添え書き（「今は 2.3.3」「今は入っていません」「記録の後に入りました」）。同じなら・今が分からなければ null。</param>
public sealed record RecordedVpmRow(string Text, string? NowText)
{
    public bool Differs => NowText is not null;
}

/// <summary>
/// 改変の詳細の「記録した時のVPM」の欄（ユーザ判断 2026-11-12）。出し分けを試験が直に見るため、ディスクを見ずに組む。
/// </summary>
/// <param name="IsVisible">欄を出すか。プロジェクトを紐付けておらず、控えも無い改変では出さない。</param>
/// <param name="SinceText">控えた写しの日（「2026-11-12から」）。見つけた記録から補ったとき・何も無いときは null。</param>
/// <param name="Caption">見つけた記録から補ったときの断り。控えた写しを出すときは null。</param>
/// <param name="EmptyText">出す行が無いときの文。</param>
public sealed record RecordedVpmView(bool IsVisible, string? SinceText, string? Caption, IReadOnlyList<RecordedVpmRow> Rows, string? EmptyText)
{
    public static RecordedVpmView Hidden { get; } = new(false, null, null, [], null);

    public bool HasSince => SinceText is not null;

    public bool HasCaption => Caption is not null;

    public bool HasRows => Rows.Count > 0;

    public bool HasEmptyText => EmptyText is not null;

    /// <summary>
    /// 控えた写し（<see cref="ModificationRecord.RecordedVpm"/>）があれば最後の写しを出す。前の写しは画面に出さない（API で読める）：
    /// 壊れた原因を探すのに要るのは、最後に記録した時と今の違いだから。
    ///
    /// 写しが無い改変（この欄を足す前に記録した物）は、プロジェクトの VPM の履歴から、最後に使ったものを足した時の姿を引いて、
    /// **控えには書かず**「見つけた記録から」と断って見せる（推定した値を控えに入れない決まり）。
    /// 履歴の日時は Chmonos が気付いた時で、版を入れ替えてから調べるまでの間に記録した物は前の版になり得るので、言い切らない。
    /// 引けなければ（履歴がその時より後から始まる）出さない：「入っていなかった」と「分からない」を見分けられない。
    /// </summary>
    /// <param name="now">今の VPM（調べた控えの <c>vpm</c>。改変の画面のプロジェクトの欄と同じ物）。null なら比べない。</param>
    /// <param name="history">そのプロジェクトの VPM の履歴。</param>
    public static RecordedVpmView Build(ModificationRecord record, IReadOnlyList<VpmPackage>? now, IReadOnlyList<VpmPackageHistory>? history)
    {
        var snapshots = record.RecordedVpm;
        if (snapshots.Count > 0)
        {
            var last = snapshots[^1];
            var rows = BuildRows(last.Items.Select(package => (package.Name, package.Shown, package.Version)), now);
            return new RecordedVpmView(true, $"{Day(last.At)}から", null, rows, rows.Count == 0 ? "記録した時は、VPMが入っていませんでした。" : null);
        }

        if (!record.HasUnityProject)
        {
            return Hidden;
        }

        if (history is { Count: > 0 } && record.UsedMembers.Count > 0)
        {
            var at = record.UsedMembers.Max(member => member.AddedAt);
            var versions = VpmHistories.VersionsAt(history, at);
            if (versions.Count > 0)
            {
                var shown = history.ToDictionary(entry => entry.Name, entry => entry.Shown, StringComparer.OrdinalIgnoreCase);
                var rows = BuildRows(versions.Select(pair => (pair.Key, shown.GetValueOrDefault(pair.Key) ?? pair.Key, pair.Value)), now);
                return new RecordedVpmView(true, null, $"Chmonosが見つけたVPMの記録から、{Day(at)}ごろの内容を表示しています。", rows, null);
            }
        }

        return new RecordedVpmView(true, null, null, [], "使ったものを記録すると、その時のVPMも残します。");
    }

    private static List<RecordedVpmRow> BuildRows(IEnumerable<(string Name, string Shown, string? Version)> recorded, IReadOnlyList<VpmPackage>? now)
    {
        var list = recorded.ToList();
        var current = new Dictionary<string, VpmPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in now ?? [])
        {
            current.TryAdd(package.Name, package);
        }

        var rows = list
            .OrderBy(package => package.Shown, StringComparer.OrdinalIgnoreCase)
            .Select(package => new RecordedVpmRow(Line(package.Shown, package.Version), now is null ? null : NowOf(package.Version, current.GetValueOrDefault(package.Name))))
            .ToList();

        // 記録の後に入れた物も、壊れた原因になり得るので並べる（今が分からなければ比べようが無いので出さない）
        if (now is not null)
        {
            var recordedNames = list.Select(package => package.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows.AddRange(now
                .Where(package => !recordedNames.Contains(package.Name))
                .OrderBy(package => package.Shown, StringComparer.OrdinalIgnoreCase)
                .Select(package => new RecordedVpmRow(Line(package.Shown, package.Version), "記録の後に入りました")));
        }

        return rows;
    }

    /// <summary>
    /// 今の様子。版は文字列の違いだけで見る（履歴と同じ。上下は解釈しない）。どちらかの版が読めないときは違うと言わない
    /// （読めない物を「変わった」と出すと、原因探しで見当違いの物を疑わせる）。
    /// </summary>
    private static string? NowOf(string? recorded, VpmPackage? current)
        => current switch
        {
            null => "今は入っていません",
            { Version: { } version } when recorded is not null && !string.Equals(version, recorded, StringComparison.Ordinal) => $"今は {version}",
            _ => null,
        };

    private static string Line(string shown, string? version) => version is null ? shown : $"{shown} {version}";

    /// <summary>控えた時の時刻帯のままの日付（VPM の履歴の画面と同じ）。</summary>
    private static string Day(DateTimeOffset time) => $"{time:yyyy-MM-dd}";
}
