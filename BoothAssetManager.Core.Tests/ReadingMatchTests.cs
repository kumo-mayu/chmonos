using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ファイル名と商品名を読みで突き合わせる段。
/// ラテン文字のファイル名は日本語商品のローマ字表記であることが多い。
/// 通信は増えない——商品名はもう取ってあるものを使う。
/// </summary>
public class ReadingMatchTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _cacheDir;
    private readonly SearchBridge _bridge;
    private readonly KanjiReadings _readings;

    public ReadingMatchTests(ITestOutputHelper output)
    {
        _output = output;
        _cacheDir = Path.Combine(Path.GetTempPath(), "bam-rm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        _bridge = new SearchBridge(new JapaneseDictionary(
            Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz"),
            Path.Combine(_cacheDir, "c.cache")));
        _readings = new KanjiReadings(Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool Available => _bridge.IsAvailable && _readings.IsAvailable;

    private string? Find(string query, string itemName)
    {
        var hit = ReadingMatch.Find(query, itemName, _bridge, _readings);
        _output.WriteLine($"{query,-16} × {itemName,-34} → {hit ?? "(なし)"}");
        return hit;
    }

    /// <summary>ユーザが最初に挙げた例。tori のファイルが『Bird/鳥』に当たること。</summary>
    [Fact]
    public void MatchesRomajiFileNameToKanjiProductName()
    {
        if (!Available)
        {
            return;
        }

        Assert.NotNull(Find("tori", "オリジナル3Dモデル『Bird/鳥』"));
        Assert.NotNull(Find("yubiwa model", "【VRChat想定】指輪モデル_Ⅶ"));
        Assert.NotNull(Find("same chan", "サメっ子オリジナル3Dモデル「rurune」-ルルネ-"));
    }

    /// <summary>辞書に無い造語は、商品名の読みを組み立てて突き合わせる。</summary>
    [Fact]
    public void MatchesCompoundsThroughGeneratedReadings()
    {
        if (!Available)
        {
            return;
        }

        Assert.NotNull(Find("nadeoto", "【フカさんの！】撫で音ギミック【VRChat】【MA式】"));
        Assert.NotNull(Find("Sin Avatar Pen", "真・アバターペンシステム Sin Avatar Pen System"));
    }

    /// <summary>
    /// 英訳の側は見ない。作者は bird_v1.zip とは名付けないので、
    /// 見ても誤った一致（Sin→罪、Ring→土俵）を作るだけになる。
    /// </summary>
    [Fact]
    public void DoesNotMatchThroughEnglishMeaning()
    {
        if (!Available)
        {
            return;
        }

        Assert.Null(Find("Sin Avatar Pen", "罪と罰のゲーム"));
        Assert.Null(Find("shark", "サメっ子オリジナル3Dモデル"));
    }

    /// <summary>無関係な商品には当たらない。</summary>
    [Fact]
    public void DoesNotMatchUnrelatedProducts()
    {
        if (!Available)
        {
            return;
        }

        Assert.Null(Find("tori", "【くうた対応】School sweater #Kuuta3D"));
        Assert.Null(Find("Kuuta ShapekeyAddon", "オリジナル3Dモデル『Bird/鳥』"));
    }

    /// <summary>1文字の語では照合しない。無関係な商品に当たってしまう。</summary>
    [Fact]
    public void IgnoresVeryShortTokens()
    {
        if (!Available)
        {
            return;
        }

        Assert.Null(Find("a", "オリジナル3Dモデル『Bird/鳥』"));
    }
}
