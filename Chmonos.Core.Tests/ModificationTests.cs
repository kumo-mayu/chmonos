using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

public sealed class ModificationIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 2, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void 接頭辞が付く()
        => Assert.StartsWith("mod-", ModificationId.For("7841391", "普段着", Now));

    [Fact]
    public void 同じ材料からは同じIDになる()
    {
        Assert.Equal(
            ModificationId.For("7841391", "普段着", Now),
            ModificationId.For("7841391", "普段着", Now));
    }

    [Fact]
    public void 名前の前後の空白は無視する()
    {
        Assert.Equal(
            ModificationId.For("7841391", "普段着", Now),
            ModificationId.For("7841391", "  普段着  ", Now));
    }

    [Fact]
    public void 同じ名前でも時刻が違えば別のIDになる()
    {
        // 「普段着」を作り直したいとき、古い方を消す前に新しい方を作れる必要がある
        Assert.NotEqual(
            ModificationId.For("7841391", "普段着", Now),
            ModificationId.For("7841391", "普段着", Now.AddSeconds(1)));
    }

    [Fact]
    public void アバターが違えば別のIDになる()
    {
        Assert.NotEqual(
            ModificationId.For("7841391", "普段着", Now),
            ModificationId.For("4897493", "普段着", Now));
    }

    [Fact]
    public void 長さは接頭辞と8桁()
        => Assert.Equal("mod-".Length + 8, ModificationId.For("1", "a", Now).Length);

    [Theory]
    [InlineData("mod-3f9c1b7e", true)]
    [InlineData("local-3f9c1b7e", false)]
    [InlineData("7841391", false)]
    [InlineData(null, false)]
    public void 改変のIDかを見分ける(string? id, bool expected)
        => Assert.Equal(expected, ModificationId.IsModification(id));
}

public sealed class ModificationMemberTests
{
    [Fact]
    public void ファイルのハッシュがあればUnityから入った分()
    {
        var member = new ModificationMember { ItemId = "1", FileHash = "9F2C" };

        Assert.True(member.HasFile);
    }

    [Fact]
    public void 手で足した分はハッシュが空のまま()
    {
        // 「どのファイルを使ったかは分からない」が正しい。推定で埋めない
        var member = new ModificationMember { ItemId = "1" };

        Assert.False(member.HasFile);
        Assert.Null(member.FileHash);
        Assert.Null(member.Package);
    }
}

public sealed class ModificationRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly ModificationRepository _repo;

    public ModificationRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-mod-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _repo = new ModificationRepository(_paths);
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

    private static ModificationRecord Record(
        string name,
        string avatar = "7841391",
        DateTimeOffset? createdAt = null)
    {
        var at = createdAt ?? DateTimeOffset.Now;
        return new ModificationRecord
        {
            Id = ModificationId.For(avatar, name, at),
            AvatarItemId = avatar,
            Name = name,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    [Fact]
    public async Task 書いて読み戻せる()
    {
        var record = Record("普段着") with
        {
            UnityProject = @"D:\work\vrchat\VRChatProjects\kip01",
            Memo = "袖の貫通を直した",
            Members =
            [
                new ModificationMember
                {
                    ItemId = "5901276",
                    FileHash = "9F2C4A1B",
                    Package = "Kuuta_ShapekeyAddon/BlendShare-0.0.10-User.unitypackage",
                },
                new ModificationMember { ItemId = "4897493", VariationId = 4821046 },
            ],
        };

        await _repo.SaveAsync(record);
        var loaded = await _repo.LoadAsync(record.Id);

        Assert.NotNull(loaded);
        Assert.Equal("普段着", loaded.Name);
        Assert.Equal(@"D:\work\vrchat\VRChatProjects\kip01", loaded.UnityProject);
        Assert.True(loaded.HasUnityProject);

        // 並びが導入の順。依存物が先
        Assert.Equal(["5901276", "4897493"], loaded.Members.Select(member => member.ItemId));
        Assert.True(loaded.Members[0].HasFile);
        Assert.False(loaded.Members[1].HasFile);
        Assert.Equal(4821046, loaded.Members[1].VariationId);
    }

    [Fact]
    public async Task 無いIDはnullを返す()
        => Assert.Null(await _repo.LoadAsync("mod-00000000"));

    [Fact]
    public async Task 保存すると1改変1ファイルになる()
    {
        await _repo.SaveAsync(Record("普段着"));
        await _repo.SaveAsync(Record("制服"));

        Assert.Equal(2, Directory.GetFiles(_paths.ModificationsDir, "*.json").Length);
    }

    [Fact]
    public async Task 新しく作った順に返す()
    {
        var old = Record("古い", createdAt: DateTimeOffset.Now.AddDays(-3));
        var recent = Record("新しい", createdAt: DateTimeOffset.Now);

        await _repo.SaveAsync(old);
        await _repo.SaveAsync(recent);

        var result = await _repo.LoadAllAsync();

        Assert.Equal(["新しい", "古い"], result.Modifications.Select(record => record.Name));
        Assert.Empty(result.FailedIds);
    }

    [Fact]
    public async Task 読めないファイルは飛ばして数を返す()
    {
        // 1件壊れたせいで一覧が空になるのは困る
        await _repo.SaveAsync(Record("生きている"));
        await File.WriteAllTextAsync(
            _paths.ModificationFile("mod-deadbeef"),
            "これはJSONではない");

        var result = await _repo.LoadAllAsync();

        Assert.Equal("生きている", Assert.Single(result.Modifications).Name);
        Assert.Equal(["mod-deadbeef"], result.FailedIds);
    }

    [Fact]
    public async Task 改変のIDで始まらないファイルは読まない()
    {
        await _repo.SaveAsync(Record("普段着"));
        await File.WriteAllTextAsync(Path.Combine(_paths.ModificationsDir, "メモ.json"), "{}");

        var result = await _repo.LoadAllAsync();

        Assert.Single(result.Modifications);
        Assert.Empty(result.FailedIds);
    }

    [Fact]
    public async Task 消すと画像も一緒に消える()
    {
        // 改変専用の画像なので、記録を消して画像だけ残すと行き場が無くなる
        var record = Record("普段着");
        await _repo.SaveAsync(record);

        var images = _paths.ModificationImagesDir(record.Id);
        Directory.CreateDirectory(images);
        await File.WriteAllTextAsync(Path.Combine(images, "user-1.webp"), "絵");

        _repo.Delete(record.Id);

        Assert.False(_repo.Exists(record.Id));
        Assert.False(Directory.Exists(images));
    }

    [Fact]
    public void 無いものを消しても投げない()
        => _repo.Delete("mod-00000000");

    [Fact]
    public async Task 画像の置き場所は商品IDと衝突しない()
    {
        var record = Record("普段着");
        await _repo.SaveAsync(record);

        // images/ 直下は商品IDのフォルダが並ぶ場所。_mods を挟んで避ける
        Assert.Contains(Path.Combine("images", "_mods"), _paths.ModificationImagesDir(record.Id));
    }

    [Fact]
    public async Task 一覧が空でも投げない()
    {
        var result = await _repo.LoadAllAsync();

        Assert.Empty(result.Modifications);
        Assert.Empty(result.FailedIds);
    }
}
