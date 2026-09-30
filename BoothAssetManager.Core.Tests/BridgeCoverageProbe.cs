using BoothAssetManager.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 手元の商品名に出てくる語を、今の橋渡しがどれだけ拾えるかを見る覚え書き。
/// 形態素解析を足すかどうかの判断材料。
/// </summary>
public class BridgeCoverageProbe
{
    private readonly ITestOutputHelper _output;

    public BridgeCoverageProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ReportsWhichProductWordsAreReachable()
    {
        if (!SharedDictionaries.JapaneseAvailable)
        {
            return;
        }

        var dictionary = SharedDictionaries.Japanese;

        // 手元の商品名に実際に出てくる語と、その読み
        var words = new (string Word, string Reading)[]
        {
            ("想定", "そうてい"), ("指輪", "ゆびわ"), ("対応", "たいおう"), ("心音", "しんおん"),
            ("心拍計", "しんぱくけい"), ("撫で音", "なでおと"), ("鳥", "とり"), ("少年", "しょうねん"),
            ("表情", "ひょうじょう"), ("追加", "ついか"), ("変形", "へんけい"), ("真", "しん"),
            ("アバターペン", "あばたーぺん"), ("タマクラゲ", "たまくらげ"),
        };

        var lines = new List<string>();
        var reachable = 0;
        foreach (var (word, reading) in words)
        {
            var forms = dictionary.ByReading(reading);
            var hit = forms.Contains(word);
            if (hit)
            {
                reachable++;
            }

            lines.Add($"{word,-8} 読み{reading,-12} {(hit ? "○" : "×")} {string.Join(" ", forms.Take(4))}");
        }

        _output.WriteLine(string.Join("\n", lines));
        _output.WriteLine($"読みから引けた語: {reachable}/{words.Length}");
    }
}
