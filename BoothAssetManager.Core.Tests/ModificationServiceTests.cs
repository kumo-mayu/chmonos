using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Tests;

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
        var before = (await _service.LoadAsync(id))!.UpdatedAt;

        await Task.Delay(10);
        await _service.SetMemoAsync(id, "何か");

        Assert.True((await _service.LoadAsync(id))!.UpdatedAt > before);
    }

    // ---- 構成物 ----

    private static ModificationMember Member(string itemId, string? hash = null)
        => new() { ItemId = itemId, FileHash = hash };

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

        await _service.RemoveMemberAsync(id, 1);

        Assert.Equal(["1", "3"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 範囲外の位置を指しても壊さない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));

        await _service.RemoveMemberAsync(id, 5);
        await _service.RemoveMemberAsync(id, -1);

        Assert.Single((await _service.LoadAsync(id))!.Members);
    }

    [Fact]
    public async Task 並べ替えられる()
    {
        // 依存物を後から思い出したときに直せないと困る
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("本体"));
        await _service.AddMemberAsync(id, Member("依存"));

        await _service.MoveMemberAsync(id, 1, -1);

        Assert.Equal(["依存", "本体"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
    }

    [Fact]
    public async Task 端では動かない()
    {
        var id = await NewAsync();
        await _service.AddMemberAsync(id, Member("1"));
        await _service.AddMemberAsync(id, Member("2"));

        await _service.MoveMemberAsync(id, 0, -1);
        await _service.MoveMemberAsync(id, 1, 1);

        Assert.Equal(["1", "2"], (await _service.LoadAsync(id))!.Members.Select(member => member.ItemId));
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
