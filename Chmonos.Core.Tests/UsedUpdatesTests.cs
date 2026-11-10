using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>中身の更新の控えと、促しが消える条件（<c>docs/spec/used-updates.md</c>）。</summary>
public class UsedUpdatesTests
{
    private static readonly DateTimeOffset Found = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static LocalFileRecord File(string hash, bool detached = false, bool old = false) => new()
    {
        Hash = hash,
        Paths = old ? [] : [$@"D:\fake\{hash}.zip"],
        SizeBytes = 1,
        Detached = detached,
        Replaced = old ? new ReplacedVersion(@"D:\fake\old.zip", Found) : null,
    };

    private static ItemRecord Item(LocalBlock local) => new()
    {
        Id = "1",
        Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.UnixEpoch },
        Local = local,
    };

    private static LocalBlock Updated(params LocalFileRecord[] files)
    {
        var local = new LocalBlock { LocalFiles = files };
        return local with { ContentUpdate = UsedUpdates.Found(local, Found) };
    }

    [Fact]
    public void 控えは持っているファイルだけで_外した物と古い版は入れない()
    {
        var local = Updated(File("A"), File("B", detached: true), File("C", old: true));

        Assert.Equal(["A"], local.ContentUpdate!.FilesBefore);
        Assert.True(local.HasPendingContentUpdate);
    }

    [Fact]
    public void 新しいファイルを取り込むと商品の促しは消える()
    {
        var local = Updated(File("A"));

        var imported = local with { LocalFiles = [File("A"), File("NEW")] };

        Assert.True(imported.HasFilesNewerThanUpdate);
        Assert.False(imported.HasPendingContentUpdate);
    }

    [Fact]
    public void 新しいファイルでも外した物は数えない()
    {
        var local = Updated(File("A"));

        var detached = local with { LocalFiles = [File("A"), File("NEW", detached: true)] };

        Assert.True(detached.HasPendingContentUpdate);
    }

    [Fact]
    public void 見つけた後に登録したフォルダも新しい物に数える()
    {
        var local = Updated(File("A"));

        var before = local with { LocalFolders = [new LocalFolderRecord { Path = @"D:\fake\old", RegisteredAt = Found.AddDays(-1) }] };
        var after = local with { LocalFolders = [new LocalFolderRecord { Path = @"D:\fake\new", RegisteredAt = Found.AddDays(1) }] };

        Assert.True(before.HasPendingContentUpdate);
        Assert.False(after.HasPendingContentUpdate);
    }

    [Fact]
    public void 確認したを押すと商品の促しは消える()
    {
        var local = Updated(File("A"));

        var checkedLocal = local with { ContentUpdate = local.ContentUpdate! with { CheckedAt = Found.AddDays(1) } };

        Assert.False(checkedLocal.HasPendingContentUpdate);
    }

    [Fact]
    public void 改変の行は_使ったファイルが更新の前からの物なら出て_新しいファイルで送り直すと消える()
    {
        var item = Item(Updated(File("A")) with { LocalFiles = [File("A"), File("NEW")] });

        Assert.True(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A" }));
        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "NEW" }));
    }

    [Fact]
    public void 改変の行の確認は_その更新より後に押した物だけ効く()
    {
        var item = Item(Updated(File("A")));

        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A", UpdateCheckedAt = Found.AddDays(1) }));
        Assert.True(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A", UpdateCheckedAt = Found.AddDays(-1) }));
    }

    [Fact]
    public void 使ったファイルが分からない行は商品の印に従い_外した行には出さない()
    {
        var pending = Item(Updated(File("A")));
        var imported = Item(Updated(File("A")) with { LocalFiles = [File("A"), File("NEW")] });
        var unknown = new ModificationMember { ItemId = "1" };

        Assert.True(UsedUpdates.IsMemberBehind(pending, unknown));
        Assert.False(UsedUpdates.IsMemberBehind(imported, unknown));
        Assert.False(UsedUpdates.IsMemberBehind(pending, new ModificationMember { ItemId = "1", FileHash = "A", Detached = true }));
    }

    [Fact]
    public void 更新を見つけていなければ何も出さない()
    {
        var item = Item(new LocalBlock { LocalFiles = [File("A")] });

        Assert.False(item.Local.HasPendingContentUpdate);
        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A" }));
    }

    // ---- 特定のアバター向けの更新（ユーザ判断 2026-11-09）。名前・ID は作り物 ----

    private static readonly Dictionary<string, IReadOnlyList<string>> AvatarNames = new()
    {
        ["A1"] = ["アオイ"],
        ["A2"] = ["ルリ"],
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> BaseNames = new() { ["作り物の素体"] = ["作り物の素体"] };

    private static NotificationDiff History(params string[] added) => new()
    {
        Field = "更新履歴",
        Lines = added.Select(text => new NotificationLine { Kind = NotificationLineKind.Added, Text = text }).ToList(),
    };

    [Fact]
    public void 更新の行が全部アバターの名前を含めば_そのアバター向けになる()
    {
        var targets = UsedUpdateKind.TargetsOf([History("2026/09/12 アオイくんの瞳の縁を再修正しました。", "ver1.1")], AvatarNames, BaseNames);

        Assert.Equal(["A1"], targets!.Avatars);
        Assert.Empty(targets.Bases);
    }

    [Fact]
    public void 名前の無い更新の行が1行でもあれば全部向け()
    {
        Assert.Null(UsedUpdateKind.TargetsOf([History("アオイの瞳を修正", "影のマスクを追加")], AvatarNames, BaseNames));
    }

    [Fact]
    public void 共通素体の名前でも向き先になる()
    {
        var targets = UsedUpdateKind.TargetsOf([History("作り物の素体用のメッシュを修正")], AvatarNames, BaseNames);

        Assert.Equal(["作り物の素体"], targets!.Bases);
    }

    [Fact]
    public void 特定のアバター向けの更新は_そのアバターか同じ素体のアバターの改変だけに当たる()
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "A1", DisplayName = "アオイ" },
                new AvatarRegistryEntry { ItemId = "A2", DisplayName = "ルリ" },
                new AvatarRegistryEntry { ItemId = "A3", DisplayName = "モモ", BaseName = "作り物の素体" },
            ],
        };
        var forAoi = UsedUpdates.Found(new LocalBlock(), Found, new UpdateTargets(["A1"], []));
        var forBase = UsedUpdates.Found(new LocalBlock(), Found, new UpdateTargets([], ["作り物の素体"]));

        Assert.True(UsedUpdates.AppliesTo(forAoi, "A1", registry));
        Assert.False(UsedUpdates.AppliesTo(forAoi, "A2", registry));
        Assert.True(UsedUpdates.AppliesTo(forAoi, null, registry));
        Assert.True(UsedUpdates.AppliesTo(forBase, "A3", registry));
        Assert.False(UsedUpdates.AppliesTo(forBase, "A1", registry));

        // 素体の名前は大文字小文字だけの違いを同じ素体とみなす（AvatarBaseNames。前はここだけ Ordinal で、別の素体と見ていた）
        var latin = new AvatarRegistry { Entries = [new AvatarRegistryEntry { ItemId = "A4", DisplayName = "作り物", BaseName = "SampleBody" }] };
        Assert.True(UsedUpdates.AppliesTo(UsedUpdates.Found(new LocalBlock(), Found, new UpdateTargets([], ["samplebody"])), "A4", latin));
    }

    [Fact]
    public void 使っている所は_特定のアバター向けならそのアバターの改変と_アバターの分からないプロジェクトだけ数える()
    {
        var registry = new AvatarRegistry();
        var local = new LocalBlock { LocalFiles = [File("A")] };
        var item = Item(local with { ContentUpdate = UsedUpdates.Found(local, Found, new UpdateTargets(["A1"], [])) });
        ModificationRecord Mod(string id, string avatar, string? project = null) => new()
        {
            Id = id,
            AvatarItemId = avatar,
            Name = id,
            UnityProject = project,
            Members = [new ModificationMember { ItemId = "1" }],
        };
        var scan = new UnityProjectScan([new UnityProjectMatch("1", 1, 1)], new Dictionary<string, IReadOnlyList<UnityImageMatch>>());

        var used = new UsedItems(
            [Mod("m1", "A1"), Mod("m2", "A2", @"D:\p2")],
            new Dictionary<string, UnityProjectScan> { [@"D:\p2"] = scan, [@"D:\open"] = scan });

        // 改変は A1 の分だけ。プロジェクトは A2 の改変に紐付いた p2 を除き、紐付きの無い open だけ
        Assert.Equal(new UsedBy(1, 1), used.Of(item, registry));
        Assert.Equal(new UsedBy(2, 2), used.Of("1"));
    }
}
