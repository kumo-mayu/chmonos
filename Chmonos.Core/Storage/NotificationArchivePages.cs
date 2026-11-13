using System.Globalization;
using System.Text.Json;
using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保管した月の目録の1件。行を読み直すときの位置（<see cref="Ordinal"/>）と、並べる・絞るのに要る欄だけを持つ。
/// 本文・変わったところ（行の大半の大きさ）は持たない：50MB の月を丸ごと読むと作業セットが 359MB になった（2026-11-10 に測った）。
/// </summary>
/// <param name="Ordinal">月のファイルの中で何行目の知らせか（版の行・空行を除いて0から）。</param>
public sealed record NotificationArchiveEntry(int Ordinal, string Id, string Title, DateTimeOffset LastChangedAt);

/// <summary>
/// 保管した月の目録（保管した通知を読む画面。ユーザ案 2026-11-10「その形で良いです」）。
/// 一度前から流して、行の位置と題・日時だけを控える。中身は100件ずつ <see cref="NotificationArchive.ReadEntriesAsync"/> で読む。
/// </summary>
public sealed class NotificationArchiveIndex
{
    /// <summary>商品名の照らし方。アバター名で絞る欄（編集画面）と同じく、大文字小文字・ひらがなとカタカナ・全角と半角を区別しない。</summary>
    private const CompareOptions NameMatch = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    internal NotificationArchiveIndex(string month, IReadOnlyList<NotificationArchiveEntry> newest, bool precise, int unreadableLines, int unreadableBlocks)
    {
        Month = month;
        Newest = newest;
        Precise = precise;
        UnreadableLines = unreadableLines;
        UnreadableBlocks = unreadableBlocks;
    }

    public string Month { get; }

    /// <summary>読めた知らせ（同じ ID・同じ作った日時は1件）を新しい順に。</summary>
    public IReadOnlyList<NotificationArchiveEntry> Newest { get; }

    /// <summary>
    /// 目録を作ったときの読み方（正確な読みか）。中身を読み直すときも同じ読み方にする：
    /// 速い読みと正確な読みでは壊れた所で出る行が違い得るので、読み方が違うと行の位置がずれる。
    /// </summary>
    internal bool Precise { get; }

    /// <summary>読めない行（壊れた行・新しい版の行・知らない種類）の数。</summary>
    public int UnreadableLines { get; }

    /// <summary>gzip が壊れていて飛ばした所の数（行の数とは別。1か所に何件あったかは分からない）。</summary>
    public int UnreadableBlocks { get; }

    /// <summary>
    /// 題（商品名）に <paramref name="name"/> を含む物を、新しい順のまま。空なら全部。
    /// 照らすのは目録に控えた題なので、ファイルを読み直さない（打ち替えるたびに月を流さない）。
    /// </summary>
    public IReadOnlyList<NotificationArchiveEntry> Matching(string? name, CultureInfo? culture = null)
    {
        var needle = name?.Trim() ?? string.Empty;
        if (needle.Length == 0)
        {
            return Newest;
        }

        var compare = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        return Newest.Where(entry => compare.IndexOf(entry.Title, needle, NameMatch) >= 0).ToList();
    }
}

public sealed partial class NotificationArchive
{
    /// <summary>目録に控える欄だけを読む型。本文・変わったところは読み飛ばす（解いた物を作らない）。</summary>
    private sealed record EntryHead
    {
        public required string Id { get; init; }

        /// <summary>知らない種類の行を、中身を読む口（<see cref="Parse"/>）と同じく読めない行に数えるために受ける。</summary>
        public required NotificationKind Kind { get; init; }

        public required string Title { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }
    }

