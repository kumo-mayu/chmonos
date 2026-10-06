using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// Hub・VCC・ALCOM の見分けを、作り物の記録で確かめる（2026-10-07。仮想の PC の代わり）。
/// 記録の形は、手元の PC の実物（Hub 3.16・VCC 2.4・ALCOM 1.1）に合わせてある。
/// 本物の PC で確かめきれないのは、版や入れ方で記録の形が違う場合と、入っていない PC でリンクを開いたときに Windows が何を出すか
/// </summary>
public sealed class UnityToolPresenceTests
{
    private const string HubExe = @"C:\Program Files\Unity Hub\Unity Hub.exe";
    private const string VccExe = @"C:\Users\someone\AppData\Local\Programs\VRChat Creator Companion\CreatorCompanion.exe";
    private const string AlcomExe = @"C:\Users\someone\AppData\Local\ALCOM\ALCOM.exe";
    private const string LocalAppData = @"C:\Users\someone\AppData\Local";

    private static readonly UninstallRecord HubRecord = new("Unity Hub", "Unity Hub 3.16.4", "Unity Technologies Inc.", null, HubExe + ",0");
    private static readonly UninstallRecord VccRecord = new(
        "VRChat Creator Companion", "VRChat Creator Companion", "VRChat", @"C:\Users\someone\AppData\Local\Programs\VRChat Creator Companion", null);
    private static readonly UninstallRecord AlcomRecord = new(ProjectManagerApps.AlcomUninstallKey, "ALCOM バージョン 1.1.8", "anatawa12", @"C:\Users\someone\AppData\Local\ALCOM", AlcomExe);

    private static UnityToolPresence Detect(
        InstalledAppRecords records, IEnumerable<string>? present = null, IEnumerable<string>? running = null)
    {
        var files = new HashSet<string>(present ?? [], StringComparer.OrdinalIgnoreCase);
        var processes = new HashSet<string>(running ?? [], StringComparer.OrdinalIgnoreCase);
        return UnityToolPresence.Detect(records, files.Contains, processes.Contains);
    }

    private static InstalledAppRecords Records(
        IReadOnlyList<UninstallRecord>? uninstall = null, string? hubLink = null, string? vccLink = null)
        => new(uninstall ?? [], hubLink, vccLink, LocalAppData);

    [Fact]
    public void 何も入っていないPCでは_どれも無い()
        => Assert.Equal(new UnityToolPresence(false, false, false, false), Detect(Records()));

    [Fact]
    public void Hubはリンクの登録からも_アンインストール情報のアイコンからも見つける()
    {
        Assert.True(Detect(Records(hubLink: $"\"{HubExe}\" \"%1\""), [HubExe]).HasHub);
        Assert.True(Detect(Records(uninstall: [HubRecord]), [HubExe]).HasHub);
    }

    [Fact]
    public void 消した後に記録だけ残っていても_実行ファイルが無ければ入っていないと見る()
    {
        var leftover = Records(uninstall: [HubRecord, VccRecord, AlcomRecord], hubLink: $"\"{HubExe}\" \"%1\"", vccLink: $"\"{VccExe}\" \"%1\"");

        var presence = Detect(leftover);

        Assert.False(presence.HasHub);
        Assert.False(presence.HasVcc);
        Assert.False(presence.HasAlcom);
    }

    [Fact]
    public void 実行ファイルが無くても_今動いていれば入っていると見る()
    {
        var presence = Detect(Records(), running: [UnityToolPresence.HubProcessName, UnityToolPresence.VccProcessName]);

        Assert.True(presence.HasHub);
        Assert.True(presence.HasVcc);
        Assert.False(presence.HasAlcom);
    }

    [Fact]
    public void ALCOMがvccのリンクを引き受けていれば_VCCとは数えずALCOMと数える()
    {
        var presence = Detect(Records(vccLink: $"\"{AlcomExe}\" \"%1\""), [AlcomExe]);

        Assert.False(presence.HasVcc);
        Assert.True(presence.HasAlcom);
        Assert.True(presence.LinkOpensAlcom);
    }

    [Fact]
    public void ALCOMは記録が無くても_既定の入れ先に在れば見つける()
        => Assert.True(Detect(Records(), [Path.Combine(LocalAppData, "Programs", "ALCOM", "ALCOM.exe")]).HasAlcom);

    [Fact]
    public void 全部入っていてvccのリンクがVCCのままなら_両方あり_リンクはVCC()
    {
        var all = Records(uninstall: [HubRecord, VccRecord, AlcomRecord], vccLink: $"\"{VccExe}\" \"%1\"");

        Assert.Equal(new UnityToolPresence(true, true, true, false), Detect(all, [HubExe, VccExe, AlcomExe]));
    }
}
