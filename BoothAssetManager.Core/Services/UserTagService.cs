using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// マスタ1件の使用状況。件数を出すのは、消す前・改名する前に影響が見えるようにするため。
/// </summary>
public sealed record UserTagUsage
{
    public required string Top { get; init; }

    /// <summary>このトップを付けているitem数。</summary>
    public required int ItemCount { get; init; }

    /// <summary>サブレベル名 → それを付けているitem数。マスタに無いサブも含む。</summary>
    public required IReadOnlyDictionary<string, int> SubCounts { get; init; }
}

/// <summary>
/// マスタに無いのにitemが参照している名前。要確認にも出るが、直せるのはここだけ。
///
/// <see cref="Sub"/> が null ならトップレベル、入っていればその配下のサブ。
/// サブも拾うのは、集計はitem側の全サブを数えているのに画面はマスタにある分しか
/// 出さないため、ずれたサブ名がどこにも表示されないまま絞り込みから消えるため。
/// </summary>
public sealed record OrphanUserTag
{
    public required string Top { get; init; }

    public string? Sub { get; init; }

    public required int ItemCount { get; init; }

    /// <summary>直す対象の名前。トップならトップ名、サブならサブ名。</summary>
    public string Name => Sub ?? Top;

    public bool IsSub => Sub is not null;
}

/// <summary>改名・削除の結果。書き換えたitem数を返すのは、実際に何が起きたかを見せるため。</summary>
public sealed record UserTagEditResult
{
    public required UserTagMaster Master { get; init; }

    public required int ItemsUpdated { get; init; }

    /// <summary>この操作でuserTagが空になったitem数。編集の対象に戻るので黙って進めない。</summary>
    public int ItemsLeftUntagged { get; init; }

    /// <summary>移動先のトップが新しく付いたitem数。絞り込みの結果が変わるので出す。</summary>
    public int ItemsGainedTop { get; init; }

    /// <summary>サブが無くなった元のトップを外したitem数。</summary>
    public int ItemsSourceTopRemoved { get; init; }

    /// <summary>既存の名前へ寄せた（統合した）かどうか。</summary>
    public bool WasMerged { get; init; }

    /// <summary>
    /// 前提が崩れていて何もしなかったか（大分類を小分類にする操作で、元の大分類が小分類を持っていたときなど）。
    /// 下見から押すまでの間に編集画面で小分類が足されることがあるので、書く直前にも確かめて断る
    /// </summary>
    public bool WasRefused { get; init; }
}

/// <summary>大分類を別の大分類の小分類にしたら何が起きるかの下見。確認の文に件数を出すため。</summary>
public sealed record NestTopPreview
{
    /// <summary>元の大分類が付いている商品の数。すべて書き換わる。</summary>
    public required int ItemCount { get; init; }

    /// <summary>そのうち、入れ先の大分類が既に付いている商品の数（入れ先の側へ小分類として足す）。</summary>
    public required int ItemsAlreadyHavingTarget { get; init; }

    /// <summary>入れ先に同じ名前の小分類が既にあり、統合になるか。</summary>
    public required bool IsMerge { get; init; }

    /// <summary>
    /// 元の大分類が小分類を持っているか（一覧か、商品の側に）。持っていればこの操作はできない——
    /// 小分類の下にもう1段は作れないので、持っている小分類の行き場が無い
    /// </summary>
    public required bool HasSubs { get; init; }
}

/// <summary>
/// 移動したら何が起きるかの下見。押す前に見えていないと、
/// 「空になったトップをどうするか」をユーザが決めようがない。
/// </summary>
public sealed record MoveSubPreview
{
    /// <summary>このサブが付いているitem数。</summary>
    public required int ItemCount { get; init; }

    /// <summary>移動後、元のトップがサブなしで残るitem数。ここが判断の対象。</summary>
    public required int ItemsLeavingEmptyTop { get; init; }

    /// <summary>移動先のトップが新しく付くitem数。</summary>
    public required int ItemsGainingTop { get; init; }
}

