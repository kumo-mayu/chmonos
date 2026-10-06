namespace Chmonos.Core.Services;

/// <summary>
/// 見分けに使う、Windows に残っている記録（読んだ物の写し）。読むのは App の InstalledApps で、ここは受け取って答えるだけ。
/// **分けてあるのは、本物の PC の記録の形を作り物で試験するため**（2026-10-07。何も入っていない PC・消した後の残りかすなど）
/// </summary>
/// <param name="HubLinkCommand"><c>unityhub://</c> の開くコマンド。無ければ null。</param>
/// <param name="VccLinkCommand"><c>vcc://</c> の開くコマンド（VCC か ALCOM が引き受ける）。無ければ null。</param>
/// <param name="LocalAppData">ALCOM の既定の入れ先を探す基。</param>
public sealed record InstalledAppRecords(
    IReadOnlyList<UninstallRecord> Uninstall,
    string? HubLinkCommand,
    string? VccLinkCommand,
    string? LocalAppData);

/// <summary>Unity Hub・VCC・ALCOM が手元にあるか。</summary>
public sealed record UnityToolPresence(bool HasHub, bool HasVcc, bool HasAlcom, bool LinkOpensAlcom)
{
    public const string HubExeName = "Unity Hub.exe";

    public const string HubProcessName = "Unity Hub";

    public const string VccProcessName = "CreatorCompanion";

    public const string AlcomProcessName = "ALCOM";

    /// <summary>
    /// Hub の候補。<c>unityhub://</c> の開くコマンド → アンインストール情報の場所 → アイコンの欄。
    /// （手元の Hub 3.16 は PC 全体に入り、場所の欄は空でアイコンの欄に実行ファイルがあった）
    /// </summary>
    public static IEnumerable<string> HubCandidates(InstalledAppRecords records)
    {
        if (UnityEditorLocator.ExeFromCommand(records.HubLinkCommand) is { } linked)
        {
            yield return linked;
        }

        foreach (var record in records.Uninstall.Where(record =>
            record.DisplayName.StartsWith("Unity Hub", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(record.InstallLocation))
            {
                yield return Path.Combine(record.InstallLocation, HubExeName);
            }

            if (UnityEditorLocator.ExeFromCommand(record.DisplayIcon) is { } icon)
            {
                yield return icon;
            }
        }
    }

    /// <summary>
    /// 記録から見分ける。**入っているとするのは、記録の指す実行ファイルが在るか、今動いているとき**。
    /// 前の Hub の見分けは記録があるだけで「入っている」とし、消した後に記録だけ残った PC では、
    /// 押せるのに開かないボタンになっていた（VCC・ALCOM はもとから実行ファイルを見ていた）
    /// </summary>
    public static UnityToolPresence Detect(
        InstalledAppRecords records, Func<string, bool> exists, Func<string, bool> isRunning)
    {
        var hasHub = ProjectManagerApps.FirstExisting(HubCandidates(records), exists) is not null
            || isRunning(HubProcessName);
        var hasVcc = ProjectManagerApps.FirstExisting(
                ProjectManagerApps.VccCandidates(records.Uninstall, records.VccLinkCommand), exists) is not null
            || isRunning(VccProcessName);
        var hasAlcom = ProjectManagerApps.FirstExisting(
                ProjectManagerApps.AlcomCandidates(records.Uninstall, records.LocalAppData, records.VccLinkCommand), exists) is not null
            || isRunning(AlcomProcessName);
        return new(hasHub, hasVcc, hasAlcom, ProjectManagerApps.LinkOpensAlcom(records.VccLinkCommand));
    }
}
