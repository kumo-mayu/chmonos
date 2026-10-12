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

    private QuerySnapshot(DataStore store)
    {
        _store = store;
        Paths = store.Paths;
    }

    public AppPaths Paths { get; }

    /// <summary>
    /// 読めた商品の全部（ID の順）。**説明文（本文と見出しごとの文）は落としてある**（2026-10-12・ユーザ判断）：
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
        try
        {
            return _store.Items.LoadAsync(item.Id).GetAwaiter().GetResult() ?? item;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return item;
        }
    }

    /// <summary>検索欄の記法（<c>avatar:</c>・<c>has:update</c>）が見る事実。素体の索引は条件と同じ物を使う（同じ答えにするため）。</summary>
    internal SearchFacts Facts => _facts ??= new SearchFacts(() => Compatibility, () => Registry, UnreadUpdates.ContainsKey);

    public static async Task<QuerySnapshot> LoadAsync(DataStore store, CancellationToken cancellationToken = default)
    {
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var full = loaded.Items.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
        var videos = full.ToDictionary(item => item.Id, item => VideoLinks.Find(item.Booth), StringComparer.Ordinal);
        var items = full.Select(item => item with { Booth = item.Booth with { Description = null, H2Sections = [] } }).ToList();
        var registry = store.Avatars.Load();
        var ownedItemIds = items.Where(item => item.IsOwned).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var shopNotes = store.ShopNotes.Load();
        var recent = store.Recent.Load();
        var notifications = store.Notifications.Load();

        return new QuerySnapshot(store)
        {
            Items = items,
            ItemsById = items.ToDictionary(item => item.Id, StringComparer.Ordinal),
            FailedItemIds = loaded.FailedItemIds,
            Registry = registry,
            Compatibility = AvatarCompatibilityIndex.Build(registry),
            AvatarNames = Chmonos.Core.Services.AvatarNames.Map(registry.Entries),
            OwnedAvatarIds = registry.Entries
                .Where(entry => entry.IsOwnedManually || ownedItemIds.Contains(entry.ItemId))
                .Select(entry => entry.ItemId)
                .ToHashSet(StringComparer.Ordinal),
            UnreadUpdates = Chmonos.Core.Services.UnreadUpdates.Of(notifications),
            Notifications = notifications,
            Modifications = ModificationUsage.From((await store.Modifications.LoadAllAsync(cancellationToken)).Modifications),
            Recent = new RecentTimes(
                RecentActivity.Times(recent.Entries, RecentKind.Added),
                RecentActivity.Times(recent.Entries, RecentKind.Used),
                RecentActivity.Times(recent.Entries, RecentKind.Viewed)),
            ShopNotes = shopNotes,
            FavoriteShops = Chmonos.Core.Services.ShopNotes.FavoriteKeys(shopNotes),
            Settings = store.Settings.Load(),
            UserTagMaster = store.UserTags.Load(),
            AttributeMaster = store.Attributes.Load(),
            VideoTitles = store.VideoTitles.Load()
                .GroupBy(record => record.VideoId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Title, StringComparer.Ordinal),
            Videos = videos,
            ImageFiles = items.ToDictionary(item => item.Id, item => ListImages(store.Paths.ItemImagesDir(item.Id)), StringComparer.Ordinal),
        };
    }

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
