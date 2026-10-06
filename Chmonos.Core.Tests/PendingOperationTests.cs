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

    /// <summary>元を消した後（②の後）で止まると、改変・登録簿・ほかの商品の対応アバター・知らせ・足跡が古いIDを指したまま残っていた。</summary>
    [Fact]
    public async Task IDの変更_元を消した後で止まっても_次の起動でほかの参照が移る()
    {
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { LocalFiles = [File("aaa")] }));
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
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.ChangeItemId, FromId = FromId, ToId = ToId });

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
    /// 前の合わせ方をもう一度当てると購入記録が2回分になり、メモが2回つながっていた。
    /// </summary>
    [Fact]
    public async Task IDの変更_移す先へ書いて元を消す前に止まっても_購入記録とメモが増えない()
    {
        var source = new LocalBlock
        {
            Memo = "元のメモ",
            LocalFiles = [File("aaa")],
            Purchases = [new Purchase { Price = 500 }, new Purchase { Price = 500 }, new Purchase { Price = 800, Kind = PurchaseKind.Given }],
        };
        var target = new LocalBlock { Memo = "先のメモ", Purchases = [new Purchase { Price = 1000 }] };
        var skipped = new HashSet<int> { 2 };

        await _store.Items.SaveAsync(Item(FromId, source));
        await _store.Items.SaveAsync(Item(ToId, ItemIdChange.Merge(source, target, skipped)));
        await LeaveRecordAsync(new PendingOperation
        {
            Kind = PendingOperationKind.ChangeItemId,
            FromId = FromId,
            ToId = ToId,
            SkippedPurchases = [2],
        });

        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        var moved = (await _store.Items.LoadAsync(ToId))!.Local;
        Assert.Equal([500, 500, 1000], moved.Purchases.Select(purchase => purchase.Price!.Value).Order());
        Assert.Equal("先のメモ\n\n元のメモ", moved.Memo);
        Assert.Equal(["aaa"], moved.LocalFiles.Select(file => file.Hash));
        Assert.Empty(Records());
    }

    /// <summary>①の前で止まって移す先が既にあった（合わせていない）ときは、ふつうに合わせて済ませる。</summary>
    [Fact]
    public async Task IDの変更_移す先へ書く前に止まっていれば_続きでふつうに合わせる()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Purchases = [new Purchase { Price = 1000 }] }));
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.ChangeItemId, FromId = FromId, ToId = ToId });

        await Runner().ResumeAsync();

        Assert.Null(await _store.Items.LoadAsync(FromId));
        var moved = (await _store.Items.LoadAsync(ToId))!.Local;
        Assert.Equal([500, 1000], moved.Purchases.Select(purchase => purchase.Price!.Value).Order());
        Assert.Equal("元のメモ", moved.Memo);
    }

    [Fact]
    public async Task IDの変更_同じ記録を2度当てても変わらない()
    {
        await _store.Items.SaveAsync(Item(FromId, new LocalBlock { Memo = "元のメモ", Purchases = [new Purchase { Price = 500 }] }));
        await _store.Items.SaveAsync(Item(ToId, new LocalBlock { Memo = "先のメモ", Purchases = [new Purchase { Price = 1000 }] }));
        await _store.Items.SaveAsync(Item("9900102", new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = FromId }] }));
        var operation = new PendingOperation { Kind = PendingOperationKind.ChangeItemId, FromId = FromId, ToId = ToId };

        await LeaveRecordAsync(operation);
        await Runner().ResumeAsync();
        var once = Json((await _store.Items.LoadAllAsync()).Items.Select(item => item.Local).ToList());

        await LeaveRecordAsync(operation);
        await Runner().ResumeAsync();
        var twice = Json((await _store.Items.LoadAllAsync()).Items.Select(item => item.Local).ToList());

        Assert.Equal(once, twice);
        Assert.Empty(Records());
    }

    /// <summary>移す先へ何も書く前（BOOTHから取っている間など）に止まった物は、元のままなので記録を消すだけ。BOOTHへも行かない。</summary>
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

    // ---- タグの名前の変更・統合 ----

    private static UserTagTop Top(string name, params string[] subs)
        => new() { Name = name, Subs = subs.Select(sub => new UserTagSub { Name = sub }).ToList() };

    private static LocalBlock Tagged(params UserTagAssignment[] tags) => new() { UserTags = tags };

    private async Task SaveSearchAsync(SearchModuleState module)
        => await _store.SavedSearches.SaveAsync(new SavedSearchList { Entries = [new SearchHistoryEntry { Name = "保存", Modules = [module] }] });

    private SearchModuleState SavedModule() => _store.SavedSearches.Load().Entries.Single().Modules.Single();

    /// <summary>一覧を書いた後・商品を途中まで書き換えたところで止まると、残りの商品と保存した検索が古い名前のまま残っていた。</summary>
    [Fact]
    public async Task タグの名前の変更_商品の途中で止まっても_次の起動で残りの商品と保存した検索が新しい名前になる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("服", "夏")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "服", Subs = ["夏"] })));
        await _store.Items.SaveAsync(Item("9900112", Tagged(new UserTagAssignment { Top = "衣装", Subs = ["夏"] })));
        await SaveSearchAsync(new SearchModuleState { Kind = "UserTag", UserTags = [new UserTagCondition { Top = "衣装" }] });
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", NewName = "服" });

        await Runner().ResumeAsync();

        Assert.Equal(["服"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Equal("服", (await _store.Items.LoadAsync("9900112"))!.Local.UserTags.Single().Top);
        Assert.Equal("服", SavedModule().UserTags.Single().Top);
        Assert.Empty(Records());
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

        Assert.NotNull(await Runner().RenameUserTagAsync("衣装", null, "服", CancellationToken.None));
        var once = Json((_store.UserTags.Load(), (await _store.Items.LoadAsync("9900111"))!.Local, _store.SavedSearches.Load()));

        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", NewName = "服" });
        await Runner().ResumeAsync();
        var twice = Json((_store.UserTags.Load(), (await _store.Items.LoadAsync("9900111"))!.Local, _store.SavedSearches.Load()));

        Assert.Equal(once, twice);
        Assert.Equal(["服"], _store.UserTags.Load().Tops.Select(top => top.Name));
        Assert.Empty(Records());
    }

    [Fact]
    public async Task 小分類の名前の変更_一覧を書いた後で止まっても_次の起動で商品と保存した検索が新しい名前になる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "サマー")] });
        await _store.Items.SaveAsync(Item("9900111", Tagged(new UserTagAssignment { Top = "衣装", Subs = ["夏"] })));
        await SaveSearchAsync(new SearchModuleState { Kind = "UserTag", UserTags = [new UserTagCondition { Top = "衣装", Subs = ["夏"] }] });
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.RenameUserTag, Top = "衣装", Sub = "夏", NewName = "サマー" });

        await Runner().ResumeAsync();

        Assert.Equal(["サマー"], (await _store.Items.LoadAsync("9900111"))!.Local.UserTags.Single().Subs);
        Assert.Equal(["サマー"], SavedModule().UserTags.Single().Subs);
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
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        await _settings.UpdateAsync(current => current with { CardAttributes = ["質"] });
        await SaveSearchAsync(new SearchModuleState { Kind = "Attribute", Ranges = [new AttributeRange("質", 3, 5)] });
        await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" });

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

        Assert.NotNull(await Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.UseSource, CancellationToken.None));
        var once = Json((_store.Attributes.Load(), (await _store.Items.LoadAsync("9900121"))!.Local, _store.Settings.Load().CardAttributes));

        await LeaveRecordAsync(new PendingOperation
        {
            Kind = PendingOperationKind.RenameAttribute,
            OldName = "質",
            NewName = "質感",
            Keep = AttributeMergeValue.UseSource,
        });
        await Runner().ResumeAsync();
        var twice = Json((_store.Attributes.Load(), (await _store.Items.LoadAsync("9900121"))!.Local, _store.Settings.Load().CardAttributes));

        Assert.Equal(once, twice);
        Assert.Equal(4, (await _store.Items.LoadAsync("9900121"))!.Local.Attributes["質感"]);
        Assert.Empty(Records());
    }

    [Fact]
    public async Task 属性の名前の変更_記録を書けなければ始めない()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質" }] });
        BlockJournal();

        Assert.Null(await Runner().RenameAttributeAsync("質", "質感", AttributeMergeValue.KeepTarget, CancellationToken.None));
        Assert.Equal(["質"], _store.Attributes.Load().Attributes.Select(definition => definition.Name));
    }

    // ---- 続きの失敗 ----

    [Fact]
    public async Task 続きが落ちたら記録は残して知らせ_次に済んだら知らせを解消済みにする()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await _store.Items.SaveAsync(Item("9900121", new LocalBlock { Attributes = new Dictionary<string, int> { ["質"] = 4 } }));
        var recorded = await LeaveRecordAsync(new PendingOperation { Kind = PendingOperationKind.RenameAttribute, OldName = "質", NewName = "質感" });

        _attributes.Fail = true;
        Assert.Equal(1, await Runner().ResumeAsync());

        Assert.Equal(recorded.Id, Assert.Single(Records()).Id);
        var notice = Assert.Single(_store.Notifications.Load());
        Assert.Equal(NotificationKind.UnfinishedOperation, notice.Kind);
        Assert.Equal(PendingOperationRunner.NotificationPrefix + recorded.Id, notice.Id);
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
