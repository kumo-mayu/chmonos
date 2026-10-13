using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>
/// 保存先を読んだ写し（答えを作る材料の一式）。作った後は変えない：読み直すときは新しい写しを作って差し替える
/// （問い合わせの途中で半分だけ新しい答えを返さないため）。
///
/// **読むだけ**。書き込みの錠は取らず、ファイルも書かない（`docs/spec/local-api.md`「同期」）。
/// 正しい値はいつも保存先のファイルの側にある。
/// </summary>
public sealed class QuerySnapshot
{
    private readonly DataStore _store;

    private QuerySnapshot(DataStore store, bool sharesItemCache)
    {
        _store = store;
        Paths = store.Paths;
        SharesItemCache = sharesItemCache;
    }

    /// <summary>
    /// 商品の記録を、アプリの保存先の控え（<c>ItemRepository</c>）と共有しているか（API がアプリと同じプロセスで動くとき。
    /// docs/history/headless-core-2026-10-13.md 段2）。共有していれば説明文も落とさない——画面が同じ記録を説明文ごと控えに持っているので、
    /// 落とした写しを別に作るとかえって増える。別のプロセスで動かす（試験）ときは、控えを通さず説明文を落とす
    /// </summary>
    public bool SharesItemCache { get; }

    public AppPaths Paths { get; }

    /// <summary>
    /// 読めた商品の全部（ID の順）。**説明文（本文と見出しごとの文）は落としてある**（2026-10-12・ユーザ判断。アプリと控えを共有するときは落とさない：<see cref="SharesItemCache"/>）：
    /// 一覧・検索の既定の対象・条件のどれも使わないのに、2,000 件で専用のメモリの約3割（143MB のうち 43MB）を占めていた。
    /// 本文を探すとき（<see cref="HaystackOf"/>）だけ、その商品の JSON をファイルから読む。動画のリンクは落とす前に拾っておく（<see cref="Videos"/>）
    /// </summary>
    public required IReadOnlyList<ItemRecord> Items { get; init; }

    public required IReadOnlyDictionary<string, ItemRecord> ItemsById { get; init; }

    /// <summary>読めなかった商品の ID（手で壊した JSON など。黙って減らさず、<c>/v1/health</c> で知らせる）。</summary>
    public required IReadOnlyList<string> FailedItemIds { get; init; }

    public required AvatarRegistry Registry { get; init; }

    public required AvatarCompatibilityIndex Compatibility { get; init; }

    /// <summary>アバターの見せる名前（登録簿の手で直した名前 → BOOTH の名前。画面と同じ）。</summary>
    public required IReadOnlyDictionary<string, string> AvatarNames { get; init; }

    /// <summary>持っているアバター（所持している商品か、手で「持っている」にした物）。</summary>
    public required IReadOnlySet<string> OwnedAvatarIds { get; init; }

    public required IReadOnlyDictionary<string, UnreadUpdate> UnreadUpdates { get; init; }

    /// <summary>通知の全部（既読・片付けた物も）。</summary>
    public required IReadOnlyList<NotificationRecord> Notifications { get; init; }

    public required ModificationUsage Modifications { get; init; }

    /// <summary>
    /// 読み終えていない部分（読めなかったファイルがあり、前の写しの記録で埋めた部分）。API はこの部分の印を確定せず、次の見比べで読み直す。
    /// </summary>
    public IReadOnlySet<StorePart> IncompleteParts { get; private init; } = new HashSet<StorePart>();

    public required RecentTimes Recent { get; init; }

    public required IReadOnlyList<ShopNoteRecord> ShopNotes { get; init; }

    public required IReadOnlySet<string> FavoriteShops { get; init; }

    public required AppSettings Settings { get; init; }

    public required UserTagMaster UserTagMaster { get; init; }

    public required AttributeMaster AttributeMaster { get; init; }

    public required IReadOnlyDictionary<string, string> VideoTitles { get; init; }

    /// <summary>商品ごとの動画のリンク（説明文を落とす前に拾った物）。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<VideoLink>> Videos { get; init; }

    /// <summary>商品ごとの画像のファイル（手元にある物。画面のカードと同じ並べ方の材料）。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ImageFiles { get; init; }

    /// <summary>
    /// 絞り込みの条件の材料。画面と同じ物を渡す（同じ答えにするため）。
    /// ドライブ文字の読み替えはしない（読み替えの表を作るにはドライブを調べて書き込むので。記録の場所のまま照らす）。
    /// </summary>
    public SearchModuleContext ConditionContext(DateTimeOffset now) => new(
        () => Compatibility,
        Modifications,
        Recent,
        null,
        now)
    {
        Items = Items,
        FavoriteShops = FavoriteShops,
        UnreadUpdateKinds = itemId => UnreadUpdates.TryGetValue(itemId, out var unread) ? unread.Kinds : BoothChangeKind.None,
    };

