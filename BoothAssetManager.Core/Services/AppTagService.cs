using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// マスタ1件の使用状況。件数を出すのは、消す前・改名する前に影響が見えるようにするため。
/// </summary>
public sealed record AppTagUsage
{
    public required string Top { get; init; }

    /// <summary>このトップを付けているitem数。</summary>
    public required int ItemCount { get; init; }

    /// <summary>サブレベル名 → それを付けているitem数。マスタに無いサブも含む。</summary>
    public required IReadOnlyDictionary<string, int> SubCounts { get; init; }
}

/// <summary>
/// マスタに無いのにitemが参照している名前。要確認にも出るが、直せるのはここだけ。
/// </summary>
public sealed record OrphanAppTag
{
    public required string Top { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>改名・削除の結果。書き換えたitem数を返すのは、実際に何が起きたかを見せるため。</summary>
public sealed record AppTagEditResult
{
    public required AppTagMaster Master { get; init; }

    public required int ItemsUpdated { get; init; }

    /// <summary>この操作でappTagが空になったitem数。編集の対象に戻るので黙って進めない。</summary>
    public int ItemsLeftUntagged { get; init; }

    /// <summary>既存の名前へ寄せた（統合した）かどうか。</summary>
    public bool WasMerged { get; init; }
}

public interface IAppTagService
{
    Task<IReadOnlyList<AppTagUsage>> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrphanAppTag>> LoadOrphansAsync(CancellationToken cancellationToken = default);

    Task<AppTagEditResult> RenameTopAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    Task<AppTagEditResult> RenameSubAsync(string top, string oldName, string newName, CancellationToken cancellationToken = default);

    Task<AppTagEditResult> DeleteTopAsync(string name, CancellationToken cancellationToken = default);

    Task<AppTagEditResult> DeleteSubAsync(string top, string name, CancellationToken cancellationToken = default);

    Task<AppTagMaster> SetMemoAsync(string top, string? sub, string? memo, CancellationToken cancellationToken = default);
}

/// <summary>
/// appTagマスタの改名・削除・メモ。
///
/// item側は名前で参照しているので、改名は全itemの一括書き換えを伴う
/// （読みやすさを優先した設計の代償。実測では1000件でも一瞬なので許容する）。
/// 同じ名前へ改名すると統合になる。統合も削除も戻せないので、
/// 何件が書き換わるかを先に数えられるようにしてある。
/// </summary>
public sealed class AppTagService : IAppTagService
{
    private readonly DataStore _store;

    public AppTagService(DataStore store)
    {
        _store = store;
    }

    /// <summary>マスタにある分だけを、マスタの並び順で返す。</summary>
    public async Task<IReadOnlyList<AppTagUsage>> LoadUsageAsync(CancellationToken cancellationToken = default)
    {
        var master = _store.AppTags.Load();
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return master.Tops.Select(top => Count(loaded.Items, top.Name)).ToList();
    }

    /// <summary>
    /// マスタから消えたか名前が変わった分類を、itemがまだ参照している状態を拾う。
    /// 要確認は知らせるだけなので、実際に直す場所としてここに出す。
    /// </summary>
    public async Task<IReadOnlyList<OrphanAppTag>> LoadOrphansAsync(CancellationToken cancellationToken = default)
    {
        var known = _store.AppTags.Load().Tops
            .Select(top => top.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .SelectMany(item => item.Local.AppTags.Select(assignment => assignment.Top))
            .Where(name => !known.Contains(name))
            .GroupBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new OrphanAppTag { Top = group.Key, ItemCount = group.Count() })
            .OrderByDescending(orphan => orphan.ItemCount)
            .ThenBy(orphan => orphan.Top, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// トップレベルを改名する。新しい名前が既にあれば統合になる
    /// （両方付いていたitemでは1件にまとめ、サブレベルは足し合わせる）。
    /// マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。
    /// </summary>
    public async Task<AppTagEditResult> RenameTopAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var target = newName.Trim();
        var master = _store.AppTags.Load();

        if (target.Length == 0 || Same(oldName, target))
        {
            return new AppTagEditResult { Master = master, ItemsUpdated = 0 };
        }

        var tops = master.Tops.ToList();
        var from = tops.FindIndex(top => Same(top.Name, oldName));
        var into = tops.FindIndex(top => Same(top.Name, target));
        var merged = into >= 0 && into != from;

        if (merged && from >= 0)
        {
            // サブレベルは寄せ先に足す。同じ名前のサブは寄せ先のメモを残す
            var subs = tops[into].Subs.ToList();
            foreach (var sub in tops[from].Subs.Where(sub => !subs.Any(entry => Same(entry.Name, sub.Name))))
            {
                subs.Add(sub);
            }

            tops[into] = Replace(tops[into], tops[into].Name, subs);
            tops.RemoveAt(from);
        }
        else if (from >= 0)
        {
            tops[from] = Replace(tops[from], target, tops[from].Subs);
        }

        var updated = new AppTagMaster { Tops = tops };
        await _store.AppTags.SaveAsync(updated, cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameTopIn(item, oldName, target),
            cancellationToken);

        return new AppTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
            WasMerged = merged,
        };
    }

    public async Task<AppTagEditResult> RenameSubAsync(
        string top,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var target = newName.Trim();
        var master = _store.AppTags.Load();

        if (target.Length == 0 || Same(oldName, target))
        {
            return new AppTagEditResult { Master = master, ItemsUpdated = 0 };
        }

        var tops = master.Tops.ToList();
        var index = tops.FindIndex(entry => Same(entry.Name, top));
        var merged = false;

        if (index >= 0)
        {
            var subs = tops[index].Subs.ToList();
            var from = subs.FindIndex(sub => Same(sub.Name, oldName));
            var into = subs.FindIndex(sub => Same(sub.Name, target));
            merged = into >= 0 && into != from;

            if (merged && from >= 0)
            {
                subs.RemoveAt(from);
            }
            else if (from >= 0)
            {
                subs[from] = new AppTagSub { Name = target, Memo = subs[from].Memo };
            }

            tops[index] = Replace(tops[index], tops[index].Name, subs);
        }

        var updated = new AppTagMaster { Tops = tops };
        await _store.AppTags.SaveAsync(updated, cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameSubIn(item, top, oldName, target),
            cancellationToken);

        return new AppTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            WasMerged = merged,
        };
    }

    /// <summary>
    /// トップレベルを消す。付けていたitemからも外す（サブレベルも一緒に外れる）。
    /// マスタから消すだけだと、item側が参照だけ残った壊れた状態になるため。
    /// </summary>
    public async Task<AppTagEditResult> DeleteTopAsync(string name, CancellationToken cancellationToken = default)
    {
        var master = _store.AppTags.Load();
        var updated = new AppTagMaster { Tops = master.Tops.Where(top => !Same(top.Name, name)).ToList() };
        await _store.AppTags.SaveAsync(updated, cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => item.AppTags.Any(assignment => Same(assignment.Top, name))
                ? item with { AppTags = item.AppTags.Where(assignment => !Same(assignment.Top, name)).ToList() }
                : null,
            cancellationToken);

        return new AppTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
        };
    }

