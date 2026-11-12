using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変の画面のプロジェクトの右の欄の、VPM 1つの行（ユーザ判断 2026-11-12）。
/// 今の版と「{日付}から」を出し、前の状態があれば三角で広げる。外したものは同じ形で薄く出す。
/// </summary>
public sealed class HubVpmRow : ViewModelBase
{
    /// <summary>「表示名 版」（添え書きを含む）。</summary>
    public required string Text { get; init; }

    /// <summary>「2026-11-11から」「2026-11-12に外した」。履歴の無い前の控えでは null。</summary>
    public string? SinceText { get; init; }

    public bool HasSince => SinceText is not null;

    /// <summary>前の状態を新しい順に（「2.3.2　2026-10-01〜2026-11-11」「外していた　…」）。</summary>
    public required IReadOnlyList<string> PastLines { get; init; }

    public bool HasPast => PastLines.Count > 0;

    public bool IsRemoved { get; init; }

    /// <summary>
    /// 同じ並びのどれかが三角を持つか。持つ並びでは、三角の無い行も三角の幅だけ字下げして名前の頭を揃える
    /// （揃えないと、名前の頭が行ごとに前後して見比べにくい）。どれも持たなければ字下げしない。
    /// </summary>
    public bool AlignWithToggle { get; init; }

    /// <summary>三角の読み上げの名前（「（名前）の前のバージョンを開く」になる）。</summary>
    public required string ExpandName { get; init; }

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }
}

/// <summary>右の欄の VPM の行を組む（試験が直に呼ぶ）。</summary>
internal sealed record HubVpmRows(IReadOnlyList<HubVpmRow> Current, IReadOnlyList<HubVpmRow> Removed)
{
    /// <summary>
    /// 今の行は控えの <c>vpm</c>（今の姿）から作り、日付と前の状態を履歴から引く。外したものは履歴の最後の行が「外した」物。
    /// 履歴が無い（前の控え）なら日付を出さず、外したものも出さない。
    /// </summary>
    public static HubVpmRows Build(IReadOnlyList<VpmPackage>? current, IReadOnlyList<VpmPackageHistory>? history)
    {
        var byName = new Dictionary<string, VpmPackageHistory>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in history ?? [])
        {
            byName.TryAdd(entry.Name, entry);
        }

        var currentRows = (current ?? [])
            .Select(package => (Text: HubProjectDetail.VpmLine(package), Shown: package.Shown, History: byName.GetValueOrDefault(package.Name)))
            .ToList();
        var shownNow = (current ?? []).Select(package => package.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedRows = (history ?? [])
            .Where(entry => entry.Current.Removed && !shownNow.Contains(entry.Name))
            .OrderBy(entry => entry.Shown, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (Text: LastVersion(entry) is { } version ? $"{entry.Shown} {version}" : entry.Shown, Shown: entry.Shown, History: (VpmPackageHistory?)entry))
            .ToList();

        return new HubVpmRows(Rows(currentRows, removed: false), Rows(removedRows, removed: true));
    }

    private static List<HubVpmRow> Rows(List<(string Text, string Shown, VpmPackageHistory? History)> rows, bool removed)
    {
        var align = rows.Any(row => row.History is { Changes.Count: > 1 });
        return rows.Select(row => new HubVpmRow
        {
            Text = row.Text,
            SinceText = row.History is { } history ? Since(history) : null,
            PastLines = row.History is { } past ? PastLines(past) : [],
            IsRemoved = removed,
            AlignWithToggle = align,
            ExpandName = $"{row.Shown}の前のバージョン",
        }).ToList();
    }

    /// <summary>今の状態に気付いた日。外した物は「に外した」、入っている物は「から」。</summary>
    private static string Since(VpmPackageHistory history)
        => history.Current.Removed ? $"{Day(history.Current.From)}に外した" : $"{Day(history.Current.From)}から";

    /// <summary>今より前の状態を新しい順に。終わりの日は次の行の日（控えに持たない）。</summary>
    internal static IReadOnlyList<string> PastLines(VpmPackageHistory history)
    {
        var lines = new List<string>();
        for (var i = history.Changes.Count - 2; i >= 0; i--)
        {
            var entry = history.Changes[i];
            var what = entry switch
            {
                { Removed: true } => "外していた",
                { Version: { } version } => version,
                _ => "バージョンが読めません",
            };
            lines.Add($"{what}　{Day(entry.From)}〜{Day(history.Changes[i + 1].From)}");
        }

        return lines;
    }

    /// <summary>外した物に添える、最後に入っていた版。</summary>
    private static string? LastVersion(VpmPackageHistory history)
        => history.Changes.LastOrDefault(entry => !entry.Removed)?.Version;

    /// <summary>控えた時の時刻帯のままの日付（見つけた時にその PC で何日だったか。今の時刻帯へ直すと、旅先などで日がずれて見える）。</summary>
    private static string Day(DateTimeOffset time) => $"{time:yyyy-MM-dd}";
}