    /// <summary>検索用の文字列を照らす間に取る錠（文字列は対象を初めて探すときに畳んで覚えるので、同時に畳み合わない）。</summary>
    internal object SearchGate { get; } = new();

    private Dictionary<string, SearchHaystack>? _haystacks;
    private SearchFacts? _facts;

    /// <summary>商品の検索用の文字列（画面と同じ作り方。商品名の読みは作らない＝造語変換は使わない）。<see cref="SearchGate"/> の中で呼ぶ。</summary>
    internal SearchHaystack HaystackOf(ItemRecord item)
    {
        _haystacks ??= Items.ToDictionary(entry => entry.Id, Haystack, StringComparer.Ordinal);
        return _haystacks[item.Id];
    }

    /// <summary>
    /// 検索用の文字列。本文（<see cref="SearchField.Main"/>）だけは、写しが説明文を落としているので、初めて探すときにファイルから読む。
    /// 読んだ本文は畳んだ形で覚える（同じ本文を何度も探すたびに読み直さない）。本文を探さなければ読まない
    /// </summary>
    private SearchHaystack Haystack(ItemRecord item)
        => new(field => SearchText.RawValues(field == SearchField.Main ? FullItem(item) : item, field), makeReadings: null) { Item = item };

    /// <summary>説明文まで持った記録（ファイルの今の中身。読めなければ写しの物）。</summary>
    private ItemRecord FullItem(ItemRecord item)
    {
        if (SharesItemCache)
        {
            return item;
        }

        try
        {
            // Core の ItemRepository の控えを通さない（説明文ごと控えて、写しで落とした分が戻ってしまう）
            return JsonStore.ReadAsync<ItemRecord>(Paths.ItemFile(item.Id)).GetAwaiter().GetResult() ?? item;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return item;
        }
    }

    /// <summary>検索欄の記法（<c>avatar:</c>・<c>has:update</c>）が見る事実。素体の索引は条件と同じ物を使う（同じ答えにするため）。</summary>
    internal SearchFacts Facts => _facts ??= new SearchFacts(() => Compatibility, () => Registry, UnreadUpdates.ContainsKey);

    /// <summary>商品のファイルの印（大きさと日時）と、説明文を落とした記録。変わっていないファイルは読み直さず、写しどうしで使い回す。</summary>
    private sealed record ItemSlot(long Length, DateTime LastWriteUtc, ItemRecord Item, IReadOnlyList<VideoLink> Videos);

    private IReadOnlyDictionary<string, ItemSlot> _slots = new Dictionary<string, ItemSlot>();

    /// <summary>保存先を全部読む。</summary>
    /// <param name="sharesItemCache">アプリと同じプロセスで、アプリの <paramref name="store"/> を使うか（<see cref="SharesItemCache"/>）。</param>
    public static Task<QuerySnapshot> LoadAsync(DataStore store, CancellationToken cancellationToken = default, bool sharesItemCache = false)
        => BuildAsync(store, previous: null, StoreChangeSet.Everything(), cancellationToken, sharesItemCache);

    /// <summary>
    /// 変わった部分だけを読み直した、新しい写しを作る（この写しは変えない。問い合わせの途中で半分だけ新しい答えを返さないため）。
    /// 商品は変わった部分に入っていれば一覧を見直し、大きさか日時が変わったファイルだけを読む（2,000 件でも見直すのは一覧だけ）。
    /// </summary>
    public Task<QuerySnapshot> UpdateAsync(StoreChangeSet changes, CancellationToken cancellationToken = default)
        => BuildAsync(_store, this, changes, cancellationToken, SharesItemCache);

