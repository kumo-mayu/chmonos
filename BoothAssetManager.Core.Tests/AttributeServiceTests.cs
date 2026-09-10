using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class AttributeServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly AttributeService _service;

    public AttributeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-attr-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new AttributeService(_store);
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

    private Task SaveMasterAsync(params AttributeDefinition[] attributes)
        => _store.Attributes.SaveAsync(new AttributeMaster { Attributes = attributes });

    private Task SaveItemAsync(string id, params (string Name, int Value)[] attributes)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                Attributes = attributes.ToDictionary(entry => entry.Name, entry => entry.Value),
            },
        });

    private async Task<IReadOnlyDictionary<string, int>> AttributesOfAsync(string id)
        => (await _store.Items.LoadAsync(id))!.Local.Attributes;

    [Fact]
    public async Task CountsAndAveragesTheRatings()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "かっこいい" });

        await SaveItemAsync("1", ("かわいい", 80));
        await SaveItemAsync("2", ("かわいい", 60));

        var usage = await _service.LoadUsageAsync();

        Assert.Equal(2, usage.Single(entry => entry.Name == "かわいい").ItemCount);
        Assert.Equal(70, usage.Single(entry => entry.Name == "かわいい").Average);

        // 未評価は数えない。0%と「付けていない」は別
        Assert.Equal(0, usage.Single(entry => entry.Name == "かっこいい").ItemCount);
        Assert.Null(usage.Single(entry => entry.Name == "かっこいい").Average);
    }

    [Fact]
    public async Task FindsNamesThatOnlyItemsStillReference()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });
        await SaveItemAsync("1", ("消えた属性", 50));
        await SaveItemAsync("2", ("消えた属性", 30), ("かわいい", 20));

        var orphan = Assert.Single(await _service.LoadOrphansAsync());

        Assert.Equal("消えた属性", orphan.Name);
        Assert.Equal(2, orphan.ItemCount);
    }

    [Fact]
    public async Task RenamesInTheMasterAndInEveryItem()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい", Memo = "基準" });
        await SaveItemAsync("1", ("かわいい", 80));

        var result = await _service.RenameAsync("かわいい", "可愛い");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.False(result.WasMerged);
        Assert.Equal("基準", Assert.Single(result.Master.Attributes).Memo);
        Assert.Equal(80, (await AttributesOfAsync("1"))["可愛い"]);
    }

    /// <summary>マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。</summary>
    [Fact]
    public async Task CanRenameANameThatOnlyItemsReference()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });
        await SaveItemAsync("1", ("消えた属性", 40));

        await _service.RenameAsync("消えた属性", "かわいい");

        Assert.Equal(40, (await AttributesOfAsync("1"))["かわいい"]);
        Assert.Empty(await _service.LoadOrphansAsync());
    }

    /// <summary>
    /// 統合で値がぶつかったとき、既定は寄せ先の値を残す。
    /// 平均を取らないのは、本人が付けていない数字を作ってしまうため。
    /// </summary>
    [Fact]
    public async Task KeepsTheTargetValueWhenMerging()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "可愛い" });

        await SaveItemAsync("1", ("かわいい", 80), ("可愛い", 30));

        var result = await _service.RenameAsync("かわいい", "可愛い");

        Assert.True(result.WasMerged);
        Assert.Equal("可愛い", Assert.Single(result.Master.Attributes).Name);

        var attributes = await AttributesOfAsync("1");
        Assert.Equal(30, attributes["可愛い"]);
        Assert.False(attributes.ContainsKey("かわいい"));
    }

    [Fact]
    public async Task CanUseTheSourceValueInstead()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "可愛い" });

        await SaveItemAsync("1", ("かわいい", 80), ("可愛い", 30));

        await _service.RenameAsync("かわいい", "可愛い", AttributeMergeValue.UseSource);

        Assert.Equal(80, (await AttributesOfAsync("1"))["可愛い"]);
    }

    /// <summary>寄せ先に値が無いitemでは、選択に関係なく寄せ元の値がそのまま入る。</summary>
    [Fact]
    public async Task CarriesTheValueWhenTheTargetHadNone()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "可愛い" });

        await SaveItemAsync("1", ("かわいい", 80));

        await _service.RenameAsync("かわいい", "可愛い");

        Assert.Equal(80, (await AttributesOfAsync("1"))["可愛い"]);
    }

    /// <summary>どちらを残すかは押す前に決めるので、ぶつかる件数が要る。</summary>
    [Fact]
    public async Task PreviewsHowManyValuesCollide()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "可愛い" });

        await SaveItemAsync("1", ("かわいい", 80), ("可愛い", 30));  // ぶつかる
        await SaveItemAsync("2", ("かわいい", 50), ("可愛い", 50));  // 同じ値なので選ぶ意味が無い
        await SaveItemAsync("3", ("かわいい", 20));                  // 寄せ先が無い
        await SaveItemAsync("4", ("可愛い", 10));                    // 寄せ元が無いので対象外

        var preview = await _service.PreviewMergeAsync("かわいい", "可愛い");

        Assert.Equal(3, preview.ItemCount);
        Assert.Equal(1, preview.Conflicts);
    }

    [Fact]
    public async Task RemovesTheRatingFromEveryItemWhenDeleted()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "かっこいい" });

        await SaveItemAsync("1", ("かわいい", 80), ("かっこいい", 20));

        var result = await _service.DeleteAsync("かわいい");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal("かっこいい", Assert.Single(result.Master.Attributes).Name);

        var attributes = await AttributesOfAsync("1");
        Assert.False(attributes.ContainsKey("かわいい"));
        Assert.Equal(20, attributes["かっこいい"]);
    }

    [Fact]
    public async Task WritesTheMemoWithoutTouchingItems()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });
        await SaveItemAsync("1", ("かわいい", 80));

        await _service.SetMemoAsync("かわいい", "  丸みと配色で判断する  ");

        Assert.Equal("丸みと配色で判断する", Assert.Single(_store.Attributes.Load().Attributes).Memo);
        Assert.Equal(80, (await AttributesOfAsync("1"))["かわいい"]);
    }

    /// <summary>統合でメモを捨てない。付け方の基準が消えると尺度がぶれる。</summary>
    [Fact]
    public async Task CarriesTheSourceMemoIntoTheMergedAttribute()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい", Memo = "丸み" },
            new AttributeDefinition { Name = "可愛い", Memo = "配色" });

        var result = await _service.RenameAsync("かわいい", "可愛い");

        var memo = Assert.Single(result.Master.Attributes).Memo;
        Assert.Contains("配色", memo);
        Assert.Contains("「かわいい」から統合：丸み", memo);
    }

    [Fact]
    public async Task ReordersWithoutTouchingItems()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "かっこいい" },
            new AttributeDefinition { Name = "こわい" });

        await SaveItemAsync("1", ("かわいい", 80));

        var master = await _service.ReorderAsync(["こわい", "かわいい", "かっこいい"]);

        Assert.Equal(["こわい", "かわいい", "かっこいい"], master.Attributes.Select(entry => entry.Name));
        Assert.Equal(80, (await AttributesOfAsync("1"))["かわいい"]);
    }

    /// <summary>絞り込み中は見えている分しか送れないので、指定に無かった名前は末尾に残す。</summary>
    [Fact]
    public async Task KeepsNamesThatTheNewOrderDidNotMention()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい" },
            new AttributeDefinition { Name = "かっこいい" },
            new AttributeDefinition { Name = "こわい" });

        var master = await _service.ReorderAsync(["こわい"]);

        Assert.Equal(["こわい", "かわいい", "かっこいい"], master.Attributes.Select(entry => entry.Name));
    }

    [Fact]
    public async Task IgnoresARenameThatChangesNothing()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });
        await SaveItemAsync("1", ("かわいい", 80));

        Assert.Equal(0, (await _service.RenameAsync("かわいい", "   ")).ItemsUpdated);
        Assert.Equal(0, (await _service.RenameAsync("かわいい", "かわいい")).ItemsUpdated);
        Assert.Equal("かわいい", Assert.Single(_store.Attributes.Load().Attributes).Name);
    }

    /// <summary>
    /// 既定の指定は item に何も書かない。並べるだけで、値は触られたときにしか付かない。
    /// </summary>
    [Fact]
    public async Task MarksTheAttributeAsDefaultWithoutTouchingItems()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });
        await SaveItemAsync("1", ("かわいい", 80));

        await _service.SetDefaultAsync("かわいい", true);

        Assert.True(Assert.Single(_store.Attributes.Load().Attributes).IsDefault);
        Assert.Equal(80, (await AttributesOfAsync("1"))["かわいい"]);
    }

    [Fact]
    public async Task ClearsTheDefaultFlag()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい", IsDefault = true });

        await _service.SetDefaultAsync("かわいい", false);

        Assert.False(Assert.Single(_store.Attributes.Load().Attributes).IsDefault);
    }

    [Fact]
    public async Task IgnoresUnknownAttributesWhenSettingTheDefault()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい" });

        await _service.SetDefaultAsync("居ない属性", true);

        Assert.False(Assert.Single(_store.Attributes.Load().Attributes).IsDefault);
    }

    /// <summary>メモを書き換えても既定の指定を落とさない。組み直すたびに書き写す必要がある</summary>
    [Fact]
    public async Task KeepsTheDefaultFlagWhenTheMemoChanges()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい", IsDefault = true });

        await _service.SetMemoAsync("かわいい", "丸みで判断する");

        Assert.True(Assert.Single(_store.Attributes.Load().Attributes).IsDefault);
    }

    [Fact]
    public async Task KeepsTheDefaultFlagWhenRenamed()
    {
        await SaveMasterAsync(new AttributeDefinition { Name = "かわいい", IsDefault = true });

        await _service.RenameAsync("かわいい", "愛らしい");

        var definition = Assert.Single(_store.Attributes.Load().Attributes);
        Assert.Equal("愛らしい", definition.Name);
        Assert.True(definition.IsDefault);
    }

    /// <summary>統合したら残る側の指定が生きる。寄せ元の指定は付いてこない</summary>
    [Fact]
    public async Task KeepsTheTargetDefaultFlagWhenMerged()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい", IsDefault = false },
            new AttributeDefinition { Name = "愛らしい", IsDefault = true });

        await _service.RenameAsync("愛らしい", "かわいい");

        var definition = Assert.Single(_store.Attributes.Load().Attributes);
        Assert.Equal("かわいい", definition.Name);
        Assert.False(definition.IsDefault);
    }

    [Fact]
    public async Task KeepsTheDefaultFlagWhenReordered()
    {
        await SaveMasterAsync(
            new AttributeDefinition { Name = "かわいい", IsDefault = true },
            new AttributeDefinition { Name = "かっこいい" });

        await _service.ReorderAsync(["かっこいい", "かわいい"]);

        var loaded = _store.Attributes.Load().Attributes;
        Assert.Equal(["かっこいい", "かわいい"], loaded.Select(entry => entry.Name));
        Assert.True(loaded.First(entry => entry.Name == "かわいい").IsDefault);
    }
}
