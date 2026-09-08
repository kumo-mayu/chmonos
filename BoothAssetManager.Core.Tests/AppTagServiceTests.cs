using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class AppTagServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly AppTagService _service;

    public AppTagServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-apptag-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new AppTagService(_store);
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

    private Task SaveMasterAsync(params AppTagTop[] tops)
        => _store.AppTags.SaveAsync(new AppTagMaster { Tops = tops });

    private Task SaveItemAsync(string id, params AppTagAssignment[] appTags)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { AppTags = appTags },
        });

    private async Task<IReadOnlyList<AppTagAssignment>> AppTagsOfAsync(string id)
        => (await _store.Items.LoadAsync(id))!.Local.AppTags;

    [Fact]
    public async Task CountsHowManyItemsUseEachTag()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" }, new AppTagTop { Name = "小物" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装", Subs = ["制服"] });
        await SaveItemAsync("2", new AppTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        var usage = await _service.LoadUsageAsync();

        Assert.Equal(2, usage.Single(entry => entry.Top == "衣装").ItemCount);
        Assert.Equal(2, usage.Single(entry => entry.Top == "衣装").SubCounts["制服"]);
        Assert.Equal(0, usage.Single(entry => entry.Top == "小物").ItemCount);
    }

    /// <summary>マスタに無い名前をitemが参照したままの状態。直せるのはこの画面だけ。</summary>
    [Fact]
    public async Task FindsNamesThatOnlyItemsStillReference()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "消えたタグ" });
        await SaveItemAsync("2", new AppTagAssignment { Top = "消えたタグ" }, new AppTagAssignment { Top = "衣装" });

        var orphan = Assert.Single(await _service.LoadOrphansAsync());

        Assert.Equal("消えたタグ", orphan.Top);
        Assert.Equal(2, orphan.ItemCount);
    }

    [Fact]
    public async Task RenamesTheTagInTheMasterAndInEveryItem()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装", Subs = [new AppTagSub { Name = "制服" }] });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装", Subs = ["制服"] });

        var result = await _service.RenameTopAsync("衣装", "アバター衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.False(result.WasMerged);
        Assert.Equal("アバター衣装", Assert.Single(result.Master.Tops).Name);
        Assert.Equal("アバター衣装", Assert.Single(await AppTagsOfAsync("1")).Top);
        // サブレベルはトップに従属するので、そのまま連れて行く
        Assert.Equal("制服", Assert.Single(Assert.Single(await AppTagsOfAsync("1")).Subs));
    }

    /// <summary>マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。</summary>
    [Fact]
    public async Task CanRenameANameThatOnlyItemsReference()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "消えたタグ" });

        var result = await _service.RenameTopAsync("消えたタグ", "衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal("衣装", Assert.Single(await AppTagsOfAsync("1")).Top);
        Assert.Empty(await _service.LoadOrphansAsync());
    }

    /// <summary>既にある名前へ改名すると統合。両方付いていたitemでは1件にまとめる。</summary>
    [Fact]
    public async Task MergesWhenRenamedOntoAnExistingTag()
    {
        await SaveMasterAsync(
            new AppTagTop { Name = "小物", Subs = [new AppTagSub { Name = "指輪" }] },
            new AppTagTop { Name = "アクセサリ", Subs = [new AppTagSub { Name = "ピアス" }] });

        await SaveItemAsync(
            "1",
            new AppTagAssignment { Top = "小物", Subs = ["指輪"] },
            new AppTagAssignment { Top = "アクセサリ", Subs = ["ピアス"] });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        Assert.True(result.WasMerged);
        Assert.Equal("小物", Assert.Single(result.Master.Tops).Name);
        Assert.Equal(["指輪", "ピアス"], result.Master.Tops[0].Subs.Select(sub => sub.Name));

        var assignment = Assert.Single(await AppTagsOfAsync("1"));
        Assert.Equal("小物", assignment.Top);
        Assert.Equal(["指輪", "ピアス"], assignment.Subs);
    }

    [Fact]
    public async Task RemovesTheTagFromEveryItemWhenDeleted()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" }, new AppTagTop { Name = "小物" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装" }, new AppTagAssignment { Top = "小物" });

        var result = await _service.DeleteTopAsync("衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal("小物", Assert.Single(result.Master.Tops).Name);
        Assert.Equal("小物", Assert.Single(await AppTagsOfAsync("1")).Top);
    }

    /// <summary>
    /// appTagが空になったitem数を数える。編集の対象に戻るので、黙って進めてはいけない。
    /// </summary>
    [Fact]
    public async Task ReportsHowManyItemsAreLeftWithNoTagAtAll()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" }, new AppTagTop { Name = "小物" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装" });
        await SaveItemAsync("2", new AppTagAssignment { Top = "衣装" }, new AppTagAssignment { Top = "小物" });

        var result = await _service.DeleteTopAsync("衣装");

        Assert.Equal(2, result.ItemsUpdated);
        Assert.Equal(1, result.ItemsLeftUntagged);
    }

    [Fact]
    public async Task RenamesASubLevelWithoutTouchingOtherTops()
    {
        await SaveMasterAsync(
            new AppTagTop { Name = "衣装", Subs = [new AppTagSub { Name = "制服" }] },
            new AppTagTop { Name = "小物", Subs = [new AppTagSub { Name = "制服" }] });

        await SaveItemAsync(
            "1",
            new AppTagAssignment { Top = "衣装", Subs = ["制服"] },
            new AppTagAssignment { Top = "小物", Subs = ["制服"] });

        await _service.RenameSubAsync("衣装", "制服", "学生服");

        var appTags = await AppTagsOfAsync("1");
        Assert.Equal(["学生服"], appTags.Single(entry => entry.Top == "衣装").Subs);
        Assert.Equal(["制服"], appTags.Single(entry => entry.Top == "小物").Subs);
    }

    [Fact]
    public async Task DropsASubLevelFromItemsWhenDeleted()
    {
        await SaveMasterAsync(new AppTagTop
        {
            Name = "衣装",
            Subs = [new AppTagSub { Name = "制服" }, new AppTagSub { Name = "私服" }],
        });

        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        var result = await _service.DeleteSubAsync("衣装", "制服");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(["私服"], Assert.Single(result.Master.Tops).Subs.Select(sub => sub.Name));
        // トップは残す。サブを消しただけで分類そのものが外れては困る
        Assert.Equal(["私服"], Assert.Single(await AppTagsOfAsync("1")).Subs);
    }

    /// <summary>統合したサブが重複しないこと。同じ名前が2つ並ぶと絞り込みが分裂する。</summary>
    [Fact]
    public async Task DoesNotLeaveDuplicateSubsAfterMerging()
    {
        await SaveMasterAsync(new AppTagTop
        {
            Name = "衣装",
            Subs = [new AppTagSub { Name = "制服" }, new AppTagSub { Name = "学生服" }],
        });

        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装", Subs = ["制服", "学生服"] });

        await _service.RenameSubAsync("衣装", "制服", "学生服");

        Assert.Equal(["学生服"], Assert.Single(await AppTagsOfAsync("1")).Subs);
        Assert.Equal(["学生服"], Assert.Single((await _service.LoadUsageAsync())).SubCounts.Keys);
    }

    [Fact]
    public async Task WritesTheMemoWithoutTouchingItems()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装", Subs = [new AppTagSub { Name = "制服" }] });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装", Subs = ["制服"] });

        await _service.SetMemoAsync("衣装", null, "  アバターに着せるもの全般  ");
        await _service.SetMemoAsync("衣装", "制服", "学校のもの");

        var top = Assert.Single(_store.AppTags.Load().Tops);
        Assert.Equal("アバターに着せるもの全般", top.Memo);
        Assert.Equal("学校のもの", Assert.Single(top.Subs).Memo);
        Assert.Equal("衣装", Assert.Single(await AppTagsOfAsync("1")).Top);
    }

    /// <summary>
    /// 並びは検索の絞り込みにも編集の候補にも出るので、追加順に縛られないようにする。
    /// itemは名前で参照しているので、並べ替えでitemに触る必要はない。
    /// </summary>
    [Fact]
    public async Task ReordersTopLevelsWithoutTouchingItems()
    {
        await SaveMasterAsync(
            new AppTagTop { Name = "衣装" },
            new AppTagTop { Name = "小物" },
            new AppTagTop { Name = "ギミック" });

        await SaveItemAsync("1", new AppTagAssignment { Top = "小物" });

        var master = await _service.ReorderAsync(null, ["ギミック", "衣装", "小物"]);

        Assert.Equal(["ギミック", "衣装", "小物"], master.Tops.Select(top => top.Name));
        Assert.Equal("小物", Assert.Single(await AppTagsOfAsync("1")).Top);
    }

    [Fact]
    public async Task ReordersSubLevelsWithinOneTop()
    {
        await SaveMasterAsync(
            new AppTagTop
            {
                Name = "衣装",
                Subs = [new AppTagSub { Name = "制服" }, new AppTagSub { Name = "私服" }],
            },
            new AppTagTop { Name = "小物", Subs = [new AppTagSub { Name = "指輪" }] });

        var master = await _service.ReorderAsync("衣装", ["私服", "制服"]);

        Assert.Equal(["私服", "制服"], master.Tops[0].Subs.Select(sub => sub.Name));
        Assert.Equal(["指輪"], master.Tops[1].Subs.Select(sub => sub.Name));
    }

    /// <summary>
    /// 絞り込み中は見えている分しか動かせないので、指定に無かったものは末尾に残す。
    /// 黙って消えるより、末尾に寄る方がまだ気付ける。
    /// </summary>
    [Fact]
    public async Task KeepsNamesThatTheNewOrderDidNotMention()
    {
        await SaveMasterAsync(
            new AppTagTop { Name = "衣装" },
            new AppTagTop { Name = "小物" },
            new AppTagTop { Name = "ギミック" });

        var master = await _service.ReorderAsync(null, ["ギミック"]);

        Assert.Equal(["ギミック", "衣装", "小物"], master.Tops.Select(top => top.Name));
    }

    [Fact]
    public async Task IgnoresAReorderForATopThatIsGone()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装", Subs = [new AppTagSub { Name = "制服" }] });

        var master = await _service.ReorderAsync("無いトップ", ["制服"]);

        Assert.Equal(["制服"], Assert.Single(master.Tops).Subs.Select(sub => sub.Name));
    }

    [Fact]
    public async Task IgnoresARenameThatChangesNothing()
    {
        await SaveMasterAsync(new AppTagTop { Name = "衣装" });
        await SaveItemAsync("1", new AppTagAssignment { Top = "衣装" });

        Assert.Equal(0, (await _service.RenameTopAsync("衣装", "   ")).ItemsUpdated);
        Assert.Equal(0, (await _service.RenameTopAsync("衣装", "衣装")).ItemsUpdated);
        Assert.Equal("衣装", Assert.Single(_store.AppTags.Load().Tops).Name);
    }
}
