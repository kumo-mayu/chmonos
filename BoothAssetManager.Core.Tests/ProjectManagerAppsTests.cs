using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// VCC・ALCOM の実行ファイルの候補の並びと、ボタンの出し分け。
/// この PC のレジストリ・プロセスに左右されないよう、記録は作り物を渡し、在るかは関数で差し替える。
/// </summary>
public class ProjectManagerAppsTests
{
    private const string LocalAppData = @"C:\Users\me\AppData\Local";

    private static UninstallRecord Alcom(
        string displayName = "ALCOM バージョン 1.1.8",
        string? location = @"D:\Tools\ALCOM\",
        string? icon = @"D:\Tools\ALCOM\ALCOM.exe")
        => new(ProjectManagerApps.AlcomUninstallKey, displayName, "anatawa12", location, icon);

    private static UninstallRecord Vcc(string? location = @"C:\Users\me\AppData\Local\Programs\VRChat Creator Companion\")
        => new("{A20FE4C3-0000-0000-0000-000000000000}_is1", "VRChat Creator Companion", "VRChat", location, "");

    [Fact]
    public void ALCOMはアンインストール情報の場所を既定の入れ先より先に見る()
    {
        var candidates = ProjectManagerApps.AlcomCandidates([Alcom()], LocalAppData, null).ToList();

        Assert.Equal(@"D:\Tools\ALCOM\ALCOM.exe", candidates[0]);
        Assert.Equal(
            [
                @"D:\Tools\ALCOM\ALCOM.exe",
                @"D:\Tools\ALCOM\ALCOM.exe",
                @"C:\Users\me\AppData\Local\Programs\ALCOM\ALCOM.exe",
                @"C:\Users\me\AppData\Local\ALCOM\ALCOM.exe",
            ],
            candidates);
    }

    [Theory]
    [InlineData("ALCOM version 1.1.8")]
    [InlineData("ALCOM バージョン 1.1.8")]
    [InlineData("")]
    public void ALCOMは表示名が何語でも鍵の名前で見分ける(string displayName)
    {
        var found = ProjectManagerApps.FirstExisting(
            ProjectManagerApps.AlcomCandidates([Alcom(displayName)], LocalAppData, null),
            path => path == @"D:\Tools\ALCOM\ALCOM.exe");

        Assert.Equal(@"D:\Tools\ALCOM\ALCOM.exe", found);
    }

    [Fact]
    public void 表示名にALCOMとあっても鍵と作者が違えば候補にしない()
    {
        var other = new UninstallRecord("SomethingElse", "ALCOM", "someone", @"E:\Fake\", @"E:\Fake\ALCOM.exe");

        var candidates = ProjectManagerApps.AlcomCandidates([other], null, null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void 鍵が違っても作者がanatawa12ならALCOMとみる_ただし名前がALCOM_exeのアイコンだけ()
    {
        var nsis = new UninstallRecord("ALCOM", "ALCOM", " anatawa12 ", @"E:\Old\", @"E:\Old\ALCOM.exe");
        var sibling = new UninstallRecord("OtherTool", "別のツール", "anatawa12", null, @"E:\Other\OtherTool.exe");

        var candidates = ProjectManagerApps.AlcomCandidates([sibling, nsis], null, null).ToList();

        Assert.Equal([@"E:\Old\ALCOM.exe", @"E:\Old\ALCOM.exe"], candidates);
    }

    [Fact]
    public void 鍵の合う記録を作者だけ合う記録より先に見る()
    {
        var byPublisher = new UninstallRecord("ALCOM", "ALCOM", "anatawa12", @"E:\Old\", null);

        var candidates = ProjectManagerApps.AlcomCandidates([byPublisher, Alcom(icon: null)], null, null).ToList();

        Assert.Equal([@"D:\Tools\ALCOM\ALCOM.exe", @"E:\Old\ALCOM.exe"], candidates);
    }

    [Fact]
    public void 記録が無ければ既定の入れ先_古い入れ先の順に見る()
    {
        var found = ProjectManagerApps.FirstExisting(
            ProjectManagerApps.AlcomCandidates([], LocalAppData, null),
            path => path.EndsWith(@"Local\ALCOM\ALCOM.exe", StringComparison.Ordinal));

        Assert.Equal(@"C:\Users\me\AppData\Local\ALCOM\ALCOM.exe", found);
    }

    [Fact]
    public void vccリンクをALCOMが引き受けていればALCOMの候補にし_VCCの候補にはしない()
    {
        const string link = "\"E:\\Scoop\\apps\\vrc-alcom\\current\\ALCOM.exe\" link \"%1\"";

        Assert.Equal(
            @"E:\Scoop\apps\vrc-alcom\current\ALCOM.exe",
            ProjectManagerApps.AlcomCandidates([], null, link).Single());
        Assert.Empty(ProjectManagerApps.VccCandidates([], link));
    }

    [Fact]
    public void vccリンクがVCCならVCCの候補の最後に入り_ALCOMの候補にはしない()
    {
        const string link = "\"C:\\VCC\\CreatorCompanion.exe\" \"%1\"";

        Assert.Equal(
            [@"C:\Users\me\AppData\Local\Programs\VRChat Creator Companion\CreatorCompanion.exe", @"C:\VCC\CreatorCompanion.exe"],
            ProjectManagerApps.VccCandidates([Vcc(), Alcom()], link).ToList());
        Assert.DoesNotContain(@"C:\VCC\CreatorCompanion.exe", ProjectManagerApps.AlcomCandidates([], null, link));
    }

    [Fact]
    public void VCCの候補にALCOMの記録は入らない()
    {
        Assert.Empty(ProjectManagerApps.VccCandidates([Alcom()], null));
    }

    [Fact]
    public void 在る物が無ければnull()
    {
        Assert.Null(ProjectManagerApps.FirstExisting(
            ProjectManagerApps.AlcomCandidates([Alcom()], LocalAppData, null), _ => false));
    }

    [Theory]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, false, true, false, false)]
    public void ボタンの出し分け(bool hasVcc, bool hasAlcom, bool showVcc, bool canOpenVcc, bool showAlcom)
    {
        var buttons = ProjectManagerApps.Buttons(hasVcc, hasAlcom);

        Assert.Equal(new ProjectManagerButtons(showVcc, canOpenVcc, showAlcom), buttons);
        Assert.Equal(hasVcc && hasAlcom, buttons.Both);
    }
}
