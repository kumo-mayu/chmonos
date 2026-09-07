using BoothZipInspector.Models;

namespace BoothZipInspector;

/// <summary>
/// 複数の発見元から集めたBoothClueを、URLまたは商品IDで重複除去しつつ蓄積する。
/// 最初に見つかった発見元パスを保持する。
/// </summary>
public sealed class BoothClueCollector
{
    private readonly Dictionary<string, BoothClue> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public void Add(BoothClue clue)
    {
        var key = clue.ItemId ?? clue.Url;
        if (!_byKey.ContainsKey(key))
        {
            _byKey[key] = clue;
        }
    }

    public void AddRange(IEnumerable<BoothClue> clues)
    {
        foreach (var clue in clues)
        {
            Add(clue);
        }
    }

    public IReadOnlyList<BoothClue> Clues => _byKey.Values.ToList();
}