    /// <summary>
    /// 月の目録を作る（保管した通知を読む画面。ユーザ案 2026-11-10）。月を1回前から流し、知らせごとに位置・ID・題・最後に変わった日時だけを控える。
    ///
    /// - **並びは最後に変わった日時の新しい順**（今の通知の並び・行に出す「何日前」と同じ鍵）。同じ日時はファイルの後ろ（後から保管した物）を先に。
    /// - **同じ ID・同じ作った日時は前の物を残す**（<see cref="ReadAsync"/> と同じ。足す途中で落ちてもう一度足された写しは中身が同じ）。
    ///   同じ ID でも作った日時が違えば別の知らせ（同じ商品の別の機会の更新）として両方並べる。
    /// - 読めない行・壊れたかたまりは <see cref="ReadAsync"/> と同じく飛ばして数える。
    /// </summary>
    public async Task<NotificationArchiveIndex> IndexAsync(string month, CancellationToken cancellationToken = default)
    {
        var path = FileFor(month);
        if (!File.Exists(path))
        {
            return new NotificationArchiveIndex(month, [], precise: false, 0, 0);
        }

        var scan = new ArchiveLineScan();
        var (entries, unreadable) = await CollectEntriesAsync(path, precise: false, scan, cancellationToken);
        var precise = false;
        if (!scan.Intact)
        {
            // 速い読みの控えは捨てて頭から読み直す（ReadAsync と同じ。途中から継ぐと位置がずれる）
            scan = new ArchiveLineScan();
            (entries, unreadable) = await CollectEntriesAsync(path, precise: true, scan, cancellationToken);
            precise = true;
        }

        var newest = entries
            .OrderByDescending(entry => entry.LastChangedAt)
            .ThenByDescending(entry => entry.Ordinal)
            .ToList();
        return new NotificationArchiveIndex(month, newest, precise, unreadable, scan.DamagedBlocks);
    }

    private async Task<(List<NotificationArchiveEntry> Entries, int Unreadable)> CollectEntriesAsync(
        string path, bool precise, ArchiveLineScan scan, CancellationToken cancellationToken)
    {
        var entries = new List<NotificationArchiveEntry>();
        // ReadAsync と同じ鍵（ID と作った日時）。商品の更新の知らせは別の機会でも同じ ID なので、ID だけだと2件目から目録に入らない
        var seen = new HashSet<(string Id, DateTimeOffset CreatedAt)>();
        var unreadable = 0;
        var ordinal = -1;
        await foreach (var (line, version) in RecordLinesAsync(path, precise, scan, cancellationToken))
        {
            ordinal++;
            if (version > StoreFormat.Current || Head(line, version, path) is not { } head)
            {
                unreadable++;
                continue;
            }

            if (seen.Add((head.Id, head.CreatedAt)))
            {
                entries.Add(new NotificationArchiveEntry(ordinal, head.Id, head.Title, head.UpdatedAt ?? head.CreatedAt));
            }
        }

        return (entries, unreadable);
    }

    /// <summary>今の版の行は控える欄だけを読む。前の版の行は中身を読む口と同じ移し替えを通してから取る（欄の名前が変わっていても同じ値になる）。</summary>
    private static EntryHead? Head(string line, int version, string path)
    {
        if (version == StoreFormat.Current || StoreFormat.LeavesAsIs(version))
        {
            try
            {
                return JsonSerializer.Deserialize<EntryHead>(line, JsonStore.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return Parse(line, version, path) is { } record
            ? new EntryHead { Id = record.Id, Kind = record.Kind, Title = record.Title, CreatedAt = record.CreatedAt, UpdatedAt = record.UpdatedAt }
            : null;
    }

    /// <summary>
    /// 目録の中の何件か（ふつうは画面が次に出す100件）の中身を読む。返す順は <paramref name="entries"/> の順。
    /// 月をもう一度前から流し、その位置の行だけを解く（全部を解かない・載せない）。欲しい物が揃ったらそこで読み終える。
    /// 目録の後で月が書き直された（閉じるときの整理）などで、位置の行の ID が違えば出さない（別の知らせを出さない）。
    /// </summary>
    public async Task<IReadOnlyList<NotificationRecord>> ReadEntriesAsync(
        NotificationArchiveIndex index, IReadOnlyList<NotificationArchiveEntry> entries, CancellationToken cancellationToken = default)
    {
        var path = FileFor(index.Month);
        if (entries.Count == 0 || !File.Exists(path))
        {
            return [];
        }

        var wanted = entries.ToDictionary(entry => entry.Ordinal);
        var found = new Dictionary<int, NotificationRecord>(entries.Count);
        var ordinal = -1;
        await foreach (var (line, version) in RecordLinesAsync(path, index.Precise, new ArchiveLineScan(), cancellationToken))
        {
            ordinal++;
            if (!wanted.TryGetValue(ordinal, out var entry))
            {
                continue;
            }

            if (Parse(line, version, path) is { } record && string.Equals(record.Id, entry.Id, StringComparison.Ordinal))
            {
                found[ordinal] = record;
            }

            if (found.Count == wanted.Count)
            {
                break;
            }
        }

        return entries.Where(entry => found.ContainsKey(entry.Ordinal)).Select(entry => found[entry.Ordinal]).ToList();
    }
}
