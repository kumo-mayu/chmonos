using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// Unity エディタ・Hub・VCC の場所を、Windows と Hub の記録から読み解く（利用者の PC の置き場所に頼らない）。
/// </summary>
public sealed class UnityEditorLocatorTests
{
    /// <summary>Hub 3 の <c>editors-v2.json</c> の形。配列の下に並び、場所は配列。</summary>
    [Fact]
    public void Hubの新しい一覧から版と場所を取り出す()
    {
        const string json = """
            {
              "schema_version": "v2",
              "data": [
                {
                  "version": "2021.3.1f1",
                  "location": ["D:\\Unity\\2021.3.1f1\\Editor\\Unity.exe"],
                  "manual": true
                },
                {
                  "version": "2022.3.22f1",
                  "location": ["E:\\Apps\\Unity 2022.3.22f1\\Editor"],
                  "manual": true
                }
              ]
            }
            """;

        var editors = UnityEditorLocator.EditorsFromHubJson(json);

        Assert.Equal(
            [
                new UnityEditorEntry("2021.3.1f1", @"D:\Unity\2021.3.1f1\Editor\Unity.exe"),
                new UnityEditorEntry("2022.3.22f1", @"E:\Apps\Unity 2022.3.22f1\Editor\Unity.exe"),
            ],
            editors);
    }

    /// <summary>古い <c>editors.json</c> の形。版をキーにして並び、場所は文字列のこともある。</summary>
    [Fact]
    public void Hubの古い一覧も読める()
    {
        const string json = """
            {
              "2019.4.31f1": { "version": "2019.4.31f1", "location": "C:\\Unity\\2019.4.31f1\\Editor\\Unity.exe", "manual": true }
            }
            """;

        var editor = Assert.Single(UnityEditorLocator.EditorsFromHubJson(json));
        Assert.Equal(new UnityEditorEntry("2019.4.31f1", @"C:\Unity\2019.4.31f1\Editor\Unity.exe"), editor);
    }

    [Fact]
    public void 壊れた一覧は空で返す()
        => Assert.Empty(UnityEditorLocator.EditorsFromHubJson("{ not json"));

    /// <summary>Windows の登録の <c>Location x64</c> は版のフォルダを指す（手元の実物）。</summary>
    [Theory]
    [InlineData(@"C:\Program Files\Unity\Hub\Editor\2022.3.22f1", @"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")]
    [InlineData(@"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\", @"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")]
    [InlineData(@"D:\Unity\Editor", @"D:\Unity\Editor\Unity.exe")]
    [InlineData(@"D:\Unity\Editor\Unity.exe", @"D:\Unity\Editor\Unity.exe")]
    public void 場所をUnityexeにそろえる(string location, string expected)
        => Assert.Equal(expected, UnityEditorLocator.ExeFromLocation(location));

    /// <summary>関連付けのコマンドとアイコンの欄（どちらも手元の実物の形）。</summary>
    [Theory]
    [InlineData("\"C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.22f1\\Editor\\Unity.exe\" -openfile \"%1\"",
        @"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")]
    [InlineData(@"C:\Program Files\Unity Hub\Unity Hub.exe,0", @"C:\Program Files\Unity Hub\Unity Hub.exe")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Programs\\VRChat Creator Companion\\CreatorCompanion.exe\" \"%1\"",
        @"C:\Users\me\AppData\Local\Programs\VRChat Creator Companion\CreatorCompanion.exe")]
    [InlineData(@"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe", @"C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe")]
    public void コマンドから実行ファイルの場所を取り出す(string command, string expected)
        => Assert.Equal(expected, UnityEditorLocator.ExeFromCommand(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rundll32 shell32.dll")]
    public void 実行ファイルが無ければnull(string? command)
        => Assert.Null(UnityEditorLocator.ExeFromCommand(command));

    [Theory]
    [InlineData("Unity 2022.3.22f1", "2022.3.22f1")]
    [InlineData("Unity Hub", null)]
    [InlineData("Unity Hub 3.16.4", null)]
    [InlineData("VRChat Creator Companion version 2.4.5", null)]
    [InlineData(null, null)]
    public void アンインストール情報の名前から版を取り出す(string? name, string? expected)
        => Assert.Equal(expected, UnityEditorLocator.VersionFromUninstallName(name));

    /// <summary>実行ファイルの版の欄は後ろに識別の文字が付く（手元の実物は <c>2022.3.22f1_887be4894c44</c>）。</summary>
    [Theory]
    [InlineData("2022.3.22f1_887be4894c44", "2022.3.22f1", true)]
    [InlineData("2022.3.22f1", "2022.3.22f1", true)]
    [InlineData("2022.3.21f1_aaaa", "2022.3.22f1", false)]
    [InlineData(null, "2022.3.22f1", false)]
    public void 実行ファイルの版の欄で見分ける(string? productVersion, string version, bool expected)
        => Assert.Equal(expected, UnityEditorLocator.IsVersion(productVersion, version));
}
