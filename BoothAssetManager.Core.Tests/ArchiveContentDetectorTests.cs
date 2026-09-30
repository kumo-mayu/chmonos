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

    // ---- 1回の処理の間だけ、フォルダの列挙を覚える（ArchiveContentDetector.Pass） ----

    /// <summary>
    /// 未確定の行を組むときの形：フォルダごとに1件ずつ見分ける。親の段は全部のフォルダで同じなので、
    /// 前はフォルダの数だけ列挙し直していた（803 フォルダで約 4,800 回）。同じフォルダは1回だけ列挙し、答えは1件ずつ見分けたときと同じ。
    /// </summary>
    [Fact]
    public void APassListsEachFolderOnceAndJudgesTheSame()
    {
        var folders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [@"D:\dl\in\loose\000"] = ["a.bin"],
            [@"D:\dl\in\loose\001"] = ["b.bin"],
            [@"D:\dl\in\loose\002"] = ["c.bin"],
            [@"D:\dl\in\loose"] = [],
            [@"D:\dl\in\pack\texture"] = ["t.png"],
            [@"D:\dl\in\pack"] = ["pack.unitypackage"],
            [@"D:\dl\in"] = ["x.zip"],
            [@"D:\dl"] = [],
        };
        var listed = new List<string>();
        IReadOnlyList<string> Counting(string directory)
        {
            listed.Add(directory);
            return folders.TryGetValue(directory, out var names) ? names : [];
        }

        string[] files =
        [
            @"D:\dl\in\loose\000\a.bin",
            @"D:\dl\in\loose\001\b.bin",
            @"D:\dl\in\loose\002\c.bin",
            @"D:\dl\in\pack\texture\t.png",
            @"D:\DL\IN\pack\pack.unitypackage",
        ];

        var pass = new ArchiveContentDetector.Pass(Counting);
        var together = files.Select(pass.Judge).ToList();
        var listedByPass = listed.ToList();

        listed.Clear();
        var oneByOne = files.Select(file => ArchiveContentDetector.Judge(file, Counting)).ToList();

        Assert.Equal(oneByOne, together);
        Assert.Equal([false, false, false, true, true], together.Select(judgement => judgement.IsContent));
        Assert.Equal(@"D:\dl\in\pack", together[3].ProductFolder);

        // 同じフォルダを2回列挙しない（大文字小文字だけ違う書き方も同じフォルダ）。1件ずつだと親の段を毎回列挙し直す
        Assert.Equal(listedByPass.Count, listedByPass.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(listedByPass.Count < listed.Count);
    }

    /// <summary>
    /// 覚えるのは1回の処理の間だけ。処理をまたいで持つと、フォルダの中身が変わった（展開した・目印を消した）のに古い答えを返す。
    /// 次の処理（新しく作った物）は今のフォルダを見る。
    /// </summary>
    [Fact]
    public void ANewPassSeesWhatChangedInTheFolder()
    {
        var folders = new Dictionary<string, string[]>
        {
            [@"D:\dl\p\texture"] = ["t.png"],
            [@"D:\dl\p"] = [],
        };
        const string file = @"D:\dl\p\texture\t.png";

        var first = new ArchiveContentDetector.Pass(Layout(folders));
        Assert.False(first.Judge(file).IsContent);

        folders[@"D:\dl\p"] = ["p.unitypackage"];

        Assert.False(first.Judge(file).IsContent);
        Assert.True(new ArchiveContentDetector.Pass(Layout(folders)).Judge(file).IsContent);
    }

    /// <summary>実のフォルダでも、1回の処理でまとめて見分けた答えは、1件ずつ見分けた答えと同じ。</summary>
    [Fact]
    public void APassJudgesRealFoldersTheSameAsOneByOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "bam-content-pass-" + Guid.NewGuid().ToString("N"));

        // 親は6段までたどる。置き場を5段掘っておけば、いちばん浅いファイルからでも一時フォルダより上へ出ない
        // （実マシンの一時フォルダに目印になるファイルがあっても、答えが変わらない）
        var inside = Path.Combine(root, "1", "2", "3", "4", "5");
        try
        {
            string Put(params string[] parts)
            {
                var path = Path.Combine([inside, .. parts]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "x");
                return path;
            }

            string[] files =
            [
                Put("loose", "000", "a.bin"),
                Put("loose", "001", "b.bin"),
                Put("outfit_v1", "outfit", "texture", "t.png"),
                Put("outfit_v1", "outfit", "outfit.unitypackage"),
                Put("linked", "psd", "body.psd"),
                Put("linked", "shop - BOOTH.url"),
                Put("plain.zip"),
            ];

            var pass = new ArchiveContentDetector.Pass();
            var together = files.Select(pass.Judge).ToList();

            Assert.Equal(files.Select(file => ArchiveContentDetector.Judge(file)), together);
            Assert.Equal([false, false, true, true, true, true, false], together.Select(judgement => judgement.IsContent));
            Assert.Equal(Path.Combine(inside, "outfit_v1", "outfit"), together[2].ProductFolder);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
