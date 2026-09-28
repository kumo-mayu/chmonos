using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class UserTagServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly UserTagService _service;

    public UserTagServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-apptag-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new UserTagService(_store);
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

    private Task SaveMasterAsync(params UserTagTop[] tops)
        => _store.UserTags.SaveAsync(new UserTagMaster { Tops = tops });

    private Task SaveItemAsync(string id, params UserTagAssignment[] userTags)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { UserTags = userTags },
        });

    private async Task<IReadOnlyList<UserTagAssignment>> UserTagsOfAsync(string id)
        => (await _store.Items.LoadAsync(id))!.Local.UserTags;

    [Fact]
    public async Task CountsHowManyItemsUseEachTag()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "小物" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });
        await SaveItemAsync("2", new UserTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        var usage = await _service.LoadUsageAsync();

        Assert.Equal(2, usage.Single(entry => entry.Top == "衣装").ItemCount);
        Assert.Equal(2, usage.Single(entry => entry.Top == "衣装").SubCounts["制服"]);
        Assert.Equal(0, usage.Single(entry => entry.Top == "小物").ItemCount);
    }

    /// <summary>マスタに無い名前をitemが参照したままの状態。直せるのはこの画面だけ。</summary>
    [Fact]
    public async Task FindsNamesThatOnlyItemsStillReference()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "消えたタグ" });
        await SaveItemAsync("2", new UserTagAssignment { Top = "消えたタグ" }, new UserTagAssignment { Top = "衣装" });

        var orphan = Assert.Single(await _service.LoadOrphansAsync());

        Assert.Equal("消えたタグ", orphan.Top);
        Assert.Equal(2, orphan.ItemCount);
    }

    /// <summary>
    /// サブも拾う。集計はitem側の全サブを数えているのに画面はマスタにある分しか
    /// 出さないので、ずれたサブ名はどこにも表示されないまま絞り込みから消える。
    /// </summary>
    [Fact]
    public async Task FindsSubLevelsThatOnlyItemsStillReference()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服", "消えたサブ"] });

        var orphan = Assert.Single(await _service.LoadOrphansAsync());

        Assert.Equal("衣装", orphan.Top);
        Assert.Equal("消えたサブ", orphan.Sub);
        Assert.True(orphan.IsSub);
        Assert.Equal(1, orphan.ItemCount);
    }

    /// <summary>
    /// トップが無いときは配下のサブまでは出さない。トップを直せばサブも付いてくるので、
    /// 二重に並べても直す手が増えるだけ。
    /// </summary>
    [Fact]
    public async Task DoesNotListSubsUnderAMissingTop()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "消えたタグ", Subs = ["制服", "私服"] });

        var orphan = Assert.Single(await _service.LoadOrphansAsync());

        Assert.Equal("消えたタグ", orphan.Top);
        Assert.Null(orphan.Sub);
    }

    /// <summary>サブは同じトップの中だけで数える。別のトップの同名サブとは混ぜない。</summary>
    [Fact]
    public async Task CountsOrphanSubsPerTop()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "小物" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["無いサブ"] });
        await SaveItemAsync("2", new UserTagAssignment { Top = "小物", Subs = ["無いサブ"] });

        var orphans = await _service.LoadOrphansAsync();

        Assert.Equal(2, orphans.Count);
        Assert.All(orphans, orphan => Assert.Equal(1, orphan.ItemCount));
    }

    [Fact]
    public async Task MovesASubLevelToAnotherTop()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服", Memo = "学校のもの" }] },
            new UserTagTop { Name = "小物" });

        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });

        var result = await _service.MoveSubAsync("衣装", "制服", "小物", dropEmptySourceTop: false);

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(1, result.ItemsGainedTop);
        Assert.Empty(result.Master.Tops[0].Subs);
        Assert.Equal("学校のもの", Assert.Single(result.Master.Tops[1].Subs).Memo);

        // itemは「これは制服だ」という判断を保つ。そのために移動先のトップも付ける
        var userTags = await UserTagsOfAsync("1");
        Assert.Empty(userTags.Single(entry => entry.Top == "衣装").Subs);
        Assert.Equal(["制服"], userTags.Single(entry => entry.Top == "小物").Subs);
    }

    /// <summary>移動先のトップが既に付いていれば、そこへ足すだけ。</summary>
    [Fact]
    public async Task AddsToTheExistingTopWhenTheItemAlreadyHasIt()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] },
            new UserTagTop { Name = "小物", Subs = [new UserTagSub { Name = "指輪" }] });

        await SaveItemAsync(
            "1",
            new UserTagAssignment { Top = "衣装", Subs = ["制服"] },
            new UserTagAssignment { Top = "小物", Subs = ["指輪"] });

        var result = await _service.MoveSubAsync("衣装", "制服", "小物", dropEmptySourceTop: false);

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(0, result.ItemsGainedTop);
        Assert.Equal(["指輪", "制服"], (await UserTagsOfAsync("1")).Single(entry => entry.Top == "小物").Subs);
    }

    /// <summary>移動先に同じ名前があれば統合になる。メモは出所付きで書き足す。</summary>
    [Fact]
    public async Task MergesWhenTheTargetTopAlreadyHasThatSub()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服", Memo = "学校のもの" }] },
            new UserTagTop { Name = "小物", Subs = [new UserTagSub { Name = "制服", Memo = "職業のもの" }] });

        var result = await _service.MoveSubAsync("衣装", "制服", "小物", dropEmptySourceTop: false);

        Assert.Empty(result.Master.Tops[0].Subs);
        var sub = Assert.Single(result.Master.Tops[1].Subs);
        Assert.Contains("職業のもの", sub.Memo);
        Assert.Contains("「衣装／制服」から統合：学校のもの", sub.Memo);
    }

    /// <summary>
    /// サブが無くなった元のトップは、既定では残す。そのトップがこのサブのためだけに
    /// 付いていたとは限らない（サブなしの単独指定もあり得る）ので、消す方は選ばせる。
    /// </summary>
    [Fact]
    public async Task DropsTheEmptiedSourceTopOnlyWhenAsked()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] },
            new UserTagTop { Name = "小物" });

        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });

        var result = await _service.MoveSubAsync("衣装", "制服", "小物", dropEmptySourceTop: true);

        Assert.Equal(1, result.ItemsSourceTopRemoved);
        Assert.Equal("小物", Assert.Single(await UserTagsOfAsync("1")).Top);

        // マスタ側のトップは消さない。他のitemが単独で使っていることがある
        Assert.Contains(result.Master.Tops, top => top.Name == "衣装");
    }

    /// <summary>他にサブが残るitemでは、元のトップを外さない。</summary>
    [Fact]
    public async Task KeepsTheSourceTopWhenOtherSubsRemain()
    {
        await SaveMasterAsync(
            new UserTagTop
            {
                Name = "衣装",
                Subs = [new UserTagSub { Name = "制服" }, new UserTagSub { Name = "私服" }],
            },
            new UserTagTop { Name = "小物" });

        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        var result = await _service.MoveSubAsync("衣装", "制服", "小物", dropEmptySourceTop: true);

        Assert.Equal(0, result.ItemsSourceTopRemoved);
        Assert.Equal(["私服"], (await UserTagsOfAsync("1")).Single(entry => entry.Top == "衣装").Subs);
    }

    /// <summary>押す前に影響が見えていないと、空になるトップの扱いを決めようがない。</summary>
    [Fact]
    public async Task PreviewsWhatTheMoveWillDo()
    {
        await SaveMasterAsync(
            new UserTagTop
            {
                Name = "衣装",
                Subs = [new UserTagSub { Name = "制服" }, new UserTagSub { Name = "私服" }],
            },
            new UserTagTop { Name = "小物" });

        // 制服だけ → 移すと「衣装」が空になる
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });

        // 私服も持つ → 「衣装」は残る
        await SaveItemAsync("2", new UserTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        // 「小物」を既に持つ → トップは増えない
        await SaveItemAsync(
            "3",
            new UserTagAssignment { Top = "衣装", Subs = ["制服"] },
            new UserTagAssignment { Top = "小物" });

        // 制服を持たない → 対象外
        await SaveItemAsync("4", new UserTagAssignment { Top = "衣装", Subs = ["私服"] });

        var preview = await _service.PreviewMoveSubAsync("衣装", "制服", "小物");

        Assert.Equal(3, preview.ItemCount);
        Assert.Equal(2, preview.ItemsLeavingEmptyTop);
        Assert.Equal(2, preview.ItemsGainingTop);
    }

    [Fact]
    public async Task IgnoresAMoveWhenTheSubOrTopIsGone()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] });

        Assert.Equal(0, (await _service.MoveSubAsync("衣装", "制服", "無いトップ", dropEmptySourceTop: false)).ItemsUpdated);
        Assert.Equal(0, (await _service.MoveSubAsync("衣装", "無いサブ", "衣装", dropEmptySourceTop: false)).ItemsUpdated);
        Assert.Equal(["制服"], Assert.Single(_store.UserTags.Load().Tops).Subs.Select(sub => sub.Name));
    }

    [Fact]
    public async Task RenamesTheTagInTheMasterAndInEveryItem()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });

        var result = await _service.RenameTopAsync("衣装", "アバター衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.False(result.WasMerged);
        Assert.Equal("アバター衣装", Assert.Single(result.Master.Tops).Name);
        Assert.Equal("アバター衣装", Assert.Single(await UserTagsOfAsync("1")).Top);
        // サブレベルはトップに従属するので、そのまま連れて行く
        Assert.Equal("制服", Assert.Single(Assert.Single(await UserTagsOfAsync("1")).Subs));
    }

    /// <summary>大文字と小文字だけの変更は、統合ではなく改名として通す（前は黙って何もしなかった）。</summary>
    [Fact]
    public async Task RenamesWhenOnlyTheLetterCaseChanges()
    {
        await SaveMasterAsync(new UserTagTop { Name = "vrchat", Subs = [new UserTagSub { Name = "ギミック" }] });
        await SaveItemAsync("1", new UserTagAssignment { Top = "vrchat", Subs = ["ギミック"] });

        var top = await _service.RenameTopAsync("vrchat", "VRChat");
        var sub = await _service.RenameSubAsync("VRChat", "ギミック", "ギミック改");

        Assert.False(top.WasMerged);
        Assert.Equal(1, top.ItemsUpdated);
        Assert.Equal("VRChat", Assert.Single(_store.UserTags.Load().Tops).Name);
        Assert.Equal("VRChat", Assert.Single(await UserTagsOfAsync("1")).Top);
        Assert.Equal(1, sub.ItemsUpdated);
    }

    /// <summary>マスタに無い名前も改名できる。参照が壊れたitemを直す唯一の手段なので。</summary>
    [Fact]
    public async Task CanRenameANameThatOnlyItemsReference()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "消えたタグ" });

        var result = await _service.RenameTopAsync("消えたタグ", "衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal("衣装", Assert.Single(await UserTagsOfAsync("1")).Top);
        Assert.Empty(await _service.LoadOrphansAsync());
    }

    /// <summary>既にある名前へ改名すると統合。両方付いていたitemでは1件にまとめる。</summary>
    [Fact]
    public async Task MergesWhenRenamedOntoAnExistingTag()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "小物", Subs = [new UserTagSub { Name = "指輪" }] },
            new UserTagTop { Name = "アクセサリ", Subs = [new UserTagSub { Name = "ピアス" }] });

        await SaveItemAsync(
            "1",
            new UserTagAssignment { Top = "小物", Subs = ["指輪"] },
            new UserTagAssignment { Top = "アクセサリ", Subs = ["ピアス"] });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        Assert.True(result.WasMerged);
        Assert.Equal("小物", Assert.Single(result.Master.Tops).Name);
        Assert.Equal(["指輪", "ピアス"], result.Master.Tops[0].Subs.Select(sub => sub.Name));

        var assignment = Assert.Single(await UserTagsOfAsync("1"));
        Assert.Equal("小物", assignment.Top);
        Assert.Equal(["指輪", "ピアス"], assignment.Subs);
    }

    [Fact]
    public async Task RemovesTheTagFromEveryItemWhenDeleted()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "小物" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装" }, new UserTagAssignment { Top = "小物" });

        var result = await _service.DeleteTopAsync("衣装");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal("小物", Assert.Single(result.Master.Tops).Name);
        Assert.Equal("小物", Assert.Single(await UserTagsOfAsync("1")).Top);
    }

    /// <summary>
    /// userTagが空になったitem数を数える。編集の対象に戻るので、黙って進めてはいけない。
    /// </summary>
    [Fact]
    public async Task ReportsHowManyItemsAreLeftWithNoTagAtAll()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "小物" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装" });
        await SaveItemAsync("2", new UserTagAssignment { Top = "衣装" }, new UserTagAssignment { Top = "小物" });

        var result = await _service.DeleteTopAsync("衣装");

        Assert.Equal(2, result.ItemsUpdated);
        Assert.Equal(1, result.ItemsLeftUntagged);
    }

    [Fact]
    public async Task RenamesASubLevelWithoutTouchingOtherTops()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] },
            new UserTagTop { Name = "小物", Subs = [new UserTagSub { Name = "制服" }] });

        await SaveItemAsync(
            "1",
            new UserTagAssignment { Top = "衣装", Subs = ["制服"] },
            new UserTagAssignment { Top = "小物", Subs = ["制服"] });

        await _service.RenameSubAsync("衣装", "制服", "学生服");

        var userTags = await UserTagsOfAsync("1");
        Assert.Equal(["学生服"], userTags.Single(entry => entry.Top == "衣装").Subs);
        Assert.Equal(["制服"], userTags.Single(entry => entry.Top == "小物").Subs);
    }

    [Fact]
    public async Task DropsASubLevelFromItemsWhenDeleted()
    {
        await SaveMasterAsync(new UserTagTop
        {
            Name = "衣装",
            Subs = [new UserTagSub { Name = "制服" }, new UserTagSub { Name = "私服" }],
        });

        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服", "私服"] });

        var result = await _service.DeleteSubAsync("衣装", "制服");

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(["私服"], Assert.Single(result.Master.Tops).Subs.Select(sub => sub.Name));
        // トップは残す。サブを消しただけで分類そのものが外れては困る
        Assert.Equal(["私服"], Assert.Single(await UserTagsOfAsync("1")).Subs);
    }

    /// <summary>統合したサブが重複しないこと。同じ名前が2つ並ぶと絞り込みが分裂する。</summary>
    [Fact]
    public async Task DoesNotLeaveDuplicateSubsAfterMerging()
    {
        await SaveMasterAsync(new UserTagTop
        {
            Name = "衣装",
            Subs = [new UserTagSub { Name = "制服" }, new UserTagSub { Name = "学生服" }],
        });

        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服", "学生服"] });

        await _service.RenameSubAsync("衣装", "制服", "学生服");

        Assert.Equal(["学生服"], Assert.Single(await UserTagsOfAsync("1")).Subs);
        Assert.Equal(["学生服"], Assert.Single((await _service.LoadUsageAsync())).SubCounts.Keys);
    }

    [Fact]
    public async Task WritesTheMemoWithoutTouchingItems()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装", Subs = ["制服"] });

        await _service.SetMemoAsync("衣装", null, "  アバターに着せるもの全般  ");
        await _service.SetMemoAsync("衣装", "制服", "学校のもの");

        var top = Assert.Single(_store.UserTags.Load().Tops);
        Assert.Equal("アバターに着せるもの全般", top.Memo);
        Assert.Equal("学校のもの", Assert.Single(top.Subs).Memo);
        Assert.Equal("衣装", Assert.Single(await UserTagsOfAsync("1")).Top);
    }

    /// <summary>
    /// メモは「何をここに入れるか」の基準なので、統合で片方が黙って消えると
    /// 判断の根拠だけが失われる。どちらから来た文か分かる形で書き足す。
    /// </summary>
    [Fact]
    public async Task CarriesTheSourceMemoIntoTheMergedTag()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "小物", Memo = "身に着ける小さいもの" },
            new UserTagTop { Name = "アクセサリ", Memo = "指輪やピアス" });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        var memo = Assert.Single(result.Master.Tops).Memo;
        Assert.Contains("身に着ける小さいもの", memo);
        Assert.Contains("「アクセサリ」から統合：指輪やピアス", memo);
    }

    [Fact]
    public async Task UsesTheSourceMemoWhenTheTargetHadNone()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "小物" },
            new UserTagTop { Name = "アクセサリ", Memo = "指輪やピアス" });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        Assert.Equal("「アクセサリ」から統合：指輪やピアス", Assert.Single(result.Master.Tops).Memo);
    }

    [Fact]
    public async Task LeavesTheMemoAloneWhenTheSourceHadNone()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "小物", Memo = "身に着ける小さいもの" },
            new UserTagTop { Name = "アクセサリ" });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        Assert.Equal("身に着ける小さいもの", Assert.Single(result.Master.Tops).Memo);
    }

    /// <summary>同じ文を何度も書き足さない。統合を繰り返すと読めなくなる。</summary>
    [Fact]
    public async Task DoesNotAppendAMemoThatIsAlreadyThere()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "小物", Memo = "「アクセサリ」から統合：指輪やピアス" },
            new UserTagTop { Name = "アクセサリ", Memo = "指輪やピアス" });

        var result = await _service.RenameTopAsync("アクセサリ", "小物");

        Assert.Equal("「アクセサリ」から統合：指輪やピアス", Assert.Single(result.Master.Tops).Memo);
    }

    [Fact]
    public async Task CarriesTheSourceMemoWhenSubLevelsAreMerged()
    {
        await SaveMasterAsync(new UserTagTop
        {
            Name = "衣装",
            Subs =
            [
                new UserTagSub { Name = "学生服", Memo = "学校のもの" },
                new UserTagSub { Name = "制服", Memo = "職業のものも含む" },
            ],
        });

        var result = await _service.RenameSubAsync("衣装", "制服", "学生服");

        var sub = Assert.Single(Assert.Single(result.Master.Tops).Subs);
        Assert.Equal("学生服", sub.Name);
        Assert.Contains("学校のもの", sub.Memo);
        Assert.Contains("「制服」から統合：職業のものも含む", sub.Memo);
    }

    /// <summary>
    /// 並びは検索の絞り込みにも編集の候補にも出るので、追加順に縛られないようにする。
    /// itemは名前で参照しているので、並べ替えでitemに触る必要はない。
    /// </summary>
    [Fact]
    public async Task ReordersTopLevelsWithoutTouchingItems()
    {
        await SaveMasterAsync(
            new UserTagTop { Name = "衣装" },
            new UserTagTop { Name = "小物" },
            new UserTagTop { Name = "ギミック" });

        await SaveItemAsync("1", new UserTagAssignment { Top = "小物" });

        var master = await _service.ReorderAsync(null, ["ギミック", "衣装", "小物"]);

        Assert.Equal(["ギミック", "衣装", "小物"], master.Tops.Select(top => top.Name));
        Assert.Equal("小物", Assert.Single(await UserTagsOfAsync("1")).Top);
    }

    [Fact]
    public async Task ReordersSubLevelsWithinOneTop()
    {
        await SaveMasterAsync(
            new UserTagTop
            {
                Name = "衣装",
                Subs = [new UserTagSub { Name = "制服" }, new UserTagSub { Name = "私服" }],
            },
            new UserTagTop { Name = "小物", Subs = [new UserTagSub { Name = "指輪" }] });

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
            new UserTagTop { Name = "衣装" },
            new UserTagTop { Name = "小物" },
            new UserTagTop { Name = "ギミック" });

        var master = await _service.ReorderAsync(null, ["ギミック"]);

        Assert.Equal(["ギミック", "衣装", "小物"], master.Tops.Select(top => top.Name));
    }

    [Fact]
    public async Task IgnoresAReorderForATopThatIsGone()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "制服" }] });

        var master = await _service.ReorderAsync("無いトップ", ["制服"]);

        Assert.Equal(["制服"], Assert.Single(master.Tops).Subs.Select(sub => sub.Name));
    }

    /// <summary>
    /// 手で書いた JSON の `null` で落ちない（2026-09-18。`"subs": null` と書いた写しで
    /// タグの管理も検索も落ちた）。JSONは人が直せる形を保つ方針なので、読む側が空として受ける。
    /// 2026-09-20 に欄ごとの手当てをやめ、保存の設定1か所（`Storage/EmptyForNull`）で効かせている。
    /// </summary>
    [Fact]
    public void ReadsNullListsAsEmpty()
    {
        var master = System.Text.Json.JsonSerializer.Deserialize<UserTagMaster>(
            """{ "tops": [ { "name": "衣装", "subs": null } ] }""",
            Storage.JsonStore.Options);

        Assert.Empty(Assert.Single(master!.Tops).Subs);

        var assignment = System.Text.Json.JsonSerializer.Deserialize<UserTagAssignment>(
            """{ "top": "衣装", "subs": null }""",
            Storage.JsonStore.Options);

        Assert.Empty(assignment!.Subs);
    }

    [Fact]
    public async Task IgnoresARenameThatChangesNothing()
    {
        await SaveMasterAsync(new UserTagTop { Name = "衣装" });
        await SaveItemAsync("1", new UserTagAssignment { Top = "衣装" });

        Assert.Equal(0, (await _service.RenameTopAsync("衣装", "   ")).ItemsUpdated);
        Assert.Equal(0, (await _service.RenameTopAsync("衣装", "衣装")).ItemsUpdated);
        Assert.Equal("衣装", Assert.Single(_store.UserTags.Load().Tops).Name);
    }
}
