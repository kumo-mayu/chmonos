using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class ArchiveContentDetectorTests
{
    /// <summary>実際にファイルを置かず、フォルダごとの中身を差し替えて判定だけ試す。</summary>
    private static Func<string, IReadOnlyList<string>> Layout(Dictionary<string, string[]> folders)
        => directory => folders.TryGetValue(directory, out var names) ? names : [];

    /// <summary>
    /// 実測の形。rurune_v1.1.3 には対応するzipが無く展開先判定が効かないが、
    /// 中に unitypackage があるので展開物と分かる。
    /// </summary>
    [Fact]
    public void TreatsFilesUnderAUnityPackageFolderAsContent()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\rurune_v1.1.3\rurune\texture"] = ["hair.psd", "cloth.psd"],
            [@"D:\dl\rurune_v1.1.3\rurune"] = ["rurune.unitypackage", "rurune.blend"],
            [@"D:\dl\rurune_v1.1.3"] = [],
            [@"D:\dl"] = ["Kipfel_1.2.0.zip"],
        });

        var judgement = ArchiveContentDetector.Judge(@"D:\dl\rurune_v1.1.3\rurune\texture\hair.psd", layout);

        Assert.True(judgement.IsContent);
        Assert.Equal(@"D:\dl\rurune_v1.1.3\rurune", judgement.ProductFolder);
        Assert.Contains("rurune.unitypackage", judgement.Reason);
    }

    /// <summary>さらに深い階層でも親をたどって行き着く。</summary>
    [Fact]
    public void WalksUpThroughSeveralLevels()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\p\a\texture\mask"] = ["m.png"],
            [@"D:\dl\p\a"] = ["a.unitypackage"],
        });

        var judgement = ArchiveContentDetector.Judge(@"D:\dl\p\a\texture\mask\m.png", layout);

        Assert.True(judgement.IsContent);
        Assert.Equal(@"D:\dl\p\a", judgement.ProductFolder);
    }

    /// <summary>入れ子なら外側を根にする。配布の単位に近いのは外側のため。</summary>
    [Fact]
    public void PrefersTheOutermostMarkedFolder()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\outer\inner\texture"] = ["t.png"],
            [@"D:\dl\outer\inner"] = ["inner.unitypackage"],
            [@"D:\dl\outer"] = ["outer.unitypackage"],
        });

        var judgement = ArchiveContentDetector.Judge(@"D:\dl\outer\inner\texture\t.png", layout);

        Assert.Equal(@"D:\dl\outer", judgement.ProductFolder);
    }

    /// <summary>同梱のBOOTHリンクも、単体では出回らないので目印になる。</summary>
    [Fact]
    public void TreatsABundledBoothLinkAsAMarker()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\Milfy_v1.5.0\PSD"] = ["Body.psd"],
            [@"D:\dl\Milfy_v1.5.0"] = ["liltoon - lilLab - BOOTH.url"],
        });

        Assert.True(ArchiveContentDetector.Judge(@"D:\dl\Milfy_v1.5.0\PSD\Body.psd", layout).IsContent);
    }

    /// <summary>ダウンロードフォルダに直に置かれた配布物そのものは対象外。</summary>
    [Fact]
    public void DoesNotFlagAnArchiveSittingInTheDownloadFolder()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl"] = ["Kipfel_1.2.0.zip", "Milfy_v1.5.0.zip"],
        });

        Assert.False(ArchiveContentDetector.Judge(@"D:\dl\Kipfel_1.2.0.zip", layout).IsContent);
    }

    /// <summary>目印が無ければ判断しない。名前の見た目だけでは決めない。</summary>
    [Fact]
    public void DoesNotGuessFromFolderNamesAlone()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\texture"] = ["hair.psd"],
            [@"D:\dl"] = [],
        });

        Assert.False(ArchiveContentDetector.Judge(@"D:\dl\texture\hair.psd", layout).IsContent);
    }

    /// <summary>深すぎる場所からは遡らない。別の商品の木に踏み込む恐れがあるため。</summary>
    [Fact]
    public void StopsWalkingBeyondTheDepthLimit()
    {
        var layout = Layout(new Dictionary<string, string[]>
        {
            [@"D:\dl\p"] = ["p.unitypackage"],
        });

        var deep = @"D:\dl\p\a\b\c\d\e\f\g\h.png";

        Assert.False(ArchiveContentDetector.Judge(deep, layout).IsContent);
    }
}
