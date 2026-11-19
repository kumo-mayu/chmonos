using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 付いているが値はまだの属性（<c>local.pendingAttributes</c>。ユーザ判断 2026-11-20・`docs/spec/data-model.md`「属性」）。
/// 保存・形式の版・改名と削除・まとめて足す・ID の付け替えで、値の無い属性（付いていない）と 0% との区別を崩さない事を押さえる。
/// </summary>
public class PendingAttributeTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly CommandHandler _commands;

    public PendingAttributeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-pending-attr-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _commands = new CommandHandler(null!, null!, new EditService(_store));
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

        GC.SuppressFinalize(this);
    }

    private Task SaveAsync(string id, LocalBlock local) => _store.Items.SaveAsync(new ItemRecord
    {
        Id = id,
        Booth = new BoothBlock { FetchedAt = DateTimeOffset.UnixEpoch, Name = "作り物の商品" },
        Local = local,
    });

    private async Task<LocalBlock> LocalOfAsync(string id) => (await _store.Items.LoadAsync(id))!.Local;

    private string ItemFile(string id) => new AppPaths(_root).ItemFile(id);

    private async Task<BulkItemChangeOutcome> RunAsync(BulkItemChange change, params string[] ids)
        => Assert.IsType<CommandResult.ItemsChanged>(await _commands.ExecuteAsync(new UiCommand.ChangeItems(ids, change))).Outcome;

    // --- 形式の版 ---

    /// <summary>
    /// 版 2（v1.3.0 の開発中の版）が書いた商品は、移し替えを通って「値はまだが無い」として読める。書き直すと版 3 になり、欄は書かない。
    /// 表の段 [3] を消すと読めなくなる（KeyNotFoundException）。
    /// </summary>
    [Fact]
    public async Task 版2の商品を値はまだ無しとして読める()
    {
        var json = "{\"formatVersion\": 2, \"id\": \"9900901\", \"booth\": {\"name\": \"作り物\"}, "
            + "\"local\": {\"attributes\": {\"可愛さ\": 0, \"派手さ\": 70}, \"memo\": \"覚え書き\"}}";
        File.WriteAllText(ItemFile("9900901"), json);

        var item = (await _store.Items.LoadAsync("9900901"))!;
        Assert.Equal(0, item.Local.Attributes["可愛さ"]);
        Assert.Equal(70, item.Local.Attributes["派手さ"]);
        Assert.Null(item.Local.PendingAttributes);
        Assert.Equal("覚え書き", item.Local.Memo);

        await _store.Items.SaveLocalAsync("9900901", item.Local with { Memo = "直した" }, [LocalField.Memo]);
        var written = File.ReadAllText(ItemFile("9900901"));
        Assert.Equal(3, StoreFormat.VersionOf(Encoding.UTF8.GetBytes(written)));
        Assert.DoesNotContain("pendingAttributes", written, StringComparison.Ordinal);
    }

    /// <summary>版 3 を書く。版 2 だけを知るバージョンに、値はまだの属性を消させないため（保存先の印で止まる）</summary>
    [Fact]
    public void 書く版は3で版2の保存先は古いが読める()
    {
        Assert.Equal(3, StoreFormat.Current);
        Assert.Equal(StoreFormat.Verdict.Older, StoreFormat.Check(2));
    }

    // --- 保存の形 ---

    [Fact]
    public async Task 値はまだを保存して読み直すと残り_JSONは名前の並び()
    {
        await SaveAsync("9900901", new LocalBlock { Attributes = new Dictionary<string, int> { ["派手さ"] = 40 } });
        var current = await LocalOfAsync("9900901");

        await _store.Items.SaveLocalAsync("9900901", current with { PendingAttributes = ["可愛さ", "落ち着き"] }, LocalOwners.Attributes);

        var local = await LocalOfAsync("9900901");
        Assert.Equal(["可愛さ", "落ち着き"], local.PendingAttributes!);
        Assert.Equal(40, local.Attributes["派手さ"]);

        using var document = JsonDocument.Parse(File.ReadAllText(ItemFile("9900901")));
        var pending = document.RootElement.GetProperty("local").GetProperty("pendingAttributes");
        Assert.Equal(["可愛さ", "落ち着き"], pending.EnumerateArray().Select(name => name.GetString()));
    }

    /// <summary>空なら書かない（版 2 の商品を書き直しただけで形が変わらないように）</summary>
    [Fact]
    public async Task 値はまだが空なら欄を書かない()
    {
        await SaveAsync("9900901", new LocalBlock { PendingAttributes = [] });
        Assert.DoesNotContain("pendingAttributes", File.ReadAllText(ItemFile("9900901")), StringComparison.Ordinal);
    }

    /// <summary>手で両方に書いた名前は値が勝つ（値はまだとしては返さない）。大文字と小文字だけ違う名前も同じ属性</summary>
    [Fact]
    public void 手で両方に書いた名前は値が勝つ()
    {
        var local = new LocalBlock
        {
            PendingAttributes = ["Soft", "soft", "落ち着き"],
            Attributes = new Dictionary<string, int> { ["SOFT"] = 20 },
        };

        Assert.Equal(["落ち着き"], local.PendingAttributes!);
        Assert.True(ItemAttributes.IsAttached(local, "soft"));
        Assert.False(ItemAttributes.IsPending(local, "soft"));
    }

    /// <summary>持ち主の宣言で重ねる所（SaveLocalAsync）と、変わった項目を見る所（編集の画面）が、値はまだを属性の一部として扱う</summary>
    [Fact]
    public void 値はまだは属性の項目として重ね_変わった事も見る()
    {
        var before = new LocalBlock { Memo = "前" };
        var after = before with { PendingAttributes = ["可愛さ"] };

        Assert.Equal([LocalField.Attributes], LocalFields.Changed(before, after, LocalOwners.EditScreen));
        Assert.Equal(["可愛さ"], LocalFields.Merge(new LocalBlock(), after, LocalOwners.Attributes).PendingAttributes!);
        Assert.Empty(LocalFields.Changed(after, after with { }, LocalOwners.EditScreen));
    }

    // --- 足す・値を付ける・外す ---

    [Fact]
    public void 値を付けると値はまだから外れて値の方へ移る()
    {
        var local = new LocalBlock { PendingAttributes = ["可愛さ", "派手さ"] };

        var rated = ItemAttributes.WithValue(local, "可愛さ", 0);

        Assert.Equal(0, rated.Attributes["可愛さ"]);
        Assert.Equal(["派手さ"], rated.PendingAttributes!);

        // 外すと付いていない状態へ戻る（値はまだに戻らない）
        var removed = ItemAttributes.Without(rated, "可愛さ")!;
        Assert.False(ItemAttributes.IsAttached(removed, "可愛さ"));
        Assert.Equal(["派手さ"], removed.PendingAttributes!);
    }

    /// <summary>まとめて足すのは値はまだとして。値がある（0% を含む）・値はまだの商品は何もせず数える</summary>
    [Fact]
    public async Task まとめて足すと値はまだになり_付いている商品は数えるだけ()
    {
        await SaveAsync("9900901", new LocalBlock { Memo = "残す" });
        await SaveAsync("9900902", new LocalBlock { Attributes = new Dictionary<string, int> { ["可愛さ"] = 0 } });
        await SaveAsync("9900903", new LocalBlock { PendingAttributes = ["可愛さ"] });

        var outcome = await RunAsync(new BulkItemChange.AddAttribute("可愛さ"), "9900901", "9900902", "9900903", "9900999");

        Assert.Equal(new BulkItemChangeOutcome(Changed: 1, AlreadyDone: 2, NotFound: 1), outcome);
        var added = await LocalOfAsync("9900901");
        Assert.Equal(["可愛さ"], added.PendingAttributes!);
        Assert.Empty(added.Attributes);
        Assert.Equal("残す", added.Memo);
        Assert.Equal(0, (await LocalOfAsync("9900902")).Attributes["可愛さ"]);
        Assert.Null((await LocalOfAsync("9900902")).PendingAttributes);
    }

    [Fact]
    public async Task まとめて外すと値はまだも外れる()
    {
        await SaveAsync("9900901", new LocalBlock { PendingAttributes = ["可愛さ", "派手さ"] });
        await SaveAsync("9900902", new LocalBlock());

        var outcome = await RunAsync(new BulkItemChange.RemoveAttribute("可愛さ"), "9900901", "9900902");

        Assert.Equal(new BulkItemChangeOutcome(Changed: 1, AlreadyDone: 1, NotFound: 0), outcome);
        Assert.Equal(["派手さ"], (await LocalOfAsync("9900901")).PendingAttributes!);
    }

    // --- 属性の管理（改名・統合・削除） ---

    [Fact]
    public async Task 改名で値はまだの名前も付け替わり_2回当てても同じ()
    {
        var service = new AttributeService(_store);
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "かわいい" }] });
        await SaveAsync("9900901", new LocalBlock { PendingAttributes = ["かわいい", "派手さ"] });

        var first = await service.RenameAsync("かわいい", "可愛さ");
        var again = await service.RenameAsync("かわいい", "可愛さ");

        Assert.Equal(1, first.ItemsUpdated);
        Assert.Equal(0, again.ItemsUpdated);
        Assert.Equal(["可愛さ", "派手さ"], (await LocalOfAsync("9900901")).PendingAttributes!);
    }

    /// <summary>統合先に値がある商品では値が残り、値はまだの方は落ちる。値はまだどうしなら1つになる</summary>
    [Fact]
    public async Task 統合で値はまだは値に負け_重なれば1つになる()
    {
        var service = new AttributeService(_store);
        await _store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "かわいい" }, new AttributeDefinition { Name = "可愛さ" }],
        });
        await SaveAsync("9900901", new LocalBlock
        {
            PendingAttributes = ["かわいい"],
            Attributes = new Dictionary<string, int> { ["可愛さ"] = 30 },
        });
        await SaveAsync("9900902", new LocalBlock { PendingAttributes = ["かわいい", "可愛さ"] });

        var preview = await service.PreviewMergeAsync("かわいい", "可愛さ");
        Assert.Equal((2, 0), (preview.ItemCount, preview.Conflicts));

        await service.RenameAsync("かわいい", "可愛さ");

        var valued = await LocalOfAsync("9900901");
        Assert.Equal(30, valued.Attributes["可愛さ"]);
        Assert.Null(valued.PendingAttributes);
        Assert.Equal(["可愛さ"], (await LocalOfAsync("9900902")).PendingAttributes!);
    }

    [Fact]
    public async Task 削除で値はまだからも外れ_マスタに無い名前は使われていない属性に出る()
    {
        var service = new AttributeService(_store);
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "可愛さ" }] });
        await SaveAsync("9900901", new LocalBlock { PendingAttributes = ["可愛さ", "手で書いた名前"] });

        var orphan = Assert.Single(await service.LoadOrphansAsync());
        Assert.Equal(("手で書いた名前", 1), (orphan.Name, orphan.ItemCount));

        var result = await service.DeleteAsync("可愛さ");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(["手で書いた名前"], (await LocalOfAsync("9900901")).PendingAttributes!);

        // 使われている数（管理の画面の件数）は値のある物だけ。値はまだは「未評価」のまま数えない
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "手で書いた名前" }] });
        Assert.Equal(0, Assert.Single(await service.LoadUsageAsync()).ItemCount);
    }

    /// <summary>
    /// 改名の指紋：値はまだだけで古い名前を持つ商品も記録に入る。値はまだを持たない商品の指紋は前の形のまま
    /// （版 2 のうちに記録した途中の操作を、版を上げた後も「記録と同じ」と見られるように）
    /// </summary>
    [Fact]
    public void 改名の指紋は値はまだを持たない商品で前と同じ()
    {
        var master = new AttributeMaster { Attributes = [new AttributeDefinition { Name = "かわいい" }] };
        var items = new[]
        {
            new ItemRecord { Id = "9900901", Booth = new BoothBlock { Name = "a" }, Local = new LocalBlock { Attributes = new Dictionary<string, int> { ["かわいい"] = 50 } } },
            new ItemRecord { Id = "9900902", Booth = new BoothBlock { Name = "b" }, Local = new LocalBlock { PendingAttributes = ["かわいい"] } },
        };

        var fingerprint = OperationFingerprint.ForAttributeRename(master, items, "かわいい", "可愛さ");

        // 前の作り：属性の表だけを保存と同じ書き方で書き出したハッシュ（鍵が1つなので並べ直しは要らない）
        var before = JsonSerializer.SerializeToNode(new Dictionary<string, int> { ["かわいい"] = 50 }, JsonStore.Options)!.ToJsonString();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(before));
        Assert.Equal(Convert.ToHexStringLower(digest.AsSpan(0, 8)), fingerprint.Holders["9900901"]);
        Assert.True(fingerprint.Holders.ContainsKey("9900902"));
    }

    // --- そのほかの書き手 ---

    /// <summary>ID の付け替えで合わせるとき、値と値はまだは1組で移す先が勝つ（混ぜない）</summary>
    [Fact]
    public void IDの付け替えは値と値はまだを1組で選ぶ()
    {
        var source = new LocalBlock { Attributes = new Dictionary<string, int> { ["可愛さ"] = 80 } };
        var target = new LocalBlock { PendingAttributes = ["派手さ"] };

        var merged = ItemIdChange.Merge(source, target, new HashSet<int>(), "9900901", "9900902");
        Assert.Empty(merged.Attributes);
        Assert.Equal(["派手さ"], merged.PendingAttributes!);

        var fromSource = ItemIdChange.Merge(new LocalBlock { PendingAttributes = ["可愛さ"] }, new LocalBlock(), new HashSet<int>(), "9900901", "9900902");
        Assert.Equal(["可愛さ"], fromSource.PendingAttributes!);
    }

    /// <summary>取り込みを取り消す時、値はまだだけの商品も人の入力がある物として残す</summary>
    [Fact]
    public void 値はまだだけでも人の入力に数える()
        => Assert.True(ImportedItemRemoval.HasHumanInput(new LocalBlock { PendingAttributes = ["可愛さ"] }));

    /// <summary>読むだけの側は値のある物だけを見る：検索の属性の範囲・並べ替え・編集状況の「属性が未入力」</summary>
    [Fact]
    public void 読むだけの側では値はまだは値の無い物()
    {
        var pendingOnly = new ItemRecord
        {
            Id = "9900901",
            Booth = new BoothBlock { Name = "a" },
            Local = new LocalBlock { PendingAttributes = ["可愛さ"] },
        };

        Assert.False(AttributeCondition.InRange(pendingOnly, new AttributeRange("可愛さ", 0, 100)));
        Assert.True(EditFieldsMissing.IsMissing(pendingOnly, EditField.Attributes));
    }
}
