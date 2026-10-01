using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

public sealed class ModificationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly ModificationService _service;

    public ModificationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-modsvc-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new ModificationService(_store);
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

    private Task SaveAvatarAsync(params string[] itemIds)
        => _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = itemIds.Select(id => new AvatarRegistryEntry { ItemId = id }).ToList(),
        });

    private Task SaveItemAsync(string id, string name, string? category = null)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = name,
                FetchedAt = DateTimeOffset.Now,
                Category = category is null ? null : new BoothCategory { Id = 1, Name = category },
            },
            Local = new LocalBlock(),
        });

    [Fact]
    public async Task 作ると読み戻せる()
    {
        await SaveAvatarAsync("7841391");

        var created = await _service.CreateAsync("7841391", "普段着");

        Assert.NotNull(created);
        Assert.Equal("普段着", created.Name);
        Assert.Equal("7841391", created.AvatarItemId);
        Assert.Empty(created.Members);

        var mine = await _service.LoadForAvatarAsync("7841391");
        Assert.Equal("普段着", Assert.Single(mine).Name);
    }

    [Fact]
    public async Task 名前の前後の空白は落とす()
    {
        await SaveAvatarAsync("7841391");

        var created = await _service.CreateAsync("7841391", "  普段着  ");

        Assert.Equal("普段着", created!.Name);
    }

    [Fact]
    public async Task 名前が空なら作らない()
    {
        await SaveAvatarAsync("7841391");

        Assert.Null(await _service.CreateAsync("7841391", "   "));
        Assert.Empty((await _service.LoadAllAsync()).Modifications);
    }

    [Fact]
    public async Task 同じ名前でも作れる()
    {
        // 「普段着」を作り直したいとき、古い方を消す前に新しい方を作れないと困る
        await SaveAvatarAsync("7841391");

        var first = await _service.CreateAsync("7841391", "普段着");
        var second = await _service.CreateAsync("7841391", "普段着");

        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(2, (await _service.LoadForAvatarAsync("7841391")).Count);
    }

    [Fact]
    public async Task 同じ名前があることを先に知らせられる()
    {
        await SaveAvatarAsync("7841391");
        await _service.CreateAsync("7841391", "普段着");

        Assert.True(await _service.HasSameNameAsync("7841391", "普段着"));

        // 前後の空白は落として比べる（作るときも落とすので、揃えないと擦り抜ける）
        Assert.True(await _service.HasSameNameAsync("7841391", "  普段着  "));

        Assert.False(await _service.HasSameNameAsync("7841391", "制服"));

        // 読みは別物として扱う。同じかどうかは人にしか分からない
        Assert.False(await _service.HasSameNameAsync("7841391", "ふだんぎ"));

        // 別のアバターの改変は数えない
        Assert.False(await _service.HasSameNameAsync("4897493", "普段着"));
    }

    [Fact]
    public async Task 別のアバターの改変は混ざらない()
    {
        await SaveAvatarAsync("7841391", "4897493");
        await _service.CreateAsync("7841391", "普段着");
        await _service.CreateAsync("4897493", "制服");

        Assert.Equal("普段着", Assert.Single(await _service.LoadForAvatarAsync("7841391")).Name);
        Assert.Equal("制服", Assert.Single(await _service.LoadForAvatarAsync("4897493")).Name);
    }

    [Fact]
    public async Task 登録簿に無いアバターはその場で足す()
    {
        // 「登録簿に入るまで改変が作れない」を避ける
        await SaveAvatarAsync();
        await SaveItemAsync("7841391", "【オリジナル3Dモデル】Wendy", "3Dキャラクター");

        await _service.CreateAsync("7841391", "普段着");

        var entry = Assert.Single(_store.Avatars.Load().Entries);
        Assert.Equal("7841391", entry.ItemId);
        Assert.Equal("【オリジナル3Dモデル】Wendy", entry.BoothName);
        Assert.Equal("3Dキャラクター", entry.Category);

        // 判定は検出の仕事。人が改変を作った事実だけを書く
        Assert.Null(entry.AvatarOverride);
    }

    [Fact]
    public async Task 商品が手元に無くても登録簿に足せる()
    {
        await SaveAvatarAsync();

        await _service.CreateAsync("9999999", "試作");

        var entry = Assert.Single(_store.Avatars.Load().Entries);
        Assert.Equal("9999999", entry.ItemId);
        Assert.Null(entry.BoothName);
    }

    [Fact]
    public async Task 既に登録簿にあるアバターは触らない()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "7841391", DisplayName = "ウェンディ" }],
        });

        await _service.CreateAsync("7841391", "普段着");

        var entry = Assert.Single(_store.Avatars.Load().Entries);
        Assert.Equal("ウェンディ", entry.DisplayName);
    }

    [Fact]
    public async Task 消せる()
    {
        await SaveAvatarAsync("7841391");
        var created = await _service.CreateAsync("7841391", "普段着");

        Assert.True(await _service.DeleteAsync(created!.Id));
        Assert.Empty(await _service.LoadForAvatarAsync("7841391"));
    }

    [Fact]
    public async Task 無い改変を消そうとしたら偽を返す()
        => Assert.False(await _service.DeleteAsync("mod-00000000"));

    // ---- 名前・メモ・プロジェクト ----

    private async Task<string> NewAsync(string name = "普段着", string avatar = "7841391")
    {
        await SaveAvatarAsync(avatar);
        return (await _service.CreateAsync(avatar, name))!.Id;
    }

    [Fact]
    public async Task 名前を変えられる()
    {
        var id = await NewAsync("テスト");

        Assert.True(await _service.RenameAsync(id, "  普段着  "));
        Assert.Equal("普段着", (await _service.LoadAsync(id))!.Name);
    }

    [Fact]
    public async Task 空の名前には変えない()
    {
        var id = await NewAsync("普段着");

        Assert.False(await _service.RenameAsync(id, "   "));
        Assert.Equal("普段着", (await _service.LoadAsync(id))!.Name);
    }

    [Fact]
    public async Task メモを書ける_空白だけならnullに戻す()
    {
        var id = await NewAsync();

        await _service.SetMemoAsync(id, "  袖の貫通を直した  ");
        Assert.Equal("袖の貫通を直した", (await _service.LoadAsync(id))!.Memo);

        await _service.SetMemoAsync(id, "   ");
        Assert.Null((await _service.LoadAsync(id))!.Memo);
    }

    [Fact]
    public async Task プロジェクトを紐付けて外せる()
    {
        var id = await NewAsync();

        await _service.SetProjectAsync(id, @"D:\work\vrchat\VRChatProjects\kip01");
        var loaded = await _service.LoadAsync(id);
        Assert.True(loaded!.HasUnityProject);

        await _service.SetProjectAsync(id, null);
        Assert.False((await _service.LoadAsync(id))!.HasUnityProject);
    }

    [Fact]
    public async Task 触ると更新日時が動く()
    {
        var id = await NewAsync();

        // 更新日時を1日前へずらしてから触る。作ってすぐ触ると、時計の刻み（Windows では約15ms）の内に収まって
        // 同じ時刻になることがあり、待つ長さで揺れていた
        var created = (await _store.Modifications.LoadAsync(id))!;
        var before = created.UpdatedAt.AddDays(-1);
        await _store.Modifications.SaveAsync(created with { UpdatedAt = before });

        await _service.SetMemoAsync(id, "何か");

        Assert.True((await _service.LoadAsync(id))!.UpdatedAt > before);
    }

    // ---- 構成物 ----

    private static ModificationMember Member(string itemId, string? hash = null)
        => new() { ItemId = itemId, FileHash = hash };

    /// <summary>
    /// 今その位置に並んでいる行。**操作は位置ではなく行そのもので指す**（J5）ので、
    /// 試験からも保存されている行を渡す（足した日時はサービスが入れるため、作った値とは別物）。
    /// </summary>
    private async Task<ModificationMember> MemberAtAsync(string id, int index)
    {
        var members = (await _service.LoadAsync(id))!.Members;
        return index >= 0 && index < members.Count
            ? members[index]

            // 範囲外を指したときの試験用。どの行とも一致しない身元を返す
            : new ModificationMember { ItemId = "無い行", AddedAt = DateTimeOffset.MinValue };
    }

    [Fact]
    public async Task 足すと末尾に付く()
    {
        // 並びが導入の順なので、後から来たものは後ろ
        var id = await NewAsync();

        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2"));

        var loaded = await _service.LoadAsync(id);
        Assert.Equal(["1", "2"], loaded!.Members.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 足した時刻はサービスが入れる()
    {
        var id = await NewAsync();

        await _service.AddMemberAsync(id, Member("1"));

        Assert.NotEqual(default, (await _service.LoadAsync(id))!.Members[0].AddedAt);
    }

    [Fact]
    public async Task 同じ商品を2回足せる()
    {
        // 別のバージョンを重ねることがある
        var id = await NewAsync();

        await _service.AddMemberAsync(id, Member("1", "AAA"));
        await _service.AddMemberAsync(id, Member("1", "BBB"));

        Assert.Equal(2, (await _service.LoadAsync(id))!.Members.Count);
    }

    [Fact]
    public async Task 位置で外せる()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2"));
        await _service.AddMemberAsync(id, Member("3"));

        await _service.RemoveMemberAsync(id, await MemberAtAsync(id, 1));

        Assert.Equal(["1", "3"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 外しても行と記録と位置は残り戻せる()
    {
        // 商品の手元のファイルと同じく戻せるようにする（ユーザ指示 2026-09-19）。前は外すと使ったファイルの記録ごと消えた
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2", hash: "ZIP2") with { Package = "a/b.unitypackage" });
        await _service.AddMemberAsync(id, Member("3"));

        await _service.SetMemberDetachedAsync(id, await MemberAtAsync(id, 1), detached: true);
        var detached = (await _service.LoadAsync(id))!;

        Assert.Equal(["1", "2", "3"], detached.Members.Select(member => member.ItemId));
        Assert.True(detached.Members[1].Detached);
        Assert.Equal("ZIP2", detached.Members[1].FileHash);
        Assert.Equal("a/b.unitypackage", detached.Members[1].Package);
        Assert.Equal(["1", "3"], detached.UsedMembers.Select(member => member.ItemId));

        await _service.SetMemberDetachedAsync(id, await MemberAtAsync(id, 1), detached: false);
        var restored = (await _service.LoadAsync(id))!;

        Assert.False(restored.Members[1].Detached);
        Assert.Equal(["1", "2", "3"], restored.UsedMembers.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 外した商品はこの商品を使った改変に数えない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("5901276"));
        await _service.SetMemberDetachedAsync(id, await MemberAtAsync(id, 0), detached: true);

        Assert.Empty(await _service.LoadUsingItemAsync("5901276"));
    }

    [Fact]
    public async Task blueprintIDを持てて空で外せる()
    {
        var id = await NewAsync();

        await _service.SetBlueprintIdAsync(id, "  avtr_0123abcd-4567-89ab-cdef-0123456789ab ");
        Assert.Equal("avtr_0123abcd-4567-89ab-cdef-0123456789ab", (await _service.LoadAsync(id))!.BlueprintId);

        await _service.SetBlueprintIdAsync(id, " ");
        Assert.Null((await _service.LoadAsync(id))!.BlueprintId);
    }

    [Fact]
    public async Task 手で足した行を選んだファイルの行に置き換える()
    {
        // Unityへ送るときに選んだ物を記録する（ユーザ判断 2026-09-13）。2つ選べばその位置に2行並び、前後の並びは変えない
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("前"));
        await _service.AddMemberAsync(id, Member("クラゲ"));
        await _service.AddMemberAsync(id, Member("後"));
        var addedAt = (await _service.LoadAsync(id))!.Members[1].AddedAt;

        var replaced = await _service.ReplaceMemberAsync(id, await MemberAtAsync(id, 1),
        [
            new ModificationMember { ItemId = "クラゲ", FileHash = "AAA", Package = "Bracelet.v1.01/Bracelet.v1.01.unitypackage" },
            new ModificationMember { ItemId = "クラゲ", FileHash = "BBB", Package = "fullset.v1.06/fullset.v1.06.unitypackage" },
        ]);

        var members = (await _service.LoadAsync(id))!.Members;
        Assert.True(replaced);
        Assert.Equal(["前", "クラゲ", "クラゲ", "後"], members.Select(member => member.ItemId));
        Assert.Equal(["AAA", "BBB"], members.Skip(1).Take(2).Select(member => member.FileHash));
        Assert.All(members.Skip(1).Take(2), member => Assert.Equal(addedAt, member.AddedAt));
    }

    [Fact]
    public async Task ファイルの記録がある行は置き換えない()
    {
        // 選んでいる間に別の送り方で記録された行を、古い選択で上書きしない
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("クラゲ", "AAA"));

        var replaced = await _service.ReplaceMemberAsync(id, await MemberAtAsync(id, 0), [Member("クラゲ", "BBB")]);

        Assert.False(replaced);
        Assert.Equal("AAA", Assert.Single((await _service.LoadAsync(id))!.Members).FileHash);
    }

    [Fact]
    public async Task 別の商品の行は置き換えない()
    {
        // 選んでいる間に並びが変わると、同じ位置に別の商品が来ている
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("本体"));

        var replaced = await _service.ReplaceMemberAsync(id, await MemberAtAsync(id, 0), [Member("クラゲ", "AAA")]);

        Assert.False(replaced);
        Assert.Null(Assert.Single((await _service.LoadAsync(id))!.Members).FileHash);
    }

    [Fact]
    public async Task 置き換えで範囲外の位置を指しても壊さない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));

        Assert.False(await _service.ReplaceMemberAsync(id, await MemberAtAsync(id, 3), [Member("1", "AAA")]));
        Assert.False(await _service.ReplaceMemberAsync(id, await MemberAtAsync(id, -1), [Member("1", "AAA")]));
        Assert.Null(Assert.Single((await _service.LoadAsync(id))!.Members).FileHash);
    }

    [Fact]
    public async Task 範囲外の位置を指しても壊さない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));

        await _service.RemoveMemberAsync(id, await MemberAtAsync(id, 5));
        await _service.RemoveMemberAsync(id, await MemberAtAsync(id, -1));

        Assert.Single((await _service.LoadAsync(id))!.Members);
    }

    [Fact]
    public async Task 並べ替えられる()
    {
        // 依存物を後から思い出したときに直せないと困る
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("本体"));
        await _service.AddMemberAsync(id, Member("依存"));

        await _service.MoveMemberAsync(id, await MemberAtAsync(id, 1), -1);

        Assert.Equal(["依存", "本体"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 端では動かない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2"));

        await _service.MoveMemberAsync(id, await MemberAtAsync(id, 0), -1);
        await _service.MoveMemberAsync(id, await MemberAtAsync(id, 1), 1);

        Assert.Equal(["1", "2"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    /// <summary>
    /// **並びが変わっても、指した行に当たる**（ユーザ判断 2026-09-21・J5）。
    /// 画面は読み込んだ時点の位置を送るので、位置で指していると別の行を外していた。
    /// </summary>
    [Fact]
    public async Task 並びが変わっても指した行に当たる()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2"));
        await _service.AddMemberAsync(id, Member("3"));

        // 画面が「2」の行を掴む
        var target = await MemberAtAsync(id, 1);

        // その間に別の道で並びが変わった（先頭を後ろへ）
        await _service.MoveMemberAsync(id, await MemberAtAsync(id, 0), 2);
        Assert.Equal(["2", "3", "1"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));

        await _service.RemoveMemberAsync(id, target);

        // 位置で指していれば「3」が消えていた
        Assert.Equal(["3", "1"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    // ---- 逆引き ----

    [Fact]
    public async Task その商品を使っている改変を引ける()
    {
        await SaveAvatarAsync("7841391", "4897493");
        var a = (await _service.CreateAsync("7841391", "普段着"))!.Id;
        var b = (await _service.CreateAsync("4897493", "制服"))!.Id;
        await _service.AddMemberAsync(a, Member("5901276"));
        await _service.AddMemberAsync(b, Member("9999999"));

        var using5901276 = await _service.LoadUsingItemAsync("5901276");

        Assert.Equal("普段着", Assert.Single(using5901276).Name);
        Assert.Empty(await _service.LoadUsingItemAsync("0000000"));
    }

    [Fact]
    public async Task そのプロジェクトに紐づく改変を引ける()
    {
        var a = await NewAsync("普段着");
        var b = (await _service.CreateAsync("7841391", "制服"))!.Id;
        await _service.SetProjectAsync(a, @"D:\proj\kip01");
        await _service.SetProjectAsync(b, @"D:\proj\other");

        // **1つのプロジェクトに複数の改変が紐づく**ので、複数返る形にしてある
        Assert.Single(await _service.LoadForProjectAsync(@"D:\proj\kip01"));
        Assert.Empty(await _service.LoadForProjectAsync(@"D:\proj\居ない"));
    }

    [Fact]
    public async Task プロジェクトの大文字小文字と末尾の区切りを区別しない()
    {
        // 素の比較だと同じプロジェクトを別物と見て取り逃す
        var id = await NewAsync();
        await _service.SetProjectAsync(id, @"D:\proj\kip01");

        Assert.Single(await _service.LoadForProjectAsync(@"d:\PROJ\KIP01\"));
    }

    [Fact]
    public async Task プロジェクトが空なら何も返さない()
        => Assert.Empty(await _service.LoadForProjectAsync("   "));
}