    private static async Task<QuerySnapshot> BuildAsync(
        DataStore store, QuerySnapshot? previous, StoreChangeSet changes, CancellationToken cancellationToken, bool sharesItemCache)
    {
        bool Need(StorePart part) => previous is null || changes.Overflowed || changes.Parts.Contains(part);

        var (slots, failed, touched) = Need(StorePart.Items)
            ? await Task.Run(() => ScanItems(store, sharesItemCache, previous?._slots, cancellationToken), cancellationToken)
            : (previous!._slots, previous.FailedItemIds, (IReadOnlySet<string>)new HashSet<string>());
        // ファイル名と中の ID が食い違うと、同じ ID の商品が2つ見えることがある（手で写したファイル）。1つ目だけ使う
        var items = slots.Values.Select(slot => slot.Item)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        // 画像の一覧は、商品が変わった・画像が変わったと知らされた商品だけ見直す（2,000 件の商品の画像のフォルダを毎回並べない）
        Dictionary<string, IReadOnlyList<string>> images;
        if (previous is null || changes.Overflowed)
        {
            images = items.ToDictionary(item => item.Id, item => ListImages(store.Paths.ItemImagesDir(item.Id)), StringComparer.Ordinal);
        }
        else
        {
            images = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var relist = touched.Concat(changes.Parts.Contains(StorePart.Images) ? changes.ItemIds : []).ToHashSet(StringComparer.Ordinal);
            foreach (var item in items)
            {
                images[item.Id] = !relist.Contains(item.Id) && previous.ImageFiles.TryGetValue(item.Id, out var known)
                    ? known
                    : ListImages(store.Paths.ItemImagesDir(item.Id));
            }
        }

        var registry = Need(StorePart.Registry) ? store.Avatars.Load() : previous!.Registry;
        var shopNotes = Need(StorePart.ShopNotes) ? store.ShopNotes.Load() : previous!.ShopNotes;
        var notifications = Need(StorePart.Notifications) ? store.Notifications.Load() : previous!.Notifications;
        var recent = Need(StorePart.Recent) ? TimesOf(store.Recent.Load()) : previous!.Recent;
        // 改変の読み込みは、開けなかった・壊れたファイルを内側で受け止めて「読めなかった ID」に入れるだけで、投げない。
        // 黙って一覧から消すと、読み直しが成功したように見え、部分の印も確定して二度と読み直されない（点検31）。
        // 読めなかった改変は前の写しの記録を残し、この部分を読み終えていない物として知らせる（API は印を確定せず、次の見比べで読み直す）
        var incomplete = new HashSet<StorePart>();
        ModificationUsage modifications;
        if (Need(StorePart.Modifications))
        {
            var loaded = await store.Modifications.LoadAllAsync(cancellationToken);
            var records = loaded.Modifications.ToList();
            if (loaded.FailedIds.Count > 0)
            {
                incomplete.Add(StorePart.Modifications);
                var failedIds = loaded.FailedIds.ToHashSet(StringComparer.Ordinal);
                records.AddRange((previous?.Modifications.Records ?? [])
                    .Where(record => failedIds.Contains(record.Id) && records.All(read => read.Id != record.Id)));
            }

            modifications = ModificationUsage.From(records);
        }
        else
        {
            modifications = previous!.Modifications;
            if (previous.IncompleteParts.Contains(StorePart.Modifications))
            {
                incomplete.Add(StorePart.Modifications);
            }
        }
        var ownedItemIds = items.Where(item => item.IsOwned).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        return new QuerySnapshot(store, sharesItemCache)
        {
            _slots = slots,
            Items = items,
            ItemsById = items.ToDictionary(item => item.Id, StringComparer.Ordinal),
            FailedItemIds = failed,
            Registry = registry,
            Compatibility = previous is not null && ReferenceEquals(registry, previous.Registry) ? previous.Compatibility : AvatarCompatibilityIndex.Build(registry),
            AvatarNames = Chmonos.Core.Services.AvatarNames.Map(registry.Entries),
            OwnedAvatarIds = registry.Entries
                .Where(entry => entry.IsOwnedManually || ownedItemIds.Contains(entry.ItemId))
                .Select(entry => entry.ItemId)
                .ToHashSet(StringComparer.Ordinal),
            UnreadUpdates = previous is not null && ReferenceEquals(notifications, previous.Notifications)
                ? previous.UnreadUpdates
                : Chmonos.Core.Services.UnreadUpdates.Of(notifications),
            Notifications = notifications,
            Modifications = modifications,
            IncompleteParts = incomplete,
            Recent = recent,
            ShopNotes = shopNotes,
            FavoriteShops = Chmonos.Core.Services.ShopNotes.FavoriteKeys(shopNotes),
            Settings = Need(StorePart.Settings) ? store.Settings.Load() : previous!.Settings,
            UserTagMaster = Need(StorePart.UserTags) ? store.UserTags.Load() : previous!.UserTagMaster,
            AttributeMaster = Need(StorePart.Attributes) ? store.Attributes.Load() : previous!.AttributeMaster,
            VideoTitles = Need(StorePart.VideoTitles)
                ? store.VideoTitles.Load()
                    .GroupBy(record => record.VideoId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last().Title, StringComparer.Ordinal)
                : previous!.VideoTitles,
            Videos = slots.Values.GroupBy(slot => slot.Item.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Videos, StringComparer.Ordinal),
            ImageFiles = images,
        };
    }