public interface IUserTagService
{
    Task<IReadOnlyList<UserTagUsage>> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrphanUserTag>> LoadOrphansAsync(CancellationToken cancellationToken = default);

    Task<UserTagEditResult> RenameTopAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    Task<UserTagEditResult> RenameSubAsync(string top, string oldName, string newName, CancellationToken cancellationToken = default);

    Task<UserTagEditResult> DeleteTopAsync(string name, CancellationToken cancellationToken = default);

    Task<UserTagEditResult> DeleteSubAsync(string top, string name, CancellationToken cancellationToken = default);

    Task<UserTagMaster> SetMemoAsync(string top, string? sub, string? memo, CancellationToken cancellationToken = default);

    Task<UserTagMaster> ReorderAsync(string? top, IReadOnlyList<string> names, CancellationToken cancellationToken = default);

    Task<MoveSubPreview> PreviewMoveSubAsync(string fromTop, string sub, string toTop, CancellationToken cancellationToken = default);

    Task<UserTagEditResult> MoveSubAsync(
        string fromTop,
        string sub,
        string toTop,
        bool dropEmptySourceTop,
        CancellationToken cancellationToken = default);

    Task<NestTopPreview> PreviewNestTopAsync(string top, string intoTop, CancellationToken cancellationToken = default);

    Task<UserTagEditResult> NestTopAsync(string top, string intoTop, CancellationToken cancellationToken = default);
}

/// <summary>
/// userTagマスタの改名・削除・メモ。
///
/// item側は名前で参照しているので、改名は全itemの一括書き換えを伴う
/// （読みやすさを優先した設計の代償。実測では1000件でも一瞬なので許容する）。
/// 同じ名前へ改名すると統合になる。統合も削除も戻せないので、
/// 何件が書き換わるかを先に数えられるようにしてある。
/// </summary>
public sealed class UserTagService : IUserTagService
{
    private readonly DataStore _store;

    public UserTagService(DataStore store)
    {
        _store = store;
    }

    /// <summary>マスタにある分だけを、マスタの並び順で返す。</summary>
    public async Task<IReadOnlyList<UserTagUsage>> LoadUsageAsync(CancellationToken cancellationToken = default)
    {
        var master = _store.UserTags.Load();
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return master.Tops.Select(top => Count(loaded.Items, top.Name)).ToList();
    }

