using Chmonos.Core.Resolution;
using Chmonos.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace Chmonos.Core.Tests;

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

        // 作り物のファイル名（英語の語・ローマ字・日本語・版の数字が混ざる形）。購入した物や友人のデータの名前は書かない
        var fileNames = new[]
        {
            "Hoshizora_Dress_v1.2.zip",
            "kumoribi_hair_ver2.zip",
            "NightCafe_Interior_1.0.zip",
            "ゆきあかりワンピース.zip",
            "soft_shadow_shader.zip",
            "Tsukimi_Accessory_Set.zip",
            "冬のコートセット_v3.zip",
            "MochiMochi_Texture_Pack.zip",
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
