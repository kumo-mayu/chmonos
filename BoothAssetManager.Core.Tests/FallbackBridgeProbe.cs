using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの候補検索（フォールバック）に、表記をまたぐ仕組みが効くかを見る覚え書き。
/// 通信はしない——ファイル名から作る検索語が、別表記でどう変わるかだけを見る。
/// </summary>
public class FallbackBridgeProbe
{
    private readonly ITestOutputHelper _output;

    public FallbackBridgeProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ReportsWhatTheBridgeWouldAddToFileNameQueries()
    {
        if (!SharedDictionaries.JapaneseAvailable)
        {
            return;
        }

        var bridge = SharedDictionaries.Bridge;

        // 手元の実ファイル名（DLforTest）と、日本語の商品を指す名前
        var fileNames = new[]
        {
            "Bracelet_tamakurage.v1.01.zip",
            "HeartBeatGimmick_v3.0.3.zip",
            "Sig_Ring_07_ver2.zip",
            "SinAvatarPen_v1.2.2.zip",
            "Kuuta_ShapekeyAddon.zip",
            "tori_v1.zip",
            "yubiwa_model.zip",
            "same_chan.zip",
        };

        foreach (var fileName in fileNames)
        {
            var query = FileNameQuery.ToSearchQuery(fileName);
            var all = query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(token => bridge.Expand(token))
                .ToList();

            string Show(params BridgeRoute[] routes) =>
                string.Join(" ", all.Where(c => routes.Contains(c.Via)).Select(c => c.Text).Distinct(StringComparer.Ordinal).Take(4));

            _output.WriteLine($"{fileName,-34} 「{query}」");
            _output.WriteLine($"    読みの経路 : {Show(BridgeRoute.Reading, BridgeRoute.Katakana, BridgeRoute.Dictionary)}");
            _output.WriteLine($"    英語の経路 : {Show(BridgeRoute.English)}");
        }
    }
}
