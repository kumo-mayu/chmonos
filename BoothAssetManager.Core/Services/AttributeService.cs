using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>属性1件の使用状況。平均も出すのは、値の入り方が偏っていないか見えるようにするため。</summary>
public sealed record AttributeUsage
{
    public required string Name { get; init; }

    /// <summary>評価が入っているitem数。未評価は数えない。</summary>
    public required int ItemCount { get; init; }

    /// <summary>入っている値の平均。1件も無ければ null。</summary>
    public double? Average { get; init; }
}

/// <summary>マスタに無いのにitemが参照している属性名。要確認にも出るが、直せるのはここだけ。</summary>
public sealed record OrphanAttribute
{
    public required string Name { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>統合したとき、両方に値が入っているitemでどちらを残すか。</summary>
public enum AttributeMergeValue
{
    /// <summary>寄せ先の値を残す。</summary>
    KeepTarget,

    /// <summary>寄せ元の値で上書きする。</summary>
    UseSource,
}

/// <summary>統合したら何が起きるかの下見。値がぶつかる件数が分からないと選びようがない。</summary>
public sealed record AttributeMergePreview
{
    /// <summary>寄せ元の評価が入っているitem数。</summary>
    public required int ItemCount { get; init; }

    /// <summary>両方に評価が入っていて、値が食い違うitem数。ここが判断の対象。</summary>
    public required int Conflicts { get; init; }
}

public sealed record AttributeEditResult
{
    public required AttributeMaster Master { get; init; }

    public required int ItemsUpdated { get; init; }

    public bool WasMerged { get; init; }
}

public interface IAttributeService
{
    Task<IReadOnlyList<AttributeUsage>> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrphanAttribute>> LoadOrphansAsync(CancellationToken cancellationToken = default);

    Task<AttributeMergePreview> PreviewMergeAsync(string from, string to, CancellationToken cancellationToken = default);

    Task<AttributeEditResult> RenameAsync(
        string oldName,
        string newName,
        AttributeMergeValue keep = AttributeMergeValue.KeepTarget,
        CancellationToken cancellationToken = default);

    Task<AttributeEditResult> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>編集画面で最初から並べる属性かを切り替える。item側には何も書かない</summary>
    Task<AttributeMaster> SetDefaultAsync(string name, bool isDefault, CancellationToken cancellationToken = default);

    Task<AttributeMaster> SetMemoAsync(string name, string? memo, CancellationToken cancellationToken = default);

    Task<AttributeMaster> ReorderAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
}

/// <summary>
/// 属性マスタ（<c>attributes.json</c>）の改名・統合・削除・メモ・並べ替え。
///
/// userTagと同じくitem側は名前で参照しているので、改名は全itemの一括書き換えを伴う。
/// 違いは、item側が名前だけでなく 0〜100 の値を持つこと。統合すると
/// 「両方に値が入っているitem」でどちらの値を残すかという問題が出るので、
/// アプリでは決めずに呼び出し側から受け取る。
/// </summary>
public sealed class AttributeService : IAttributeService
{
    private readonly DataStore _store;

    public AttributeService(DataStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<AttributeUsage>> LoadUsageAsync(CancellationToken cancellationToken = default)
    {
        var master = _store.Attributes.Load();
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return master.Attributes.Select(definition =>
        {
            var values = loaded.Items
                .Select(item => item.Local.Attributes.TryGetValue(definition.Name, out var value) ? value : (int?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            return new AttributeUsage
            {
                Name = definition.Name,
                ItemCount = values.Count,
                Average = values.Count == 0 ? null : values.Average(),
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<OrphanAttribute>> LoadOrphansAsync(CancellationToken cancellationToken = default)
    {
        var known = _store.Attributes.Load().Attributes
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .SelectMany(item => item.Local.Attributes.Keys)
            .Where(name => !known.Contains(name))
            .GroupBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new OrphanAttribute { Name = group.Key, ItemCount = group.Count() })
            .OrderByDescending(orphan => orphan.ItemCount)
            .ThenBy(orphan => orphan.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// 統合で値がぶつかるitem数を数える。同じ値なら選ぶ意味が無いので、
    /// 食い違うものだけを「衝突」として数える。
    /// </summary>
    public async Task<AttributeMergePreview> PreviewMergeAsync(
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        var count = 0;
        var conflicts = 0;

        foreach (var attributes in loaded.Items.Select(item => item.Local.Attributes))
        {
            if (!TryGet(attributes, from, out var source))
            {
                continue;
            }

            count++;

            if (TryGet(attributes, to, out var target) && target != source)
            {
                conflicts++;
            }
        }

        return new AttributeMergePreview { ItemCount = count, Conflicts = conflicts };
    }

    /// <summary>
    /// 改名する。新しい名前が既にあれば統合になる。
    /// 両方に値が入っているitemでは <paramref name="keep"/> の側を残す。
    /// マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。
    /// </summary>
    public async Task<AttributeEditResult> RenameAsync(
        string oldName,
        string newName,
        AttributeMergeValue keep = AttributeMergeValue.KeepTarget,
        CancellationToken cancellationToken = default)
    {
        var target = newName.Trim();

        if (target.Length == 0 || Same(oldName, target))
        {
            return new AttributeEditResult { Master = _store.Attributes.Load(), ItemsUpdated = 0 };
        }

        var merged = false;
        var updated = await ChangeMasterAsync(
            master =>
            {
                var definitions = master.Attributes.ToList();
                var from = definitions.FindIndex(entry => Same(entry.Name, oldName));
                var into = definitions.FindIndex(entry => Same(entry.Name, target));
                merged = into >= 0 && into != from;

                if (merged && from >= 0)
                {
                    definitions[into] = new AttributeDefinition
                    {
                        Name = definitions[into].Name,
                        Memo = MergeMemo(definitions[into].Memo, definitions[from].Name, definitions[from].Memo),

                        // 残る側の指定を引き継ぐ。組み直すたびに書き写さないと黙って落ちる
                        IsDefault = definitions[into].IsDefault,
                    };

                    definitions.RemoveAt(from);
                }
                else if (from >= 0)
                {
                    definitions[from] = new AttributeDefinition { Name = target, Memo = definitions[from].Memo, IsDefault = definitions[from].IsDefault };
                }

                return new AttributeMaster { Attributes = definitions };
            },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            attributes => RenameIn(attributes, oldName, target, keep),
            cancellationToken);

        return new AttributeEditResult { Master = updated, ItemsUpdated = rewritten, WasMerged = merged };
    }

    /// <summary>
    /// 消す。付けていたitemからも評価を外す。
    /// マスタから消すだけだと、item側が参照だけ残った壊れた状態になるため。
    /// </summary>
    public async Task<AttributeEditResult> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var updated = await ChangeMasterAsync(
            master => new AttributeMaster
            {
                Attributes = master.Attributes.Where(entry => !Same(entry.Name, name)).ToList(),
            },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            attributes => TryGet(attributes, name, out _)
                ? attributes.Where(entry => !Same(entry.Key, name)).ToDictionary(entry => entry.Key, entry => entry.Value)
                : null,
            cancellationToken);

        return new AttributeEditResult { Master = updated, ItemsUpdated = rewritten };
    }

    /// <summary>
    /// 編集画面で最初から並べる属性かを切り替える。
    ///
    /// **item側には何も書かない。**並べるだけで、値は触られたときにしか保存されない。
    /// 既に付いている商品に遡って何かすることもない。
    /// </summary>
    public async Task<AttributeMaster> SetDefaultAsync(
        string name,
        bool isDefault,
        CancellationToken cancellationToken = default)
    {
        return await ChangeMasterAsync(
            master =>
            {
                var definitions = master.Attributes.ToList();
                var index = definitions.FindIndex(entry => Same(entry.Name, name));

                if (index < 0 || definitions[index].IsDefault == isDefault)
                {
                    return null;
                }

                definitions[index] = new AttributeDefinition
                {
                    Name = definitions[index].Name,
                    Memo = definitions[index].Memo,
                    IsDefault = isDefault,
                };

                return new AttributeMaster { Attributes = definitions };
            },
            cancellationToken);
    }

    /// <summary>メモだけを書き換える。item側は名前しか参照していないので影響しない。</summary>
    public async Task<AttributeMaster> SetMemoAsync(
        string name,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        return await ChangeMasterAsync(
            master =>
            {
                var definitions = master.Attributes.ToList();
                var index = definitions.FindIndex(entry => Same(entry.Name, name));

                if (index < 0)
                {
                    return null;
                }

                definitions[index] = new AttributeDefinition
                {
                    Name = definitions[index].Name,
                    Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
                    IsDefault = definitions[index].IsDefault,
                };

                return new AttributeMaster { Attributes = definitions };
            },
            cancellationToken);
    }

    /// <summary>
    /// 並べ替える。並びは検索の候補にも編集の候補にも出るので、追加順に縛られないようにする。
    /// 指定に無かった名前は末尾に残す（黙って消えるより気付ける）。
    /// </summary>
    public async Task<AttributeMaster> ReorderAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        return await ChangeMasterAsync(
            master =>
            {
                var remaining = master.Attributes.ToList();
                var sorted = new List<AttributeDefinition>(remaining.Count);

                foreach (var name in names)
                {
                    var index = remaining.FindIndex(entry => Same(entry.Name, name));
                    if (index >= 0)
                    {
                        sorted.Add(remaining[index]);
                        remaining.RemoveAt(index);
                    }
                }

                sorted.AddRange(remaining);
                return new AttributeMaster { Attributes = sorted };
            },
            cancellationToken);
    }

    /// <summary>
    /// マスタを読み直してから書き換える。<c>attributes.json</c> は書き手が2つ（管理画面・編集画面）あるので、
    /// **読んでから書くまでを錠の中に入れる**（`docs/spec/data-model.md`）。
    /// <paramref name="change"/> が null を返したら書かない。
    /// </summary>
    private async Task<AttributeMaster> ChangeMasterAsync(
        Func<AttributeMaster, AttributeMaster?> change,
        CancellationToken cancellationToken)
    {
        var written = new AttributeMaster();
        await _store.Attributes.TryUpdateAsync(
            master =>
            {
                var updated = change(master);
                written = updated ?? master;
                return updated;
            },
            cancellationToken);

        return written;
    }

    /// <summary>
    /// 全itemを見て、変換関数が新しい辞書を返したものだけ保存する。
    /// 1件ずつ書くので、途中で落ちてもそこまでは反映されている。
    /// </summary>
    private async Task<int> RewriteItemsAsync(
        Func<IReadOnlyDictionary<string, int>, Dictionary<string, int>?> transform,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var updated = 0;

        foreach (var item in loaded.Items)
        {
            var attributes = transform(item.Local.Attributes);
            if (attributes is null)
            {
                continue;
            }

            // 全件を先に読んでから順に書く。書く頃には写しが古いので、属性だけを名指しする
            await _store.Items.SaveLocalAsync(
                item.Id,
                item.Local with { Attributes = attributes },
                LocalOwners.Attributes,
                cancellationToken: cancellationToken);

            updated++;
        }

        return updated;
    }

    /// <summary>
    /// 名前を付け替える。寄せ先に既に値があれば、どちらを残すかは呼び出し側の指定に従う。
    /// 並び順は元の位置を保つ（辞書の順序はJSONにそのまま出るので、勝手に変えない）。
    /// </summary>
    private static Dictionary<string, int>? RenameIn(
        IReadOnlyDictionary<string, int> attributes,
        string oldName,
        string newName,
        AttributeMergeValue keep)
    {
        if (!TryGet(attributes, oldName, out var value))
        {
            return null;
        }

        var result = new Dictionary<string, int>();

        foreach (var entry in attributes)
        {
            if (Same(entry.Key, oldName))
            {
                // 寄せ先が後ろにあるなら、そちらで受ける
                if (attributes.Any(other => !Same(other.Key, oldName) && Same(other.Key, newName)))
                {
                    continue;
                }

                result[newName] = value;
                continue;
            }

            if (Same(entry.Key, newName))
            {
                result[entry.Key] = keep == AttributeMergeValue.UseSource ? value : entry.Value;
                continue;
            }

            result[entry.Key] = entry.Value;
        }

        return result;
    }

    /// <summary>
    /// 統合するとき、寄せ元のメモを寄せ先へ書き足す。
    /// メモは付け方の基準なので、統合で片方が黙って消えると尺度がぶれる。
    /// </summary>
    private static string? MergeMemo(string? into, string fromName, string? from)
    {
        var source = from?.Trim();
        if (string.IsNullOrEmpty(source))
        {
            return into;
        }

        var target = into?.Trim();
        var added = $"「{fromName}」から統合：{source}";

        return string.IsNullOrEmpty(target)
            ? added
            : target.Contains(source, StringComparison.CurrentCulture)
                ? target
                : $"{target}{Environment.NewLine}{Environment.NewLine}{added}";
    }

    private static bool TryGet(IReadOnlyDictionary<string, int> attributes, string name, out int value)
    {
        foreach (var entry in attributes)
        {
            if (Same(entry.Key, name))
            {
                value = entry.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