    /// <summary>
    /// 商品のファイルを並べ、前の写しと大きさか日時が違うファイルだけを読む。
    ///
    /// **Core の <c>ItemRepository</c> の控えを使わない**（2026-10-12）：あちらは読んだ記録を説明文ごと控えるので、
    /// 写しで説明文を落としても、控えの側に全部が残る。ここでは印と説明文を落とした記録だけを持つ。
    /// 読めないファイル（壊れた JSON）は読めなかった商品として知らせ、書きかけで開けないファイルは前の記録のまま残す（一瞬消えて見えないように）。
    /// </summary>
    /// <remarks>
    /// アプリと同じプロセスで動くとき（<paramref name="sharesItemCache"/>）だけは控えを通し、説明文も落とさない：画面が同じ記録を控えに持っているので、
    /// 同じ実体を指すだけで済む（変わっていないファイルは控えが同じ実体を返すので、変わった商品の見分けもそのまま効く）
    /// </remarks>
    private static (IReadOnlyDictionary<string, ItemSlot> Slots, IReadOnlyList<string> Failed, IReadOnlySet<string> Touched) ScanItems(
        DataStore store,
        bool sharesItemCache,
        IReadOnlyDictionary<string, ItemSlot>? previous,
        CancellationToken cancellationToken)
    {
        var paths = store.Paths;
        var slots = new Dictionary<string, ItemSlot>(StringComparer.Ordinal);
        var failed = new List<string>();
        var touched = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(paths.ItemsDir))
        {
            return (slots, failed, touched);
        }

        foreach (var file in new DirectoryInfo(paths.ItemsDir).EnumerateFiles("*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = Path.GetFileNameWithoutExtension(file.Name);
            if (!StoreIds.IsItemId(fileId))
            {
                continue;
            }

            if (previous is not null && previous.TryGetValue(fileId, out var known)
                && known.Length == file.Length && known.LastWriteUtc == file.LastWriteTimeUtc)
            {
                slots[fileId] = known;
                continue;
            }

            try
            {
                var record = sharesItemCache
                    ? store.Items.LoadAsync(fileId, cancellationToken).GetAwaiter().GetResult()
                    : JsonStore.ReadAsync<ItemRecord>(file.FullName, cancellationToken).GetAwaiter().GetResult();

                // 中の ID が形を外れた商品は外す（画面と同じ。中の ID で画像や説明の場所を組むので）
                if (record is null || !StoreIds.IsItemId(record.Id))
                {
                    failed.Add(fileId);
                    continue;
                }

                slots[fileId] = new ItemSlot(
                    file.Length,
                    file.LastWriteTimeUtc,
                    sharesItemCache ? record : record with { Booth = record.Booth with { Description = null, H2Sections = [] } },
                    VideoLinks.Find(record.Booth));
                touched.Add(record.Id);
            }
            catch (System.Text.Json.JsonException)
            {
                failed.Add(fileId);
            }
            catch (IOException) when (previous is not null && previous.TryGetValue(fileId, out var kept))
            {
                slots[fileId] = kept;
            }
            catch (IOException)
            {
                // 初めて読むときに開けなかった（ほかのアプリが書いている途中）。次の読み直しで入る
            }
        }

        // 消えた商品も、画像の一覧を見直す相手に入れる（写しから外れたことを知らせるため）
        if (previous is not null)
        {
            touched.UnionWith(previous.Keys.Where(id => !slots.ContainsKey(id)).Select(id => previous[id].Item.Id));
        }

        return (slots, failed, touched);
    }

    private static RecentTimes TimesOf(RecentLog log)
        => new(
            RecentActivity.Times(log.Entries, RecentKind.Added),
            RecentActivity.Times(log.Entries, RecentKind.Used),
            RecentActivity.Times(log.Entries, RecentKind.Viewed));

    /// <summary>この写しの後で変わった商品（外への知らせに使う。前の写しと記録の参照が違う物・増えた物・消えた物）。</summary>
    public IReadOnlyList<string> ChangedItemsSince(QuerySnapshot previous)
        => ItemsById.Where(pair => !previous.ItemsById.TryGetValue(pair.Key, out var before) || !ReferenceEquals(before, pair.Value))
            .Select(pair => pair.Key)
            .Concat(previous.ItemsById.Keys.Where(id => !ItemsById.ContainsKey(id)))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>画像のフォルダの WebP（名前の順）。読めないフォルダは空として扱う（画像が無いだけで答えは返す）。</summary>
    private static IReadOnlyList<string> ListImages(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.webp").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
