using System.Collections.Concurrent;
using System.Globalization;
using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>月をまたぐ目録の1件。どの月の目録の何件目か。</summary>
public sealed record NotificationArchiveCatalogEntry(string Month, NotificationArchiveEntry Entry);

/// <summary>
/// 保管した全部の月をまたぐ目録（保管した通知を商品・変更の種類で切り替えて見る。open 146・ユーザ判断 2026-11-19）。
/// 月の目録（位置・題・日時・種類・商品）を全部の月ぶん束ね、最後に変わった日時の新しい順に並べる。中身は持たない（月の目録と同じ理由）。
/// ディスクには書かない：目録は月のファイルから毎回作れる値で、書くと月のファイルとずれる物が1つ増える
/// </summary>
public sealed class NotificationArchiveCatalog
{
    private readonly IReadOnlyDictionary<string, NotificationArchiveIndex> _months;

    internal NotificationArchiveCatalog(IReadOnlyDictionary<string, NotificationArchiveIndex> months)
    {
        _months = months;
        Newest = months
            .SelectMany(month => month.Value.Newest.Select(entry => new NotificationArchiveCatalogEntry(month.Key, entry)))
            .OrderByDescending(item => item.Entry.LastChangedAt)
            .ThenByDescending(item => item.Month, StringComparer.Ordinal)
            .ThenByDescending(item => item.Entry.Ordinal)
            .ToList();
    }

    /// <summary>1つの月だけの目録（年月で見るとき。読む口を月をまたぐときと1本にする）。</summary>
    public static NotificationArchiveCatalog OfMonth(NotificationArchiveIndex index)
        => new(new Dictionary<string, NotificationArchiveIndex>(StringComparer.Ordinal) { [index.Month] = index });

    /// <summary>全部の月の知らせ。最後に変わった日時の新しい順。</summary>
    public IReadOnlyList<NotificationArchiveCatalogEntry> Newest { get; }

    /// <summary>読めなかった所がある月があるか（その分は出していないことを画面で言う）。</summary>
    public bool HasDamage => _months.Values.Any(index => index.UnreadableLines + index.UnreadableBlocks > 0);

    /// <summary>
    /// 種類（null なら全部）・商品（null なら全部）・題の言葉（空なら全部）で絞った物を、新しい順のまま。
    /// 題の照らし方は月の目録の「商品名で絞る」と同じ（<see cref="NotificationArchiveIndex.Matching"/>）
    /// </summary>
    public IReadOnlyList<NotificationArchiveCatalogEntry> Matching(
        NotificationKind? kind = null, IReadOnlySet<string>? itemIds = null, string? name = null, CultureInfo? culture = null)
    {
        var needle = name?.Trim() ?? string.Empty;
        var compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        return Newest
            .Where(item => kind is null || item.Entry.Kind == kind)
            .Where(item => itemIds is null || item.Entry.ItemId is { } id && itemIds.Contains(id))
            .Where(item => needle.Length == 0 || compare.IndexOf(item.Entry.Title, needle, NotificationArchiveIndex.NameMatch) >= 0)
            .ToList();
    }

    /// <summary>
    /// 商品で選ぶ欄の候補（名前 → 商品の ID）。名前は、その商品の新しい知らせの題（商品の知らせの題は商品名）。
    /// 同じ名前の別の商品は「名前（ID）」で分ける（候補の欄は文字で選ぶので、同じ文字だと選び分けられない）
    /// </summary>
    public IReadOnlyDictionary<string, string> ItemChoices()
    {
        var named = new List<(string Name, string Id)>();
        foreach (var group in Newest.Where(item => item.Entry.ItemId is not null).GroupBy(item => item.Entry.ItemId!, StringComparer.Ordinal))
        {
            // 商品ページの変更の題は商品名そのもの。ほかの種類は題が文のことがあるので、無いときだけ使う
            var title = (group.FirstOrDefault(item => item.Entry.Kind == NotificationKind.ItemUpdated) ?? group.First()).Entry.Title;
            named.Add((title, group.Key));
        }

        var duplicated = named.GroupBy(pair => pair.Name, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, id) in named)
        {
            choices[duplicated.Contains(name) ? $"{name}（{id}）" : name] = id;
        }