    /// <summary>
    /// マスタから消えたか名前が変わった分類を、itemがまだ参照している状態を拾う。
    /// 要確認は知らせるだけなので、実際に直す場所としてここに出す。
    /// </summary>
    public async Task<IReadOnlyList<OrphanUserTag>> LoadOrphansAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return FindOrphans(_store.UserTags.Load(), loaded.Items);
    }

    /// <summary>
    /// マスタに無い参照を数える。トップが無いときは、その配下のサブまでは見ない
    /// （トップを直せばサブも一緒に付いてくるので、二重に出しても直す手が増えるだけ）。
    /// </summary>
    public static IReadOnlyList<OrphanUserTag> FindOrphans(UserTagMaster master, IReadOnlyList<ItemRecord> items)
    {
        var subsByTop = FirstWins.Map(
            master.Tops,
            top => top.Name,
            top => top.Subs.Select(sub => sub.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase),
            StringComparer.CurrentCultureIgnoreCase);

        var found = new List<(string Top, string? Sub)>();

        foreach (var assignment in items.SelectMany(item => item.Local.UserTags))
        {
            if (!subsByTop.TryGetValue(assignment.Top, out var known))
            {
                found.Add((assignment.Top, null));
                continue;
            }

            foreach (var sub in assignment.Subs.Where(sub => !known.Contains(sub)))
            {
                found.Add((assignment.Top, sub));
            }
        }

        return found
            // 表記揺れは1件にまとめる。別々に出すと、直す手が無駄に増える
            .GroupBy(
                entry => entry,
                (key, group) => new OrphanUserTag { Top = key.Top, Sub = key.Sub, ItemCount = group.Count() },
                OrphanKeyComparer.Instance)
            .OrderBy(orphan => orphan.IsSub)
            .ThenByDescending(orphan => orphan.ItemCount)
            .ThenBy(orphan => orphan.Top, StringComparer.CurrentCulture)
            .ThenBy(orphan => orphan.Sub, StringComparer.CurrentCulture)
            .ToList();
    }

    private sealed class OrphanKeyComparer : IEqualityComparer<(string Top, string? Sub)>
    {
        public static readonly OrphanKeyComparer Instance = new();

        public bool Equals((string Top, string? Sub) left, (string Top, string? Sub) right)
            => Same(left.Top, right.Top)
                && (left.Sub is null
                    ? right.Sub is null
                    : right.Sub is not null && Same(left.Sub, right.Sub));

        public int GetHashCode((string Top, string? Sub) key) => HashCode.Combine(
            StringComparer.CurrentCultureIgnoreCase.GetHashCode(key.Top),
            key.Sub is null ? 0 : StringComparer.CurrentCultureIgnoreCase.GetHashCode(key.Sub));
    }

    /// <summary>
    /// トップレベルを改名する。新しい名前が既にあれば統合になる
    /// （両方付いていたitemでは1件にまとめ、サブレベルは足し合わせる）。
    /// マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。
    /// </summary>
    public async Task<UserTagEditResult> RenameTopAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var target = newName.Trim();

        // 表記まで同じときだけ何もしない。大文字と小文字だけの変更（vrchat → VRChat）は改名として通す
        // （前は同じ名前と見なして黙って何もせず、画面では窓が閉じるのに名前が変わらなかった。点検 2026-09-28）
        if (target.Length == 0 || string.Equals(oldName, target, StringComparison.Ordinal))
        {
            return new UserTagEditResult { Master = _store.UserTags.Load(), ItemsUpdated = 0 };
        }

        var merged = false;
        var updated = await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();
                var from = tops.FindIndex(top => Same(top.Name, oldName));
                var into = tops.FindIndex(top => Same(top.Name, target));
                merged = into >= 0 && into != from;

                if (merged && from >= 0)
                {
                    // サブレベルは寄せ先に足す。同じ名前のサブは寄せ先のメモを残す
                    var subs = tops[into].Subs.ToList();
                    foreach (var sub in tops[from].Subs.Where(sub => !subs.Any(entry => Same(entry.Name, sub.Name))))
                    {
                        subs.Add(sub);
                    }

                    tops[into] = new UserTagTop
                    {
                        Name = tops[into].Name,
                        Memo = MergeMemo(tops[into].Memo, tops[from].Name, tops[from].Memo),
                        Subs = subs,
                    };

                    tops.RemoveAt(from);
                }
                else if (from >= 0)
                {
                    tops[from] = Replace(tops[from], target, tops[from].Subs);
                }

                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameTopIn(item, oldName, target),
            cancellationToken);

        return new UserTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
            WasMerged = merged,
        };
    }

    public async Task<UserTagEditResult> RenameSubAsync(
        string top,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var target = newName.Trim();

        // 表記まで同じときだけ何もしない。大文字と小文字だけの変更（vrchat → VRChat）は改名として通す
        // （前は同じ名前と見なして黙って何もせず、画面では窓が閉じるのに名前が変わらなかった。点検 2026-09-28）
        if (target.Length == 0 || string.Equals(oldName, target, StringComparison.Ordinal))
        {
            return new UserTagEditResult { Master = _store.UserTags.Load(), ItemsUpdated = 0 };
        }

        var merged = false;
        var updated = await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();
                var index = tops.FindIndex(entry => Same(entry.Name, top));

                if (index >= 0)
                {
                    var subs = tops[index].Subs.ToList();
                    var from = subs.FindIndex(sub => Same(sub.Name, oldName));
                    var into = subs.FindIndex(sub => Same(sub.Name, target));
                    merged = into >= 0 && into != from;

                    if (merged && from >= 0)
                    {
                        subs[into] = new UserTagSub
                        {
                            Name = subs[into].Name,
                            Memo = MergeMemo(subs[into].Memo, subs[from].Name, subs[from].Memo),
                        };

                        subs.RemoveAt(from);
                    }
                    else if (from >= 0)
                    {
                        subs[from] = new UserTagSub { Name = target, Memo = subs[from].Memo };
                    }

                    tops[index] = Replace(tops[index], tops[index].Name, subs);
                }

                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameSubIn(item, top, oldName, target),
            cancellationToken);

        return new UserTagEditResult
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
    public async Task<UserTagEditResult> DeleteTopAsync(string name, CancellationToken cancellationToken = default)
    {
        var updated = await ChangeMasterAsync(
            master => new UserTagMaster { Tops = master.Tops.Where(top => !Same(top.Name, name)).ToList() },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => item.UserTags.Any(assignment => Same(assignment.Top, name))
                ? item with { UserTags = item.UserTags.Where(assignment => !Same(assignment.Top, name)).ToList() }
                : null,
            cancellationToken);

        return new UserTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
        };
    }

    public async Task<UserTagEditResult> DeleteSubAsync(
        string top,
        string name,
        CancellationToken cancellationToken = default)
    {
        var updated = await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();
                var index = tops.FindIndex(entry => Same(entry.Name, top));

                if (index >= 0)
                {
                    tops[index] = Replace(
                        tops[index],
                        tops[index].Name,
                        tops[index].Subs.Where(sub => !Same(sub.Name, name)).ToList());
                }

                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);

        var rewritten = await RewriteItemsAsync(
            item => RenameSubIn(item, top, name, newName: null),
            cancellationToken);

        return new UserTagEditResult { Master = updated, ItemsUpdated = rewritten.Updated };
    }

    /// <summary>
    /// サブレベルを別のトップへ移す。
    ///
    /// 削除して付け直すとitemの割当てが失われるので、専用の操作にする。
    /// item側は「このitemは○○だ」というサブの判断を保つため、移動先のトップを
    /// 付けたうえでサブを移す（移動先が既に付いていれば、そこへ足すだけ）。
    /// 元のトップは、他のサブや単独の割当てとして残ることがあるのでそのままにする。
    /// </summary>
    /// <summary>
    /// 移したら何が起きるかを先に数える。特に「元のトップがサブなしで残る」件数は、
    /// それをどうするかをユーザに決めてもらうために要る。
    /// </summary>
    public async Task<MoveSubPreview> PreviewMoveSubAsync(
        string fromTop,
        string sub,
        string toTop,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        var count = 0;
        var emptied = 0;
        var gaining = 0;

        foreach (var local in loaded.Items.Select(item => item.Local))
        {
            var source = local.UserTags.FirstOrDefault(
                assignment => Same(assignment.Top, fromTop) && assignment.Subs.Any(entry => Same(entry, sub)));

            if (source is null)
            {
                continue;
            }

            count++;

            if (!local.UserTags.Any(assignment => Same(assignment.Top, toTop)))
            {
                gaining++;
            }

            if (source.Subs.Count == 1)
            {
                emptied++;
            }
        }

        return new MoveSubPreview
        {
            ItemCount = count,
            ItemsLeavingEmptyTop = emptied,
            ItemsGainingTop = gaining,
        };
    }

    public async Task<UserTagEditResult> MoveSubAsync(
        string fromTop,
        string sub,
        string toTop,
        bool dropEmptySourceTop,
        CancellationToken cancellationToken = default)
    {
        UserTagSub? moving = null;
        var updated = await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();

                var from = tops.FindIndex(entry => Same(entry.Name, fromTop));
                var to = tops.FindIndex(entry => Same(entry.Name, toTop));

                if (from < 0 || to < 0 || from == to)
                {
                    return null;
                }

                moving = tops[from].Subs.FirstOrDefault(entry => Same(entry.Name, sub));
                if (moving is null)
                {
                    return null;
                }

                tops[from] = Replace(tops[from], tops[from].Name, tops[from].Subs.Where(entry => !Same(entry.Name, sub)).ToList());

                var targetSubs = tops[to].Subs.ToList();
                var existing = targetSubs.FindIndex(entry => Same(entry.Name, moving.Name));
                if (existing >= 0)
                {
                    // 移動先に同じ名前があれば、そこへ寄せる（メモは書き足す）
                    targetSubs[existing] = new UserTagSub
                    {
                        Name = targetSubs[existing].Name,
                        Memo = MergeMemo(targetSubs[existing].Memo, $"{fromTop}／{moving.Name}", moving.Memo),
                    };
                }
                else
                {
                    targetSubs.Add(moving);
                }

                tops[to] = Replace(tops[to], tops[to].Name, targetSubs);
                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);

        if (moving is null)
        {
            return new UserTagEditResult { Master = updated, ItemsUpdated = 0 };
        }

        var gained = 0;
        var sourceRemoved = 0;
        var rewritten = await RewriteItemsAsync(
            local =>
            {
                var moved = MoveSubIn(
                    local,
                    fromTop,
                    moving.Name,
                    toTop,
                    dropEmptySourceTop,
                    out var addedTop,
                    out var removedSource);

                if (moved is null)
                {
                    return null;
                }

                if (addedTop)
                {
                    gained++;
                }

                if (removedSource)
                {
                    sourceRemoved++;
                }

                return moved;
            },
            cancellationToken);

        return new UserTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
            ItemsGainedTop = gained,
            ItemsSourceTopRemoved = sourceRemoved,
        };
    }

    /// <summary>
    /// 大分類を別の大分類の小分類にしたら何が起きるかを数える。確認の文に件数を書き、
    /// 小分類を持つ大分類ならボタンの側で断るため。
    /// </summary>
    public async Task<NestTopPreview> PreviewNestTopAsync(
        string top,
        string intoTop,
        CancellationToken cancellationToken = default)
    {
        var master = _store.UserTags.Load();
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var locals = loaded.Items.Select(item => item.Local).ToList();

        var source = master.Tops.FirstOrDefault(entry => Same(entry.Name, top));
        var target = master.Tops.FirstOrDefault(entry => Same(entry.Name, intoTop));

        return new NestTopPreview
        {
            ItemCount = locals.Count(local => local.UserTags.Any(assignment => Same(assignment.Top, top))),
            ItemsAlreadyHavingTarget = locals.Count(local =>
                local.UserTags.Any(assignment => Same(assignment.Top, top))
                && local.UserTags.Any(assignment => Same(assignment.Top, intoTop))),
            IsMerge = target?.Subs.Any(sub => Same(sub.Name, source?.Name ?? top)) ?? false,
            HasSubs = (source?.Subs.Count ?? 0) > 0 || HasItemSubs(locals, top),
        };
    }

    /// <summary>
    /// 大分類を、別の大分類の小分類にする（ユーザ要望 2026-09-29：作ってから、別の大分類の下に置くべきだったと気付く）。
    /// 付いていた商品は「入れ先の大分類＋小分類（元の大分類の名前）」に書き換える。入れ先に同じ名前の小分類があれば統合する（メモは書き足す）。
    ///
    /// **小分類を持つ大分類ではしない。**小分類の下にもう1段は作れないので、持っている小分類の行き場が無い。
    /// 一覧の小分類は錠の中で、商品の側の小分類（一覧に無い名前）は書き換える前に確かめて断る。
    /// 小分類へ変えた後に元へ戻す操作は無いので、画面は押す前に件数を言って確かめる。
    /// </summary>
    public async Task<UserTagEditResult> NestTopAsync(
        string top,
        string intoTop,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (Same(top, intoTop) || HasItemSubs(loaded.Items.Select(item => item.Local), top))
        {
            return new UserTagEditResult { Master = _store.UserTags.Load(), ItemsUpdated = 0, WasRefused = true };
        }

        string? subName = null;
        var merged = false;
        var updated = await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();
                var from = tops.FindIndex(entry => Same(entry.Name, top));
                var into = tops.FindIndex(entry => Same(entry.Name, intoTop));

                if (from < 0 || into < 0 || from == into || tops[from].Subs.Count > 0)
                {
                    return null;
                }

                var source = tops[from];
                subName = source.Name;

                var subs = tops[into].Subs.ToList();
                var existing = subs.FindIndex(entry => Same(entry.Name, source.Name));
                if (existing >= 0)
                {
                    // 同じ名前の小分類があれば統合する。メモは捨てずに書き足す（大分類の統合と同じ）
                    merged = true;
                    subs[existing] = new UserTagSub
                    {
                        Name = subs[existing].Name,
                        Memo = MergeMemo(subs[existing].Memo, source.Name, source.Memo),
                    };
                    subName = subs[existing].Name;
                }
                else
                {
                    subs.Add(new UserTagSub { Name = source.Name, Memo = source.Memo });
                }

                tops[into] = Replace(tops[into], tops[into].Name, subs);
                tops.RemoveAt(from);
                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);

        if (subName is null)
        {
            return new UserTagEditResult { Master = updated, ItemsUpdated = 0, WasRefused = true };
        }

        var targetName = updated.Tops.First(entry => Same(entry.Name, intoTop)).Name;
        var rewritten = await RewriteItemsAsync(
            local => NestTopIn(local, top, targetName, subName),
            cancellationToken);

        return new UserTagEditResult
        {
            Master = updated,
            ItemsUpdated = rewritten.Updated,
            ItemsLeftUntagged = rewritten.LeftUntagged,
            WasMerged = merged,
        };
    }

    /// <summary>商品の側で、その大分類の下に小分類が付いているか（一覧に無い小分類も含む）。</summary>
    private static bool HasItemSubs(IEnumerable<LocalBlock> locals, string top)
        => locals.Any(local => local.UserTags.Any(assignment => Same(assignment.Top, top) && assignment.Subs.Count > 0));

    /// <summary>
    /// 商品1件の「元の大分類」を「入れ先＋小分類」に置き換える。入れ先が既に付いていれば、そこへ小分類を足す。
    /// 置き換えは元の位置で行う（付けた順が編集画面の並びになるので、勝手に末尾へ動かさない）。
    ///
    /// 書く直前に元の大分類へ小分類が付いていたら（下見の後に編集画面で足された）、この商品は触らない。
    /// 付いた小分類を捨てずに、一覧に無い名前として管理の画面に出して人に決めてもらう
    /// </summary>
    private static LocalBlock? NestTopIn(LocalBlock local, string top, string intoTop, string sub)
    {
        var source = local.UserTags.FirstOrDefault(assignment => Same(assignment.Top, top));
        if (source is null || source.Subs.Count > 0)
        {
            return null;
        }

        var result = new List<UserTagAssignment>();
        var hasTarget = local.UserTags.Any(assignment => Same(assignment.Top, intoTop));

        foreach (var assignment in local.UserTags)
        {
            if (Same(assignment.Top, top))
            {
                if (!hasTarget)
                {
                    result.Add(new UserTagAssignment { Top = intoTop, Subs = [sub] });
                }

                continue;
            }

            if (Same(assignment.Top, intoTop) && !assignment.Subs.Any(entry => Same(entry, sub)))
            {
                result.Add(assignment with { Subs = [.. assignment.Subs, sub] });
                continue;
            }

            result.Add(assignment);
        }

        return local with { UserTags = result };
    }

    /// <summary>メモだけを書き換える。item側は名前しか参照していないので影響しない。</summary>
    public async Task<UserTagMaster> SetMemoAsync(
        string top,
        string? sub,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();

        return await ChangeMasterAsync(
            master =>
            {
                var tops = master.Tops.ToList();
                var index = tops.FindIndex(entry => Same(entry.Name, top));
                if (index < 0)
                {
                    return null;
                }

                if (sub is null)
                {
                    tops[index] = new UserTagTop { Name = tops[index].Name, Memo = trimmed, Subs = tops[index].Subs };
                }
                else
                {
                    var subs = tops[index].Subs.ToList();
                    var subIndex = subs.FindIndex(entry => Same(entry.Name, sub));
                    if (subIndex < 0)
                    {
                        return null;
                    }

                    subs[subIndex] = new UserTagSub { Name = subs[subIndex].Name, Memo = trimmed };
                    tops[index] = Replace(tops[index], tops[index].Name, subs);
                }

                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);
    }

    /// <summary>
    /// 並べ替える。<paramref name="top"/> が null ならトップレベル、指定すればその配下のサブ。
    ///
    /// マスタの並びは「よく使う順」「意味の近い順」といった、名前からは出てこない
    /// 判断を持てる唯一の場所なので、追加順のままにしない。
    /// item側は名前で参照しているので、並べ替えでitemに触る必要はない。
    /// </summary>
    public async Task<UserTagMaster> ReorderAsync(
        string? top,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        return await ChangeMasterAsync(
            master =>
            {
                if (top is null)
                {
                    return new UserTagMaster { Tops = Sort(master.Tops, names, entry => entry.Name) };
                }

                var tops = master.Tops.ToList();
                var index = tops.FindIndex(entry => Same(entry.Name, top));
                if (index < 0)
                {
                    return null;
                }

                tops[index] = Replace(tops[index], tops[index].Name, Sort(tops[index].Subs, names, sub => sub.Name));
                return new UserTagMaster { Tops = tops };
            },
            cancellationToken);
    }

    /// <summary>
    /// マスタを読み直してから書き換える。<c>userTags.json</c> は書き手が2つ（管理画面・編集画面）あるので、
    /// **読んでから書くまでを錠の中に入れる**（`docs/spec/data-model.md`）。
    /// 錠の外で読むと、並べ替えや改名の最中に編集画面が足したタグが消える。
    /// <paramref name="change"/> が null を返したら書かない。
    /// </summary>
    private async Task<UserTagMaster> ChangeMasterAsync(
        Func<UserTagMaster, UserTagMaster?> change,
        CancellationToken cancellationToken)
    {
        var written = new UserTagMaster();
        await _store.UserTags.TryUpdateAsync(
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
    /// 指定された名前の順に並べ、指定に無かったものは末尾へ残す。
    /// 画面が古い一覧を送ってきても、黙って消えないようにするため。
    /// </summary>
    private static List<T> Sort<T>(IReadOnlyList<T> source, IReadOnlyList<string> names, Func<T, string> nameOf)
    {
        var remaining = source.ToList();
        var sorted = new List<T>(remaining.Count);

        foreach (var name in names)
        {
            var index = remaining.FindIndex(entry => Same(nameOf(entry), name));
            if (index >= 0)
            {
                sorted.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
        }

        sorted.AddRange(remaining);
        return sorted;
    }

    private static UserTagUsage Count(IReadOnlyList<ItemRecord> items, string top)
    {
        var subCounts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var itemCount = 0;

        foreach (var assignment in items.SelectMany(item => item.Local.UserTags).Where(entry => Same(entry.Top, top)))
        {
            itemCount++;
            foreach (var sub in assignment.Subs)
            {
                subCounts[sub] = subCounts.GetValueOrDefault(sub) + 1;
            }
        }

        return new UserTagUsage { Top = top, ItemCount = itemCount, SubCounts = subCounts };
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
            // 全件を先に読んでから順に書く。書く頃には写しが古いので、**書き換えを書く直前の値に当てる**
            // （名指しは他の項目を守るだけで、userTag そのものは守らない）。
            // 触る物が無ければ書かない
            var before = 0;
            var after = 0;
            var written = await _store.Items.ChangeLocalAsync(
                item.Id,
                current =>
                {
                    before = current.UserTags.Count;
                    var local = transform(current);
                    after = local?.UserTags.Count ?? before;
                    return local;
                },
                LocalOwners.UserTags,
                cancellationToken);

            if (!written)
            {
                continue;
            }

            updated++;

            if (before > 0 && after == 0)
            {
                leftUntagged++;
            }
        }

        return (updated, leftUntagged);
    }

    /// <summary>トップの改名。寄せ先が既に付いていればサブを足し合わせて1件にする。</summary>
    private static LocalBlock? RenameTopIn(LocalBlock local, string oldName, string newName)
    {
        if (!local.UserTags.Any(assignment => Same(assignment.Top, oldName)))
        {
            return null;
        }

        var result = new List<UserTagAssignment>();
        foreach (var assignment in local.UserTags)
        {
            var top = Same(assignment.Top, oldName) ? newName : assignment.Top;
            var existing = result.FindIndex(entry => Same(entry.Top, top));

            if (existing < 0)
            {
                result.Add(new UserTagAssignment { Top = top, Subs = assignment.Subs });
                continue;
            }

            var subs = result[existing].Subs.ToList();
            subs.AddRange(assignment.Subs.Where(sub => !subs.Any(entry => Same(entry, sub))));
            result[existing] = new UserTagAssignment { Top = result[existing].Top, Subs = subs };
        }

        return local with { UserTags = result };
    }

    /// <summary><paramref name="newName"/> が null なら削除。</summary>
    private static LocalBlock? RenameSubIn(LocalBlock local, string top, string oldName, string? newName)
    {
        var touched = false;
        var result = new List<UserTagAssignment>();

        foreach (var assignment in local.UserTags)
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

            result.Add(new UserTagAssignment { Top = assignment.Top, Subs = subs });
        }

        return touched ? local with { UserTags = result } : null;
    }

    /// <summary>
    /// 統合するとき、寄せ元のメモを寄せ先へ書き足す。
    ///
    /// メモは「何をここに入れるか」の基準なので、統合で片方が黙って消えると
    /// 判断の根拠だけが失われる。どちらから来た文なのかが後で分かるよう、
    /// 元の名前を添えて残す（同じ文なら重ねない）。
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

    /// <summary>
    /// 元のトップからサブを外し、移動先のトップへ付け替える。
    /// 移動先が付いていなければ足す（<paramref name="addedTop"/> で知らせる）。
    ///
    /// サブが無くなった元のトップをどうするかは、他の理由で付いている可能性があるので
    /// アプリでは決められない。<paramref name="dropEmptySourceTop"/> でユーザの判断を受ける。
    /// </summary>
    private static LocalBlock? MoveSubIn(
        LocalBlock local,
        string fromTop,
        string sub,
        string toTop,
        bool dropEmptySourceTop,
        out bool addedTop,
        out bool removedSourceTop)
    {
        addedTop = false;
        removedSourceTop = false;

        var source = local.UserTags.FirstOrDefault(
            assignment => Same(assignment.Top, fromTop) && assignment.Subs.Any(entry => Same(entry, sub)));

        if (source is null)
        {
            return null;
        }

        var result = local.UserTags
            .Select(assignment => Same(assignment.Top, fromTop)
                ? assignment with { Subs = assignment.Subs.Where(entry => !Same(entry, sub)).ToList() }
                : assignment)
            .ToList();

        if (dropEmptySourceTop && source.Subs.Count == 1)
        {
            removedSourceTop = true;
            result.RemoveAll(assignment => Same(assignment.Top, fromTop) && assignment.Subs.Count == 0);
        }

        var target = result.FindIndex(assignment => Same(assignment.Top, toTop));
        if (target < 0)
        {
            addedTop = true;
            result.Add(new UserTagAssignment { Top = toTop, Subs = [sub] });
        }
        else if (!result[target].Subs.Any(entry => Same(entry, sub)))
        {
            result[target] = result[target] with { Subs = [.. result[target].Subs, sub] };
        }

        return local with { UserTags = result };
    }

    private static UserTagTop Replace(UserTagTop top, string name, IReadOnlyList<UserTagSub> subs)
        => new() { Name = name, Memo = top.Memo, Subs = subs };

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
