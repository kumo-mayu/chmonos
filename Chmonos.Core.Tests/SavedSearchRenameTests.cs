using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// タグ・属性・共通素体の名前を変える・統合すると、保存した検索の条件の名前も同じ命令の中で付いていく（ユーザ判断 2026-10-04）。
/// 消したときは今のまま残す（0件の理由が条件の欄に見えるように）。
/// </summary>
public sealed class SavedSearchRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-savedrename-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private readonly CommandHandler _handler;

    public SavedSearchRenameTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _handler = new CommandHandler(
            null!,
            null!,
            userTags: new UserTagService(_store),
            attributes: new AttributeService(_store),
            settings: new SettingsService(_store),
            avatarEditor: new AvatarService(_store));
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

    private static UserTagTop Top(string name, params string[] subs)
        => new() { Name = name, Subs = subs.Select(sub => new UserTagSub { Name = sub }).ToList() };

    private async Task SaveAsync(params SearchHistoryEntry[] entries)
        => await _store.SavedSearches.SaveAsync(new SavedSearchList { Entries = entries });

    private static SearchHistoryEntry TagSearch(params UserTagCondition[] conditions) => new()
    {
        Name = "保存",
        Modules = [new SearchModuleState { Kind = "UserTag", UserTags = conditions }],
    };

    private IReadOnlyList<UserTagCondition> SavedTags()
        => _store.SavedSearches.Load().Entries.Single().Modules.Single().UserTags;

    [Fact]
    public async Task 大分類の名前を変えると_保存した検索の条件も変わる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏")] });
        await SaveAsync(TagSearch(new UserTagCondition { Top = "衣装", Subs = ["夏"] }));

        await _handler.ExecuteAsync(new UiCommand.RenameUserTag("衣装", null, "服"));

        var tag = Assert.Single(SavedTags());
        Assert.Equal("服", tag.Top);
        Assert.Equal(["夏"], tag.Subs);
    }

    [Fact]
    public async Task 大分類を統合すると_条件は寄せ先の綴りで1つにまとまる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("Cloth", "冬")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装", Subs = ["夏"] },
            new UserTagCondition { Top = "Cloth", Subs = ["冬"], MatchAll = true }));

        await _handler.ExecuteAsync(new UiCommand.RenameUserTag("衣装", null, "cloth"));

        var tag = Assert.Single(SavedTags());
        Assert.Equal("Cloth", tag.Top);
        Assert.Equivalent(new[] { "冬", "夏" }, tag.Subs);
        Assert.True(tag.MatchAll);
    }

    [Fact]
    public async Task 小分類の名前を変えると_同じ大分類の条件だけ変わる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("髪", "夏")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装", Subs = ["夏"] },
            new UserTagCondition { Top = "髪", Subs = ["夏"] }));

        await _handler.ExecuteAsync(new UiCommand.RenameUserTag("衣装", "夏", "サマー"));

        Assert.Equal(["サマー"], SavedTags().Single(tag => tag.Top == "衣装").Subs);
        Assert.Equal(["夏"], SavedTags().Single(tag => tag.Top == "髪").Subs);
    }

    [Fact]
    public async Task タグを消しても_保存した検索の条件は残す()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏")] });
        await SaveAsync(TagSearch(new UserTagCondition { Top = "衣装", Subs = ["夏"] }));

        await _handler.ExecuteAsync(new UiCommand.DeleteUserTag("衣装", null));

        Assert.Equal("衣装", Assert.Single(SavedTags()).Top);
    }

    [Fact]
    public async Task 属性の名前を変えると_幅の条件と並べ替えが変わる()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await SaveAsync(new SearchHistoryEntry
        {
            Name = "保存",
            Sort = "質感 が高い順",
            Modules = [new SearchModuleState { Kind = "Attribute", Ranges = [new AttributeRange("質感", 40, 90)] }],
        });

        await _handler.ExecuteAsync(new UiCommand.RenameAttribute("質感", "手触り"));

        var entry = _store.SavedSearches.Load().Entries.Single();
        Assert.Equal(new AttributeRange("手触り", 40, 90), entry.Modules.Single().Ranges.Single());
        Assert.Equal("手触り が高い順", entry.Sort);
    }

    [Fact]
    public async Task 属性を統合して両方の幅があれば_寄せ先の幅を残す()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "質感" }, new AttributeDefinition { Name = "手触り" }],
        });
        await SaveAsync(new SearchHistoryEntry
        {
            Name = "保存",
            Modules = [new SearchModuleState { Kind = "Attribute", Ranges = [new AttributeRange("質感", 40, 90), new AttributeRange("手触り", 10, 20)] }],
        });

        await _handler.ExecuteAsync(new UiCommand.RenameAttribute("質感", "手触り"));

        Assert.Equal(new AttributeRange("手触り", 10, 20), _store.SavedSearches.Load().Entries.Single().Modules.Single().Ranges.Single());
    }

    [Fact]
    public async Task 属性を消しても_保存した検索の条件は残す()
    {
        await _store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await SaveAsync(new SearchHistoryEntry
        {
            Name = "保存",
            Modules = [new SearchModuleState { Kind = "Attribute", Ranges = [new AttributeRange("質感", 40, 90)] }],
        });

        await _handler.ExecuteAsync(new UiCommand.DeleteAttribute("質感"));

        Assert.Equal("質感", _store.SavedSearches.Load().Entries.Single().Modules.Single().Ranges.Single().Name);
    }

    [Fact]
    public async Task 共通素体の名前を変えると_対応アバターの条件の素体の鍵が変わる()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "素体A" }] });
        await SaveAsync(new SearchHistoryEntry
        {
            Name = "保存",
            Modules = [new SearchModuleState { Kind = "Avatar", Items = ["base:素体A", "avatar:123"] }],
        });

        await _handler.ExecuteAsync(new UiCommand.RenameBase("素体A", "素体B"));

        Assert.Equal(["base:素体B", "avatar:123"], _store.SavedSearches.Load().Entries.Single().Modules.Single().Items);
    }

    [Fact]
    public async Task 共通素体を消しても_保存した検索の条件は残す()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "素体A" }] });
        await SaveAsync(new SearchHistoryEntry
        {
            Name = "保存",
            Modules = [new SearchModuleState { Kind = "Avatar", Items = ["base:素体A"] }],
        });

        await _handler.ExecuteAsync(new UiCommand.DeleteBase("素体A"));

        Assert.Equal(["base:素体A"], _store.SavedSearches.Load().Entries.Single().Modules.Single().Items);
    }

    [Fact]
    public async Task 小分類を別の大分類へ移すと_条件も移り_元に小分類が残らなければ元の条件は消える()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("髪")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装", Subs = ["夏"] },
            new UserTagCondition { Top = "色", Subs = ["夏"] }));

        await _handler.ExecuteAsync(new UiCommand.MoveUserTagSub("衣装", "夏", "髪", false));

        var tags = SavedTags();
        Assert.Equal(["髪", "色"], tags.Select(tag => tag.Top).ToArray());
        Assert.Equal(["夏"], tags.Single(tag => tag.Top == "髪").Subs);
        Assert.Equal(["夏"], tags.Single(tag => tag.Top == "色").Subs);
    }

    [Fact]
    public async Task 小分類を移すと_元の条件の残りの小分類は残り_移動先の条件にまとまる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏", "冬"), Top("髪", "短")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装", Subs = ["夏", "冬"] },
            new UserTagCondition { Top = "髪", Subs = ["短"] }));

        await _handler.ExecuteAsync(new UiCommand.MoveUserTagSub("衣装", "夏", "髪", false));

        var tags = SavedTags();
        Assert.Equal(["冬"], tags.Single(tag => tag.Top == "衣装").Subs);
        Assert.Equivalent(new[] { "短", "夏" }, tags.Single(tag => tag.Top == "髪").Subs);
    }

    [Fact]
    public async Task 小分類を移すとき_移動先が大分類の全部の条件なら_そのまま残す()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("髪")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装", Subs = ["夏"] },
            new UserTagCondition { Top = "髪" }));

        await _handler.ExecuteAsync(new UiCommand.MoveUserTagSub("衣装", "夏", "髪", false));

        var tag = Assert.Single(SavedTags());
        Assert.Equal("髪", tag.Top);
        Assert.Empty(tag.Subs);
    }

    [Fact]
    public async Task 移せなかった小分類の移動では_保存した検索は変わらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("髪")] });
        await SaveAsync(TagSearch(new UserTagCondition { Top = "衣装", Subs = ["春"] }));

        await _handler.ExecuteAsync(new UiCommand.MoveUserTagSub("衣装", "春", "髪", false));

        var tag = Assert.Single(SavedTags());
        Assert.Equal("衣装", tag.Top);
        Assert.Equal(["春"], tag.Subs);
    }

    [Fact]
    public async Task 大分類を小分類にすると_条件も入れ先の小分類になる()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装"), Top("服")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装" },
            new UserTagCondition { Top = "髪", Subs = ["短"] }));

        await _handler.ExecuteAsync(new UiCommand.NestUserTagTop("衣装", "服"));

        var tags = SavedTags();
        Assert.Equal(["服", "髪"], tags.Select(tag => tag.Top).ToArray());
        Assert.Equal(["衣装"], tags[0].Subs);
        Assert.Equal(["短"], tags[1].Subs);
    }

    [Fact]
    public async Task 大分類を小分類にして入れ先の条件があれば_小分類を足して1つにする()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装"), Top("服", "和")] });
        await SaveAsync(TagSearch(
            new UserTagCondition { Top = "衣装" },
            new UserTagCondition { Top = "服", Subs = ["和"] }));

        await _handler.ExecuteAsync(new UiCommand.NestUserTagTop("衣装", "服"));

        var tag = Assert.Single(SavedTags());
        Assert.Equal("服", tag.Top);
        Assert.Equivalent(new[] { "和", "衣装" }, tag.Subs);
    }

    [Fact]
    public async Task 小分類を持つ大分類を小分類にしようとして断られたら_条件は変わらない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [Top("衣装", "夏"), Top("服")] });
        await SaveAsync(TagSearch(new UserTagCondition { Top = "衣装", Subs = ["夏"] }));

        var result = await _handler.ExecuteAsync(new UiCommand.NestUserTagTop("衣装", "服"));

        Assert.IsType<CommandResult.Failed>(result);
        var tag = Assert.Single(SavedTags());
        Assert.Equal("衣装", tag.Top);
        Assert.Equal(["夏"], tag.Subs);
    }
}