    public async Task<AppTagEditResult> DeleteSubAsync(
        string top,
        string name,
        CancellationToken cancellationToken = default)
    {
        var master = _store.AppTags.Load();
        var tops = master.Tops.ToList();
        var index = tops.FindIndex(entry => Same(entry.Name, top));

        if (index >= 0)
        {
            tops[index] = Replace(
                tops[index],
                tops[index].Name,
                tops[index].Subs.Where(sub => !Same(sub.Name, name)).ToList());
        }

        var updated = new AppTagMaster { Tops = tops };
        await _store.AppTags.SaveAsync(updated, cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameSubIn(item, top, name, newName: null),
            cancellationToken);

        return new AppTagEditResult { Master = updated, ItemsUpdated = rewritten.Updated };
    }

    /// <summary>メモだけを書き換える。item側は名前しか参照していないので影響しない。</summary>
    public async Task<AppTagMaster> SetMemoAsync(
        string top,
        string? sub,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var master = _store.AppTags.Load();
        var tops = master.Tops.ToList();
        var index = tops.FindIndex(entry => Same(entry.Name, top));
        if (index < 0)
        {
            return master;
        }

        var trimmed = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();

        if (sub is null)
        {
            tops[index] = new AppTagTop { Name = tops[index].Name, Memo = trimmed, Subs = tops[index].Subs };
        }
        else
        {
            var subs = tops[index].Subs.ToList();
            var subIndex = subs.FindIndex(entry => Same(entry.Name, sub));
            if (subIndex < 0)
            {
                return master;
            }

            subs[subIndex] = new AppTagSub { Name = subs[subIndex].Name, Memo = trimmed };
            tops[index] = Replace(tops[index], tops[index].Name, subs);
        }

        var updated = new AppTagMaster { Tops = tops };
        await _store.AppTags.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private static AppTagUsage Count(IReadOnlyList<ItemRecord> items, string top)
    {
        var subCounts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var itemCount = 0;

        foreach (var assignment in items.SelectMany(item => item.Local.AppTags).Where(entry => Same(entry.Top, top)))
        {
            itemCount++;
            foreach (var sub in assignment.Subs)
            {
                subCounts[sub] = subCounts.GetValueOrDefault(sub) + 1;
            }
        }

        return new AppTagUsage { Top = top, ItemCount = itemCount, SubCounts = subCounts };
    }

    /// <summary>
    /// 全itemを見て、変換関数が新しい <c>local</c> を返したものだけ保存する。
    /// 1件ずつ書くので、途中で落ちてもそこまでは反映されている。
    /// </summary>
    private async Task<(int Updated, int LeftUntagged)> RewriteItemsAsync(
        Func<LocalBlock, LocalBlock?> transform,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var updated = 0;
        var leftUntagged = 0;

        foreach (var item in loaded.Items)
        {
            var local = transform(item.Local);
            if (local is null)
            {
                continue;
            }

            await _store.Items.SaveAsync(item with { Local = local }, cancellationToken);
            updated++;

            if (item.Local.AppTags.Count > 0 && local.AppTags.Count == 0)
            {
                leftUntagged++;
            }
        }

        return (updated, leftUntagged);
    }

    /// <summary>トップの改名。寄せ先が既に付いていればサブを足し合わせて1件にする。</summary>
    private static LocalBlock? RenameTopIn(LocalBlock local, string oldName, string newName)
    {
        if (!local.AppTags.Any(assignment => Same(assignment.Top, oldName)))
        {
            return null;
        }

        var result = new List<AppTagAssignment>();
        foreach (var assignment in local.AppTags)
        {
            var top = Same(assignment.Top, oldName) ? newName : assignment.Top;
            var existing = result.FindIndex(entry => Same(entry.Top, top));

            if (existing < 0)
            {
                result.Add(new AppTagAssignment { Top = top, Subs = assignment.Subs });
                continue;
            }

            var subs = result[existing].Subs.ToList();
            subs.AddRange(assignment.Subs.Where(sub => !subs.Any(entry => Same(entry, sub))));
            result[existing] = new AppTagAssignment { Top = result[existing].Top, Subs = subs };
        }

        return local with { AppTags = result };
    }

    /// <summary><paramref name="newName"/> が null なら削除。</summary>
    private static LocalBlock? RenameSubIn(LocalBlock local, string top, string oldName, string? newName)
    {
        var touched = false;
        var result = new List<AppTagAssignment>();

        foreach (var assignment in local.AppTags)
        {
            if (!Same(assignment.Top, top) || !assignment.Subs.Any(sub => Same(sub, oldName)))
            {
                result.Add(assignment);
                continue;
            }

            touched = true;
            var subs = new List<string>();
            foreach (var sub in assignment.Subs)
            {
                var value = Same(sub, oldName) ? newName : sub;
                if (value is not null && !subs.Any(entry => Same(entry, value)))
                {
                    subs.Add(value);
                }
            }

            result.Add(new AppTagAssignment { Top = assignment.Top, Subs = subs });
        }

        return touched ? local with { AppTags = result } : null;
    }

    private static AppTagTop Replace(AppTagTop top, string name, IReadOnlyList<AppTagSub> subs)
        => new() { Name = name, Memo = top.Memo, Subs = subs };

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