        return choices;
    }

    /// <summary>出てくる種類。数の多い順（同じなら種類の順）。</summary>
    public IReadOnlyList<(NotificationKind Kind, int Count)> Kinds()
        => Newest.GroupBy(item => item.Entry.Kind)
            .Select(group => (group.Key, group.Count()))
            .OrderByDescending(pair => pair.Item2)
            .ThenBy(pair => pair.Key)
            .ToList();

    /// <summary>出てくる商品の ID（新しい知らせのある順）。</summary>
    public IReadOnlyList<string> ItemIds()
        => Newest.Select(item => item.Entry.ItemId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// 何件か（ふつうは画面が次に出す100件）の中身を読む。返す順は <paramref name="entries"/> の順。
    /// 月ごとに分けて月の読み口（<see cref="NotificationArchive.ReadEntriesAsync"/>）で読む。月を流すのは、頼んだ物がある月だけ
    /// </summary>
    public async Task<IReadOnlyList<NotificationRecord>> ReadAsync(
        NotificationArchive archive, IReadOnlyList<NotificationArchiveCatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<NotificationArchiveCatalogEntry, NotificationRecord>();
        foreach (var month in entries.GroupBy(item => item.Month, StringComparer.Ordinal))
        {
            if (!_months.TryGetValue(month.Key, out var index))
            {
                continue;
            }

            // 位置の行が目録の後で変わっていた物は返らない（飛ばす）
            var records = await archive.ReadByOrdinalAsync(index, [.. month.Select(item => item.Entry)], cancellationToken);
            foreach (var item in month)
            {
                if (records.TryGetValue(item.Entry.Ordinal, out var record))
                {
                    found[item] = record;
                }
            }
        }

        return entries.Where(found.ContainsKey).Select(item => found[item]).ToList();
    }
}

public sealed partial class NotificationArchive
{
    /// <summary>月の目録の控え。月のファイルの大きさと書いた日時が同じなら作り直さない（開き直すたびに全部の月を流さない）。</summary>
    private readonly ConcurrentDictionary<string, (long Length, DateTime WrittenAt, NotificationArchiveIndex Index)> _indexCache =
        new(StringComparer.Ordinal);

    private readonly object _catalogGate = new();
    private Task<NotificationArchiveCatalog>? _catalogBuilding;

    /// <summary>月の目録を作り直した回数（試験用。同時に頼まれても同じ月を2回流さないことを見る）。</summary>
    internal int IndexBuildsForTests => _indexBuilds;

    private int _indexBuilds;

    /// <summary>
    /// 全部の月をまたぐ目録を作る（open 146）。前に作った月で、ファイルが変わっていない物は控えを使う。
    /// 目録の後で月が書き直されても、中身を読む口が位置の ID を照らすので、別の知らせは出さない。
    ///
    /// **作っている途中にもう一度頼まれたら、同じ作業の終わりを待つ**（商品ページの「BOOTHでの変化の履歴」と通知の画面の保管した通知が
    /// 同時に頼むことがある）。控えは作り終えた月にしか付かないので、別々に走らせると冷えた月を2本が同時に流し、50MB の月で約1秒ずつ無駄になる。
    /// 途中で足された月は、終わった後の次の頼みで作り直す（大きさと書いた日時で見る）。
    /// <paramref name="cancellationToken"/> は待つのをやめるだけで、作業は止めない（ほかの頼み手が待っている）
    /// </summary>
    public Task<NotificationArchiveCatalog> CatalogAsync(CancellationToken cancellationToken = default)
    {
        Task<NotificationArchiveCatalog> building;
        lock (_catalogGate)
        {
            if (_catalogBuilding is not { IsCompleted: false })
            {
                _catalogBuilding = Task.Run(BuildCatalogAsync);
            }

            building = _catalogBuilding;
        }

        return building.WaitAsync(cancellationToken);
    }

    private async Task<NotificationArchiveCatalog> BuildCatalogAsync()
    {
        var months = new Dictionary<string, NotificationArchiveIndex>(StringComparer.Ordinal);
        foreach (var month in Months())
        {
            var file = new FileInfo(FileFor(month));
            if (!file.Exists)
            {
                continue;
            }

            if (_indexCache.TryGetValue(month, out var cached)
                && cached.Length == file.Length
                && cached.WrittenAt == file.LastWriteTimeUtc)
            {
                months[month] = cached.Index;
                continue;
            }

            var index = await IndexAsync(month);
            Interlocked.Increment(ref _indexBuilds);
            _indexCache[month] = (file.Length, file.LastWriteTimeUtc, index);
            months[month] = index;
        }

        return new NotificationArchiveCatalog(months);
    }
}
