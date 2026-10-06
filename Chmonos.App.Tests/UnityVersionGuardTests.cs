using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// Unity の版（ProjectVersion.txt・一覧）から、指定外の実行ファイルを選ばない（2026-10-06 外部の点検・L106）。
/// 版はエディタの場所（Hub の置き場所\{版}\Editor\Unity.exe）と Hub へのリンクになる。
/// 置き場所は作り物のフォルダを渡すので、この PC に入っている Unity には左右されない。
/// </summary>
public class UnityVersionGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "bam-unity-root-" + Guid.NewGuid().ToString("N"), "Editor");

    public static TheoryData<string> BadVersions() => new()
    {
        @"..\..\..\Windows\System32",
        @"C:\Windows\System32",
        @"2022.3.22f1\..\..\x",
        "2022.3.22f1/../x",
        @"\\server\share",
        "2022.3.22f1 -batchmode",
        "2022.3.22f1&calc",
        "notepad.exe",
        "..",
        "",
    };

    [Theory]
    [MemberData(nameof(BadVersions))]
    public void 形の外れた版からはエディタの場所を組まない(string version)
    {
        Assert.Empty(UnityLaunch.HubEditorCandidates([Root], version));
        Assert.Empty(UnityLaunch.EditorCandidates(version));
        Assert.Null(UnityLaunch.FindEditor(version));
    }

    [Theory]
    [MemberData(nameof(BadVersions))]
    public void 形の外れた版はHubへのリンクに添えない(string version)
        => Assert.Equal("unityhub://", UnityLaunch.HubLink(version));

    [Theory]
    [MemberData(nameof(BadVersions))]
    public void 形の外れた版はProjectVersionから読まない(string version)
        => Assert.Null(UnityProjects.VersionFromProjectVersionText($"m_EditorVersion: {version}\nm_EditorVersionWithRevision: x\n"));

    [Theory]
    [InlineData("2022.3.22f1")]
    [InlineData("6000.0.23f1")]
    [InlineData("2022.3.22f1c1")]
    [InlineData("5.6.7f1")]
    public void 版の形なら置き場所の中に組む(string version)
    {
        var exe = Assert.Single(UnityLaunch.HubEditorCandidates([Root], version));
        Assert.Equal(Path.Combine(Root, version, "Editor", "Unity.exe"), exe);
        Assert.Equal($"unityhub://{version}", UnityLaunch.HubLink(version));
        Assert.Equal(version, UnityProjects.VersionFromProjectVersionText($"m_EditorVersion: {version}\n"));
    }
}
