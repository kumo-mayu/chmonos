using System.Net;
using System.Text.Json;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 何か所も順に書く操作（IDの変更・タグや属性の名前の変更）を、やりかけの記録（pending-operations.json）で囲み、
/// 途中で止まったら次の起動で続きを済ませる（ユーザ判断 2026-10-06「A」）。
///
/// 途中で止まった様子は、記録を残したまま、手元のファイルをその段まで書いた形に作って再現する。
/// 続きはどの段も2回当てても同じ結果になる作りなので、同じ記録を2度当てて増えないことも見る。
/// </summary>
public sealed class PendingOperationTests : IDisposable
{
    private const string FromId = "local-9a8b7c6d";
    private const string ToId = "9900101";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-pending-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private readonly ItemService _items;
    private readonly NotificationService _notifications;
    private readonly SettingsService _settings;
    private readonly ThrowingAttributes _attributes;
    private readonly CountingHandler _http = new();

    public PendingOperationTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_http), settings, TestWait.None);
        _items = new ItemService(_store, client, new ImagePipeline(client, paths, settings), settings);
        _notifications = new NotificationService(_store);
        _settings = new SettingsService(_store);
        _attributes = new ThrowingAttributes(new AttributeService(_store));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>BOOTHへ行ったら数える（続きは問い合わせないことを見る）。どのIDも「無い」と答える。</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>名前の変更だけを、言われた間は落とす（続きの失敗を作る）。ほかは本物へ渡す。</summary>
    private sealed class ThrowingAttributes(IAttributeService inner) : IAttributeService
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<AttributeUsage>> LoadUsageAsync(CancellationToken cancellationToken = default)
            => inner.LoadUsageAsync(cancellationToken);

        public Task<IReadOnlyList<OrphanAttribute>> LoadOrphansAsync(CancellationToken cancellationToken = default)
            => inner.LoadOrphansAsync(cancellationToken);

        public Task<AttributeMergePreview> PreviewMergeAsync(string from, string to, CancellationToken cancellationToken = default)
            => inner.PreviewMergeAsync(from, to, cancellationToken);

        public Task<AttributeEditResult> RenameAsync(
            string oldName,
            string newName,
            AttributeMergeValue keep = AttributeMergeValue.KeepTarget,
            CancellationToken cancellationToken = default)
            => Fail
                ? throw new IOException("試験で落とす")
                : inner.RenameAsync(oldName, newName, keep, cancellationToken);

        public Task<RenameFingerprint> FingerprintRenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
            => inner.FingerprintRenameAsync(oldName, newName, cancellationToken);

        public Task<AttributeEditResult> DeleteAsync(string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(name, cancellationToken);

        public Task<AttributeMaster> SetDefaultAsync(string name, bool isDefault, CancellationToken cancellationToken = default)
            => inner.SetDefaultAsync(name, isDefault, cancellationToken);

        public Task<AttributeMaster> SetMemoAsync(string name, string? memo, CancellationToken cancellationToken = default)
            => inner.SetMemoAsync(name, memo, cancellationToken);

        public Task<AttributeMaster> ReorderAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default)
            => inner.ReorderAsync(names, cancellationToken);
    }

    /// <summary>起動ごとに作り直す物（この起動で走っている記録の控えを持ち越さない）。</summary>
    private PendingOperationRunner Runner()
        => new(_store.PendingOperations, _items, new UserTagService(_store), _attributes, _settings, _notifications);

    private async Task<PendingOperation> LeaveRecordAsync(PendingOperation operation)
    {
        var recorded = operation with { Id = Guid.NewGuid().ToString("N"), StartedAt = DateTimeOffset.Now };
        await _store.PendingOperations.UpdateAsync(list => [.. list, recorded]);
        return recorded;
    }

    private List<PendingOperation> Records() => _store.PendingOperations.Load();

    /// <summary>やりかけの記録のファイルの場所にフォルダを置き、書けなくする。</summary>
    private void BlockJournal() => Directory.CreateDirectory(_store.PendingOperations.Path);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonStore.Options);

    private static ItemRecord Item(string id, LocalBlock local) => new() { Id = id, Booth = new BoothBlock(), Local = local };

    private static LocalFileRecord File(string hash) => new() { Hash = hash, Paths = [$@"C:\dl\{hash}.zip"], SizeBytes = 100 };

    // ---- IDの変更 ----

    /// <summary>
    /// 商品の JSON を、読めるが消せも置き換えもできないように開いておく。その商品を消す・書く所で落ちたのと同じ様子を、
    /// 本物の操作の途中で作る（記録の指紋も本物の段で書かれる）。
    /// </summary>
    private FileStream HoldFile(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private FileStream HoldItemFile(string itemId) => HoldFile(_store.Paths.ItemFile(itemId));

    /// <summary>移す先へ書いた後・元を消す前（①と②の間）で落ちた様子を、本物の操作で作る。</summary>
    private async Task CrashAfterWritingTargetAsync(IReadOnlySet<int>? skipped = null)
    {
        using (HoldItemFile(FromId))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Runner().ChangeItemIdAsync(FromId, ToId, skipped, CancellationToken.None));
        }
    }

    /// <summary>移す先へ書く直前（指紋は書いた・移す先は書けなかった）で落ちた様子を、本物の操作で作る。</summary>
    private async Task CrashBeforeWritingTargetAsync(IReadOnlySet<int>? skipped = null)
    {
        using (HoldItemFile(ToId))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Runner().ChangeItemIdAsync(FromId, ToId, skipped, CancellationToken.None));
        }
    }

    private static IEnumerable<int> Prices(LocalBlock local) => local.Purchases.Select(purchase => purchase.Price!.Value).Order();

    /// <summary>元を消した後（②の後）で止まると、改変・登録簿・ほかの商品の対応アバター・知らせ・足跡が古いIDを指したまま残っていた。</summary>
    [Fact]
    public async Task IDの変更_元を消した後で止まっても_次の起動でほかの参照が移る()
    {
        var target = new LocalBlock { LocalFiles = [File("aaa")] };
        await _store.Items.SaveAsync(Item(ToId, target));
        await _store.Items.SaveAsync(Item("9900102", new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = FromId }] }));
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-99001",
            AvatarItemId = FromId,
            Name = "普段着",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            Members = [new ModificationMember { ItemId = FromId }],
        });
        await _store.Avatars.SaveAsync(new AvatarRegistry { Entries = [new AvatarRegistryEntry { ItemId = FromId }] });
        await _store.Notifications.SaveAsync(
        [
            new NotificationRecord { Id = "n1", Kind = NotificationKind.ItemUpdated, ItemId = FromId, Title = "t", Detail = "d", CreatedAt = DateTimeOffset.Now },
        ]);

        // 移す先へ書いた後の指紋まで書かれた記録（元はもう無い）
        var written = OperationFingerprint.Of(target);
        await LeaveRecordAsync(new PendingOperation
        {
            Kind = PendingOperationKind.ChangeItemId,
            FromId = FromId,
            ToId = ToId,
            SourceFingerprint = OperationFingerprint.Of(new LocalBlock()),
            TargetFingerprint = OperationFingerprint.Of(new LocalBlock()),
            MergedFingerprint = written,

            // 始めた時に元のIDを指していた参照（本物の操作が1回目の記録に書く物）
            References = new ItemIdReferences { Modifications = ["mod-99001"], RegistryEntry = true, LinkingItems = ["9900102"] },
        });

        Assert.Equal(1, await Runner().ResumeAsync());

        var modification = await _store.Modifications.LoadAsync("mod-99001");
        Assert.Equal(ToId, modification!.AvatarItemId);
        Assert.Equal([ToId], modification.Members.Select(member => member.ItemId));
        Assert.Equal(ToId, Assert.Single(_store.Avatars.Load().Entries).ItemId);
        Assert.Equal(ToId, (await _store.Items.LoadAsync("9900102"))!.Local.Avatars.Single().AvatarItemId);
        Assert.Equal(ToId, Assert.Single(_store.Notifications.Load()).ItemId);
        Assert.Empty(Records());
        Assert.Equal(0, _http.Requests);
    }

    /// <summary>
    /// 移す先へ書いた後・元を消す前（①と②の間）で止まると、移す先は合わせ済みで元も残る。
    /// もう一度合わせると購入記録が2回分になり、メモが2回つながっていた。指紋で「合わせ済み」と読み、元を消すだけにする。
    /// </summary>
    [Fact]
    public async Task IDの変更_移す先へ書いて元を消す前に止まっても_購入記録とメモが増えない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock
        {
            Memo = "元のメモ",
            LocalFiles = [File("aaa")],
            Purchases = [new Purchase { Price = 500 }, new Purchase { Price = 500 }, new Purchase { Price = 800, Kind = PurchaseKind.Given }],
        }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Memo = "先のメモ", Purchases = [new Purchase { Price = 1000 }] }));

        await CrashAfterWritingTargetAsync(new HashSet<int> { 2 });
        Assert.NotNull(await _store.Items.LoadAsync(FromId));
        Assert.Equal([500, 500, 1000], Prices((await _store.Items.LoadAsync(ToId))!.Local));

        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        var moved = (await _store.Items.LoadAsync(ToId))!.Local;
        Assert.Equal([500, 500, 1000], Prices(moved));
        Assert.Equal("先のメモ\n\n元のメモ", moved.Memo);
        Assert.Equal(["aaa"], moved.LocalFiles.Select(file => file.Hash));
        Assert.Empty(Records());
        Assert.Equal(0, _http.Requests);
    }

    /// <summary>
    /// 移す先が操作の前から同じ値の購入を持っていて、移す先へ書く前に止まった。前は値で照らして「合わせ済み」と読み、
    /// 元の購入を持って行かずに元を消していた（購入が1件落ちる。外部の点検 2026-10-06・L108）。
    /// </summary>
    [Fact]
    public async Task IDの変更_同じ値の購入を先が前から持っていても_書く前に止まった続きで2件残る()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));

        await CrashBeforeWritingTargetAsync();
        Assert.Equal([500], Prices((await _store.Items.LoadAsync(ToId))!.Local));

        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        var moved = (await _store.Items.LoadAsync(ToId))!.Local;
        Assert.Equal([500, 500], Prices(moved));
        Assert.Equal("元のメモ\n\n元のメモ", moved.Memo);
        Assert.Empty(Records());
    }

    /// <summary>同じ値の購入を前から持つ移す先で、書いた後に止まった続きも2件のまま（3件にしない）。</summary>
    [Fact]
    public async Task IDの変更_同じ値の購入を先が前から持っていても_書いた後に止まった続きで2件のまま()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Purchases = [new Purchase { Price = 500 }] }));

        await CrashAfterWritingTargetAsync();
        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        Assert.Equal([500, 500], Prices((await _store.Items.LoadAsync(ToId))!.Local));
        Assert.Empty(Records());
    }

    /// <summary>①の前で止まって移す先が既にあった（合わせていない）ときは、ふつうに合わせて済ませる。</summary>
    [Fact]
    public async Task IDの変更_移す先へ書く前に止まっていれば_続きでふつうに合わせる()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Purchases = [new Purchase { Price = 1000 }] }));

        await CrashBeforeWritingTargetAsync();
        var left = Assert.Single(Records());
        Assert.NotNull(left.MergedFingerprint);

        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        var moved = (await _store.Items.LoadAsync(ToId))!.Local;
        Assert.Equal([500, 1000], Prices(moved));
        Assert.Equal("元のメモ", moved.Memo);
        Assert.Empty(Records());
    }

    /// <summary>移す先へ書いた後で、人が移す先を触っていたら当てない（元も消さない）。知らせに出し、記録は消す。</summary>
    [Fact]
    public async Task IDの変更_止まった後に移す先を触っていたら当てずに知らせる()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ" }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock()));
        await CrashAfterWritingTargetAsync();
        await _store.Items.ChangeLocalAsync(ToId, local => local with { Memo = "後で書いたメモ" }, [LocalField.Memo]);

        await Runner().ResumeAsync();

        Assert.Equal("元のメモ", (await _store.Items.LoadAsync(FromId))!.Local.Memo);
        Assert.Equal("後で書いたメモ", (await _store.Items.LoadAsync(ToId))!.Local.Memo);
        Assert.Empty(Records());
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(PendingOperationRunner.ChangedDetail, notice.Detail);
        Assert.Equal(FromId, notice.ItemId);
    }

    [Fact]
    public async Task IDの変更_同じ記録を2度当てても変わらない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Memo = "先のメモ", Purchases = [new Purchase { Price = 1000 }] }));
        await _store.Items.SaveAsync(Item("9900102", new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = FromId }] }));

        await CrashBeforeWritingTargetAsync();
        var left = Assert.Single(Records());
        await Runner().ResumeAsync();
        var once = Json((await _store.Items.LoadAllAsync()).Items.Select(item => item.Local).ToList());

        await _store.PendingOperations.UpdateAsync(list => [.. list, left]);
        await Runner().ResumeAsync();
        var twice = Json((await _store.Items.LoadAllAsync()).Items.Select(item => item.Local).ToList());

        Assert.Equal(once, twice);
        Assert.Empty(Records());
    }

    /// <summary>移す先へ何も書く前（指紋も書く前）に止まった物は、元のままなので記録を消すだけ。BOOTHへも行かない。</summary>
    [Fact]
    public async Task IDの変更_書き始める前に止まっていれば_元のまま記録を消す()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ" }));
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.ChangeItemId, FromId = FromId, ToId = ToId });

        await Runner().ResumeAsync();

        Assert.Equal("元のメモ", (await _store.Items.LoadAsync(FromId))!.Local.Memo);
        Assert.Null(await _store.Items.LoadAsync(ToId));
        Assert.Empty(Records());
        Assert.Equal(0, _http.Requests);
    }

    [Fact]
    public async Task IDの変更_最後まで済めば記録は残らない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ" }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock()));

        Assert.Equal(ItemIdChangeOutcome.Moved, await Runner().ChangeItemIdAsync(FromId, ToId, null, CancellationToken.None));

        Assert.Null(await _store.Items.LoadAsync(FromId));
        Assert.Empty(Records());
        Assert.True(System.IO.File.Exists(_store.PendingOperations.Path));
        Assert.Empty(_store.Notifications.Load());
    }

    [Fact]
    public async Task IDの変更_記録を書けなければ始めない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ" }));
        BlockJournal();

        Assert.Equal(ItemIdChangeOutcome.NotRecorded, await Runner().ChangeItemIdAsync(FromId, ToId, null, CancellationToken.None));

        Assert.NotNull(await _store.Items.LoadAsync(FromId));
        Assert.Null(await _store.Items.LoadAsync(ToId));
        Assert.Equal(0, _http.Requests);
    }

    /// <summary>
    /// 済んだ記録を消せなかったら済んだ扱いにせず知らせる。前は済んだ扱いで返し、残った記録（ID だけ）が、後で同じ ID で
    /// 登録し直した商品に次の起動で当たり、その商品を移す先へ合わせて消していた（外部の点検 2026-10-06・L108）。
    /// </summary>
    [Fact]
    public async Task IDの変更_記録を消せずに残った後で同じIDの商品を登録し直しても_次の起動で古い操作が当たらない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Purchases = [new Purchase { Price = 1000 }] }));
        await CrashAfterWritingTargetAsync();
        var left = Assert.Single(Records());

        // 続きは済むが、記録を消せない（記録のファイルを読めるが書けないように開いておく）
        using (HoldFile(_store.PendingOperations.Path))
        {
            await Runner().ResumeAsync();
        }

        Assert.Null(await _store.Items.LoadAsync(FromId));
        Assert.Equal(left.Id, Assert.Single(Records()).Id);
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(PendingOperationRunner.NotRemovedDetail, notice.Detail);
        Assert.False(notice.IsResolved);

        // 同じ ID で登録し直す
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "登録し直した", LocalFiles = [File("bbb")] }));
        var targetBefore = Json((await _store.Items.LoadAsync(ToId))!.Local);

        await Runner().ResumeAsync();

        Assert.Equal("登録し直した", (await _store.Items.LoadAsync(FromId))!.Local.Memo);
        Assert.Equal(targetBefore, Json((await _store.Items.LoadAsync(ToId))!.Local));
        Assert.Empty(Records());
        Assert.Equal(PendingOperationRunner.ChangedDetail, Assert.Single(_store.Notifications.Load()).Detail);
    }

    // ---- タグの名前の変更・統合 ----

    private static UserTagTop Top(string name, params string[] subs)
        => new() { Name = name, Subs = subs.Select(sub => new UserTagSub { Name = sub }).ToList() };

    private static LocalBlock Tagged(params UserTagAssignment[] tags) => new() { UserTags = tags };

    private async Task SaveSearchAsync(SearchModuleState module)
        => await _store.SavedSearches.SaveAsync(new SavedSearchList { Entries = [new SearchHistoryEntry { Name = "保存", Modules = [module] }] });

    private SearchModuleState SavedModule() => _store.SavedSearches.Load().Entries.Single().Modules.Single();

    /// <summary>今の保存先の様子で、始める前の指紋を付ける（本物の操作が記録に書くのと同じ物）。</summary>
    private async Task<PendingOperation> StartedAsync(PendingOperation operation)
    {
        var fingerprint = operation.Kind == PendingOperationKind.RenameUserTag
            ? await new UserTagService(_store).FingerprintRenameAsync(operation.Top!, operation.Sub, operation.NewName!)
            : await _attributes.FingerprintRenameAsync(operation.OldName!, operation.NewName!);
        return operation with { MasterFingerprint = fingerprint.Master, Holders = fingerprint.Holders };
    }

    /// <summary>一覧を書いた後・商品を途中まで書き換えたところで止まると、残りの商品と保存した検索が古い名前のまま残っていた。</summary>
    [Fact]
    public async Task タグの名前の変更_商品の途中で止まっても_次の起動で残りの商品と保存した検索が新しい名前になる()
    {
        // 始める前
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装", Subs = ["夏"] })));
        await _store.Items.SaveAsync(Item("9900112", Tagged(new UserTagAssignment { Top = "衣装", Subs = ["夏"] })));
        await SaveSearchAsync(new SearchModuleState { Kind = "UserTag", UserTags = [new UserTagCondition { Top = "衣装" }] });
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", NewName = "服" });

        // 一覧と1件目まで書いたところで止まった
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("服", "夏")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "服", Subs = ["夏"] })));
        await LeaveRecordAsync(operation);

        await Runner().ResumeAsync();

        Assert.Equal(["服"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Equal("服", (await _store.Items.LoadAsync("9900112"))!.Local.UserTags.Single().Top);
        Assert.Equal("服", SavedModule().UserTags.Single().Top);
        Assert.Empty(Records());
        Assert.Empty(_store.Notifications.Load());
    }

    [Fact]
    public async Task タグの統合_同じ記録を2度当てても変わらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "衣装", Memo = "元", Subs = [new UserTagSub { Name = "夏" }] }, Top("服", "冬")],
        });
        await _store.Items.SaveAsync(Item("9900111", Tagged(
            new UserTagAssignment { Top = "衣装", Subs = ["夏"] },
            new UserTagAssignment { Top = "服", Subs = ["冬"] })));
        await SaveSearchAsync(new SearchModuleState
        {
            Kind = "UserTag",
            UserTags = [new UserTagCondition { Top = "衣装", Subs = ["夏"] }, new UserTagCondition { Top = "服", Subs = ["冬"] }],
        });
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", NewName = "服" });

        Assert.NotNull(await Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None));
        var once = Json((_store.UserTags.Load(), (await _store.Items.LoadAsync("9900111"))!.Local, _store.SavedSearches.Load()));

        await LeaveRecordAsync(operation);
        await Runner().ResumeAsync();
        var twice = Json((_store.UserTags.Load(), (await _store.Items.LoadAsync("9900111"))!.Local, _store.SavedSearches.Load()));

        Assert.Equal(once, twice);
        Assert.Equal(["服"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Empty(Records());
    }

    [Fact]
    public async Task 小分類の名前の変更_一覧を書いた後で止まっても_次の起動で商品と保存した検索が新しい名前になる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装", Subs = ["夏"] })));
        await SaveSearchAsync(new SearchModuleState { Kind = "UserTag", UserTags = [new UserTagCondition { Top = "衣装", Subs = ["夏"] }] });
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", Sub = "夏", NewName = "サマー" });
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "サマー")] });
        await LeaveRecordAsync(operation);

        await Runner().ResumeAsync();

        Assert.Equal(["サマー"], (await _store.Items.LoadAsync("9900111"))!.Local.UserTags.Single().Subs);
        Assert.Equal(["サマー"], SavedModule().UserTags.Single().Subs);
        Assert.Empty(Records());
    }

    /// <summary>
    /// 済んだ記録を消せずに残った後で、同じ名前のタグを作り直して商品に付けた。前は次の起動で古い操作が当たり、
    /// 作り直したタグまで新しい名前へ寄せていた（外部の点検 2026-10-06・L108）。
    /// </summary>
    [Fact]
    public async Task タグの名前の変更_記録を消せずに残った後で同じ名前のタグを作り直しても_次の起動で当たらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装" })));

        // 商品を書き換える所で待たせ、その間に記録のファイルを書けなくしておく（済んだ後で消せない）
        var gate = await HoldItemAsync("9900111");
        var running = Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None);
        await WaitUntilAsync(() => Records().Count == 1 && _store.UserTags.Load().Tops.Any(top => top.Name == "服"));
        using (HoldFile(_store.PendingOperations.Path))
        {
            gate.SetResult();
            Assert.NotNull(await running);
        }

        Assert.Equal("服", (await _store.Items.LoadAsync("9900111"))!.Local.UserTags.Single().Top);
        Assert.Single(Records());
        Assert.Equal(PendingOperationRunner.NotRemovedDetail, Assert.Single(_store.Notifications.Load()).Detail);

        // 同じ名前を作り直して、別の商品に付ける
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("服"), Top("衣装")] });
        await _store.Items.SaveAsync(Item("9900112", Tagged(new UserTagAssignment { Top = "衣装" })));

        await Runner().ResumeAsync();

        Assert.Equal(["服", "衣装"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Equal("衣装", (await _store.Items.LoadAsync("9900112"))!.Local.UserTags.Single().Top);
        Assert.Empty(Records());
        Assert.Equal(PendingOperationRunner.ChangedDetail, Assert.Single(_store.Notifications.Load()).Detail);
    }

    /// <summary>作り直した名前を一覧には足さず、商品にだけ付けた（一覧に無い名前）場合も当てない。</summary>
    [Fact]
    public async Task タグの名前の変更_残った記録の後で同じ名前を商品にだけ付けても_次の起動で当たらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装" })));
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", NewName = "服" });
        Assert.NotNull(await Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None));
        await LeaveRecordAsync(operation);

        await _store.Items.SaveAsync(Item("9900112", Tagged(new UserTagAssignment { Top = "衣装" })));
        await _store.Items.ChangeLocalAsync(
            "9900111",
            local => local with { UserTags = [.. local.UserTags, new UserTagAssignment { Top = "衣装" }] },
            LocalOwners.UserTags);

        await Runner().ResumeAsync();

        Assert.Equal("衣装", (await _store.Items.LoadAsync("9900112"))!.Local.UserTags.Single().Top);
        Assert.Equal(["服", "衣装"], (await _store.Items.LoadAsync("9900111"))!.Local.UserTags.Select(tag => tag.Top));
        Assert.Empty(Records());
    }

    [Fact]
    public async Task タグの名前の変更_記録を書けなければ始めない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        BlockJournal();

        Assert.Null(await Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None));
        Assert.Equal(["衣装"], _store.UserTags.Load().Tops.Select(top => top.Name));
    }

    [Fact]
    public async Task 命令の層_記録を書けなければ失敗として返し_一覧を書かない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        BlockJournal();
        var handler = new Commands.CommandHandler(null!, _items, userTags: new UserTagService(_store), settings: _settings)
        {
            PendingOperations = _store.PendingOperations,
        };

        var result = await handler.ExecuteAsync(new Commands.UiCommand.RenameUserTag("衣装", null, "服"));

        Assert.IsType<Commands.CommandResult.Failed>(result);
        Assert.Equal(["衣装"], _store.UserTags.Load().Tops.Select(top => top.Name));
    }

    // ---- 属性の名前の変更・統合 ----

    [Fact]
    public async Task 属性の名前の変更_一覧を書いた後で止まっても_次の起動で商品と設定と保存した検索が新しい名前になる()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        await _settings.UpdateAsync(current => current with { CardAttributes = ["質"] });
        await SaveSearchAsync(new SearchModuleState { Kind = "Attribute", Ranges = [new AttributeRange("質", 3, 5)] });
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" });
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await LeaveRecordAsync(operation);

        await Runner().ResumeAsync();

        Assert.Equal(4, (await _store.Items.LoadAsync("9900121"))!.Local.Attributes["質感"]);
        Assert.Equal(["質感"], _store.Settings.Load().CardAttributes);
        Assert.Equal("質感", SavedModule().Ranges.Single().Name);
        Assert.Empty(Records());
    }

    [Fact]
    public async Task 属性の統合_同じ記録を2度当てても変わらない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "質", Memo = "元" }, new AttributeDefinition { Name = "質感" }],
        });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4, ["質感"] = 2 } }));
        await _settings.UpdateAsync(current => current with { CardAttributes = ["質", "質感"] });
        var operation = await StartedAsync(new PendingOperation
        {
            Kind = PendingOperationKind.RenameAttribute,
            OldName = "質",
            NewName = "質感",
            Keep = AttributeMergeValue.UseSource,
        });

        Assert.NotNull(await Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.UseSource, CancellationToken.None));
        var once = Json((_store.Attributes.Load(), (await _store.Items.LoadAsync("9900121"))!.Local, _store.Settings.Load().CardAttributes));

        await LeaveRecordAsync(operation);
        await Runner().ResumeAsync();
        var twice = Json((_store.Attributes.Load(), (await _store.Items.LoadAsync("9900121"))!.Local, _store.Settings.Load().CardAttributes));

        Assert.Equal(once, twice);
        Assert.Equal(4, (await _store.Items.LoadAsync("9900121"))!.Local.Attributes["質感"]);
        Assert.Empty(Records());
    }

    /// <summary>済んだ後に残った記録は、同じ名前で作り直した属性（一覧・商品の値）に当てない。</summary>
    [Fact]
    public async Task 属性の名前の変更_残った記録の後で同じ名前の属性を作り直しても_次の起動で当たらない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" });
        Assert.NotNull(await Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.KeepTarget, CancellationToken.None));
        await LeaveRecordAsync(operation);

        await _store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "質感" }, new AttributeDefinition { Name = "質" }],
        });
        await _store.Items.SaveAsync(Item("9900122", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 2 } }));

        await Runner().ResumeAsync();

        Assert.Equal(["質感", "質"], _store.Attributes.Load().Attributes.Select(definition => definition.Name));
        Assert.Equal(2, (await _store.Items.LoadAsync("9900122"))!.Local.Attributes["質"]);
        Assert.Empty(Records());
        Assert.Equal(PendingOperationRunner.ChangedDetail, Assert.Single(_store.Notifications.Load()).Detail);
    }

    [Fact]
    public async Task 属性の名前の変更_記録を書けなければ始めない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        BlockJournal();

        Assert.Null(await Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.KeepTarget, CancellationToken.None));
        Assert.Equal(["質"], _store.Attributes.Load().Attributes.Select(definition => definition.Name));
    }

    // ---- 済んだのに消せずに残った記録（外部の点検 2026-10-06・L110） ----

    /// <summary>
    /// 元のIDがアバターで、改変・登録簿・ほかの商品の対応アバターから指されている様子を作り、IDの変更を最後まで走らせる。
    /// 終わり際（ほかの商品の対応アバターを書き換える所）で待たせ、その間に記録のファイルを書けなくするので、
    /// 操作は済むが、終えた印も書けず行も消せずに残る（記録の指紋と参照は本物の段で書かれる）。
    /// </summary>
    private async Task<PendingOperation> ChangeIdLeavingRecordAsync()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", LocalFiles = [File("aaa")] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock()));
        await _store.Items.SaveAsync(Item("9900102", new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = FromId }] }));
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-99001",
            AvatarItemId = FromId,
            Name = "普段着",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        });
        await _store.Avatars.SaveAsync(new AvatarRegistry { Entries = [new AvatarRegistryEntry { ItemId = FromId }] });

        var gate = await HoldItemAsync("9900102");
        var running = Runner().ChangeItemIdAsync(FromId, ToId, null, CancellationToken.None);
        await WaitUntilAsync(() => !System.IO.File.Exists(_store.Paths.ItemFile(FromId))
            && _store.Avatars.Load().Entries.Any(entry => entry.ItemId == ToId));
        using (HoldFile(_store.PendingOperations.Path))
        {
            gate.SetResult();
            Assert.Equal(ItemIdChangeOutcome.Moved, await running);
        }

        Assert.Equal(ToId, (await _store.Modifications.LoadAsync("mod-99001"))!.AvatarItemId);
        Assert.Equal(ToId, (await _store.Items.LoadAsync("9900102"))!.Local.Avatars.Single().AvatarItemId);
        var left = Assert.Single(Records());
        Assert.Null(left.FinishedAt);
        Assert.Equal(["mod-99001"], left.References!.Modifications);
        Assert.Equal(PendingOperationRunner.NotRemovedDetail, Assert.Single(_store.Notifications.Load()).Detail);
        return left;
    }

    /// <summary>終えた印は書けたが、行は消せなかった様子にする（印を書く所と消す所の間で落ちた）。</summary>
    private async Task MarkFinishedAsync(bool skipped = false)
        => await _store.PendingOperations.UpdateAsync(list => [.. list.Select(entry => entry with { FinishedAt = DateTimeOffset.Now, Skipped = skipped })]);

    /// <summary>元のIDを「持っていないアバター」として登録し直し、それを使う改変を作る（点検の手順）。</summary>
    private async Task RegisterAgainAsUnownedAvatarAsync()
    {
        await _store.Avatars.UpdateAsync(registry => new AvatarRegistry
        {
            DetectedAt = registry.DetectedAt,
            Entries = [.. registry.Entries, new AvatarRegistryEntry { ItemId = FromId, IsOwnedManually = false, Memo = "登録し直した" }],
            BaseGroups = registry.BaseGroups,
        });
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-99002",
            AvatarItemId = FromId,
            Name = "後で作った",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            Members = [new ModificationMember { ItemId = FromId }],
        });
    }

    [Fact]
    public async Task IDの変更_済んで終えた印だけ残った後で元のIDを登録し直して改変を作っても_次の起動で書き換わらず記録が消える()
    {
        await ChangeIdLeavingRecordAsync();
        await MarkFinishedAsync();
        await RegisterAgainAsUnownedAvatarAsync();

        // 始めた時に元のIDを指していた商品が、登録し直したアバターをもう一度指す（記録の参照に載っている商品）
        await _store.Items.ChangeLocalAsync(
            "9900102",
            local => local with { Avatars = [.. local.Avatars, new AvatarLink { AvatarItemId = FromId }] },
            LocalOwners.SupportedAvatars);

        Assert.Equal(1, await Runner().ResumeAsync());

        var made = (await _store.Modifications.LoadAsync("mod-99002"))!;
        Assert.Equal(FromId, made.AvatarItemId);
        Assert.Equal([FromId], made.Members.Select(member => member.ItemId));
        Assert.Equal([ToId, FromId], _store.Avatars.Load().Entries.Select(entry => entry.ItemId).Order(StringComparer.Ordinal));
        Assert.Equal([ToId, FromId], (await _store.Items.LoadAsync("9900102"))!.Local.Avatars.Select(link => link.AvatarItemId));
        Assert.Null(await _store.Items.LoadAsync(FromId));
        Assert.Empty(Records());
        Assert.True(Assert.Single(_store.Notifications.Load()).IsResolved);
        Assert.Equal(0, _http.Requests);
    }

    /// <summary>
    /// 終えた印さえ書けずに残った行（保存先に書けなかった）。次の起動では指紋で見分けて続きを当てるが、
    /// 参照は始めた時に記録した物だけを書き換えるので、後で作った改変と登録し直した登録簿の行には当たらない。
    /// </summary>
    [Fact]
    public async Task IDの変更_終えた印も書けずに残った後で元のIDを登録し直して改変を作っても_次の起動で書き換わらない()
    {
        await ChangeIdLeavingRecordAsync();
        await RegisterAgainAsUnownedAvatarAsync();

        Assert.Equal(1, await Runner().ResumeAsync());

        var made = (await _store.Modifications.LoadAsync("mod-99002"))!;
        Assert.Equal(FromId, made.AvatarItemId);
        Assert.Equal([FromId], made.Members.Select(member => member.ItemId));
        var registry = _store.Avatars.Load().Entries;
        Assert.Equal([ToId, FromId], registry.Select(entry => entry.ItemId).Order(StringComparer.Ordinal));
        Assert.Equal("登録し直した", registry.Single(entry => entry.ItemId == FromId).Memo);
        Assert.Empty(Records());
    }

    [Fact]
    public async Task タグの名前の変更_済んで終えた印だけ残った後で元の名前を同じ内容で作り直し同じ付け方に戻しても_次の起動で当たらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装" })));

        var gate = await HoldItemAsync("9900111");
        var running = Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None);
        await WaitUntilAsync(() => Records().Count == 1 && _store.UserTags.Load().Tops.Any(top => top.Name == "服"));
        using (HoldFile(_store.PendingOperations.Path))
        {
            gate.SetResult();
            Assert.NotNull(await running);
        }

        Assert.Single(Records());
        await MarkFinishedAsync();

        // 改名先を消し、元の名前を同じ内容で作り直し、同じ商品へ同じ付け方に戻す（一覧と付け方の指紋が始める前と同じになる）
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装" })));

        Assert.Equal(1, await Runner().ResumeAsync());

        Assert.Equal(["衣装"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Equal("衣装", (await _store.Items.LoadAsync("9900111"))!.Local.UserTags.Single().Top);
        Assert.Empty(Records());
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(PendingOperationRunner.NotRemovedDetail, notice.Detail);
        Assert.True(notice.IsResolved);
    }

    [Fact]
    public async Task 属性の名前の変更_済んで終えた印だけ残った後で元の名前を同じ内容で作り直し同じ値に戻しても_次の起動で当たらない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));

        var gate = await HoldItemAsync("9900121");
        var running = Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.KeepTarget, CancellationToken.None);
        await WaitUntilAsync(() => Records().Count == 1 && _store.Attributes.Load().Attributes.Any(definition => definition.Name == "質感"));
        using (HoldFile(_store.PendingOperations.Path))
        {
            gate.SetResult();
            Assert.NotNull(await running);
        }

        await MarkFinishedAsync();
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));

        Assert.Equal(1, await Runner().ResumeAsync());

        Assert.Equal(["質"], _store.Attributes.Load().Attributes.Select(definition => definition.Name));
        Assert.Equal(4, (await _store.Items.LoadAsync("9900121"))!.Local.Attributes["質"]);
        Assert.Empty(Records());
        Assert.True(Assert.Single(_store.Notifications.Load()).IsResolved);
    }

    /// <summary>当てずにやめた（記録した時から変わっていた）のに消せなかった行は、次の起動で消すが、「続きは行いませんでした」の知らせは残す。</summary>
    [Fact]
    public async Task 当てずにやめた印の行は_次の起動で当てずに消し_知らせは解消済みにしない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        var recorded = await LeaveRecordAsync(
            await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" }));
        await MarkFinishedAsync(skipped: true);
        await _notifications.AddAsync(new NotificationRecord
        {
            Id = PendingOperationRunner.NotificationPrefix + recorded.Id,
            Kind = NotificationKind.UnfinishedOperation,
            Title = "t",
            Detail = PendingOperationRunner.ChangedDetail,
            CreatedAt = DateTimeOffset.Now,
        });

        Assert.Equal(1, await Runner().ResumeAsync());

        // 指紋は始める前のままなので、印が無ければ当たる様子
        Assert.Equal(["質"], _store.Attributes.Load().Attributes.Select(definition => definition.Name));
        Assert.Empty(Records());
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(PendingOperationRunner.ChangedDetail, notice.Detail);
        Assert.False(notice.IsResolved);
    }

    [Fact]
    public async Task 終えた印と参照は_欄の名前で読める形で記録に書かれる()
    {
        await ChangeIdLeavingRecordAsync();
        await MarkFinishedAsync();

        var text = await System.IO.File.ReadAllTextAsync(_store.PendingOperations.Path);
        Assert.Contains("\"finishedAt\"", text);
        Assert.Contains("\"references\"", text);
        Assert.Contains("\"modifications\"", text);
        Assert.Contains("\"linkingItems\"", text);
        Assert.Contains("\"registryEntry\": true", text);
        Assert.DoesNotContain("\"skipped\"", text);
    }

    // ---- 続きの失敗 ----

    [Fact]
    public async Task 続きが落ちたら記録は残して知らせ_次に済んだら知らせを解消済みにする()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        var operation = await StartedAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" });
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        var recorded = await LeaveRecordAsync(operation);

        _attributes.Fail = true;
        Assert.Equal(1, await Runner().ResumeAsync());

        Assert.Equal(recorded.Id, Assert.Single(Records()).Id);
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(NotificationKind.UnfinishedOperation, notice.Kind);
        Assert.Equal(PendingOperationRunner.NotificationPrefix + recorded.Id, notice.Id);
        Assert.Equal(PendingOperationRunner.FailedDetail, notice.Detail);
        Assert.False(notice.IsResolved);

        _attributes.Fail = false;
        await Runner().ResumeAsync();

        Assert.Empty(Records());
        Assert.True(Assert.Single(_store.Notifications.Load()).IsResolved);
        Assert.Equal(4, (await _store.Items.LoadAsync("9900121"))!.Local.Attributes["質感"]);
    }

    /// <summary>操作の途中で例外が出たら記録は残る（次の起動で続ける）。</summary>
    [Fact]
    public async Task 操作の途中で落ちたら記録が残る()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        _attributes.Fail = true;

        await Assert.ThrowsAsync<IOException>(() => Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.KeepTarget, CancellationToken.None));

        var left = Assert.Single(Records());
        Assert.Equal(PendingOperationKind.RenameAttribute, left.Kind);
        Assert.Equal("質", left.OldName);
        Assert.Equal("質感", left.NewName);
    }

    /// <summary>この起動で走っている操作の記録は、続きを当てる所が拾わない（同じ操作を2本並べない）。</summary>
    [Fact]
    public async Task 走っている操作の記録は続きの対象にしない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock()));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock()));
        var runner = Runner();
        var handled = -1;

        // 錠を先に取っておき、操作を「記録を書いた後」で待たせる
        var gate = await HoldItemAsync(FromId);
        var running = runner.ChangeItemIdAsync(FromId, ToId, null, CancellationToken.None);
        await WaitUntilAsync(() => Records().Count == 1);
        handled = await runner.ResumeAsync();
        gate.SetResult();
        await running;

        Assert.Equal(0, handled);
        Assert.Empty(Records());
    }

    /// <summary>商品の錠を取ったまま待つ（その商品を書く操作を途中で止めておく）。</summary>
    private async Task<TaskCompletionSource> HoldItemAsync(string itemId)
    {
        var release = new TaskCompletionSource();
        var held = new TaskCompletionSource();
        _ = Task.Run(() => _store.Items.ChangeLocalAsync(
            itemId,
            local =>
            {
                held.SetResult();
                release.Task.GetAwaiter().GetResult();
                return null;
            },
            [LocalField.Memo]));
        await held.Task;
        return release;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
