using Chmonos.Core.Booth;

using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 共通素体に付ける画像（ユーザ判断 2026-11-20・152 の案A）。登録簿にはファイル名だけを書き、絵は <c>images/_bases/</c> に中身のハッシュの名前で置く。
/// 付ける・差し替える・外す・改名・統合・削除で、記録とファイルがどうなるかを見る
/// </summary>
public sealed class AvatarBaseImageTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarBaseImageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-baseimg-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new Unreachable()), settings, TestWait.None);
        _service = new AvatarService(_store, settings, client: null, images: new ImagePipeline(client, _paths, settings));
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

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("素体に画像を付けるのにBOOTHへは行かないはず");
    }

    /// <summary>色だけ違う小さなPNG。中身が違えば別の名前になる。</summary>
    private static byte[] MakePng(byte red)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(red, 100, 100));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private Task SaveGroupsAsync(params AvatarBaseGroup[] groups)
        => _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = groups });

    private AvatarBaseGroup? Group(string name)
        => _store.Avatars.Load().BaseGroups.FirstOrDefault(group => group.Name == name);

    private string[] FilesInBases()
        => Directory.Exists(_paths.BaseImagesDir)
            ? Directory.GetFiles(_paths.BaseImagesDir).Select(path => Path.GetFileName(path)).ToArray()
            : [];

    [Fact]
    public async Task 付けると置き場に写し_登録簿にはファイル名だけを書く()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });

        var outcome = await _service.SetBaseImageAsync("素体A", MakePng(10));

        Assert.Equal(BaseImageOutcome.Set, outcome);
        var image = Group("素体A")!.Image;
        Assert.NotNull(image);
        Assert.StartsWith(UserImageName.Prefix, image);
        Assert.Equal(Path.GetFileName(image), image);
        Assert.Equal([image!], FilesInBases());
        Assert.Equal(Path.Combine(_paths.BaseImagesDir, image!), AvatarBaseImage.OwnFile(_paths, Group("素体A")!));
    }

    [Fact]
    public async Task 差し替えると前の画像のファイルを消す()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        var first = Group("素体A")!.Image;

        await _service.SetBaseImageAsync("素体A", MakePng(20));

        var second = Group("素体A")!.Image;
        Assert.NotEqual(first, second);
        Assert.Equal([second!], FilesInBases());
    }

    /// <summary>名前が中身のハッシュなので、同じ絵を付けた2つの素体は1つのファイルを分け合う。片方を外しても、もう片方の絵は消さない</summary>
    [Fact]
    public async Task 同じ絵を分け合うほかの素体があれば_外してもファイルは残す()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" }, new AvatarBaseGroup { Name = "素体B" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        await _service.SetBaseImageAsync("素体B", MakePng(10));
        var shared = Group("素体A")!.Image;
        Assert.Equal(shared, Group("素体B")!.Image);

        Assert.True(await _service.RemoveBaseImageAsync("素体A"));

        Assert.Null(Group("素体A")!.Image);
        Assert.Equal([shared!], FilesInBases());

        await _service.RemoveBaseImageAsync("素体B");
        Assert.Empty(FilesInBases());
    }

    [Fact]
    public async Task 外すと欄を書き出さず_ファイルも消す()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));

        Assert.True(await _service.RemoveBaseImageAsync("素体A"));

        Assert.Null(Group("素体A")!.Image);
        Assert.Empty(FilesInBases());
        Assert.DoesNotContain("\"image\"", File.ReadAllText(_paths.AvatarRegistryFile));
        Assert.False(await _service.RemoveBaseImageAsync("素体A"));
    }

    [Fact]
    public async Task 画像として読めない物は何も書かない()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });

        var outcome = await _service.SetBaseImageAsync("素体A", [1, 2, 3, 4]);

        Assert.Equal(BaseImageOutcome.NotImage, outcome);
        Assert.Null(Group("素体A")!.Image);
        Assert.Empty(FilesInBases());
    }

    /// <summary>押した後に素体が消された・名前が変わったときは書かず、先に保存したファイルを残さない</summary>
    [Fact]
    public async Task 素体がもう無ければ書かず_保存したファイルを片付ける()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A", Rejected = true });

        var outcome = await _service.SetBaseImageAsync("素体A", MakePng(10));

        Assert.Equal(BaseImageOutcome.BaseMissing, outcome);
        Assert.Null(Group("素体A")!.Image);
        Assert.Empty(FilesInBases());
    }

    [Fact]
    public async Task 改名しても画像を引き継ぐ()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        var image = Group("素体A")!.Image;

        await _service.RenameBaseAsync("素体A", "素体C");

        Assert.Equal(image, Group("素体C")!.Image);
        Assert.Equal([image!], FilesInBases());
    }

    [Fact]
    public async Task 統合先に画像が無ければ_統合元の画像を引き継ぐ()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" }, new AvatarBaseGroup { Name = "素体B" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        var image = Group("素体A")!.Image;

        await _service.RenameBaseAsync("素体A", "素体B");

        Assert.Null(Group("素体A"));
        Assert.Equal(image, Group("素体B")!.Image);
        Assert.Equal([image!], FilesInBases());
    }

    /// <summary>両方にあれば統合先の絵を残す（統合先の名前が残るのと揃える）。統合元の絵は行き場が無いので片付ける</summary>
    [Fact]
    public async Task 両方に画像があれば統合先の画像を残し_統合元のファイルを消す()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" }, new AvatarBaseGroup { Name = "素体B" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        await _service.SetBaseImageAsync("素体B", MakePng(20));
        var kept = Group("素体B")!.Image;

        await _service.RenameBaseAsync("素体A", "素体B");

        Assert.Equal(kept, Group("素体B")!.Image);
        Assert.Equal([kept!], FilesInBases());
    }

    [Fact]
    public async Task 削除すると画像を外してファイルを消す()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));

        await _service.DeleteBaseAsync("素体A");

        var group = Group("素体A")!;
        Assert.True(group.Rejected);
        Assert.Null(group.Image);
        Assert.Empty(FilesInBases());
    }

    /// <summary>検出は登録簿の素体を作り直さずに残す。付けた画像が検出し直しで落ちないこと</summary>
    [Fact]
    public async Task 検出し直しても画像は残る()
    {
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A", IsManual = true });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        var image = Group("素体A")!.Image;

        await _service.DetectAsync();

        Assert.Equal(image, Group("素体A")!.Image);
    }

    /// <summary>欄の無い（v1.0.0〜の）登録簿は、画像なしとして読む</summary>
    [Fact]
    public async Task 欄の無い登録簿は画像なしとして読む()
    {
        await File.WriteAllTextAsync(_paths.AvatarRegistryFile, """{"entries":[],"baseGroups":[{"name":"素体A"}]}""");

        Assert.Null(Group("素体A")!.Image);
        Assert.Null(AvatarBaseImage.OwnFile(_paths, Group("素体A")!));
    }

    /// <summary>登録簿は手で直せる。置き場の外を指す名前・アプリが付けない形の名前は使わない</summary>
    [Theory]
    [InlineData("../settings.json")]
    [InlineData("..\\user-0a1b2c3d.webp")]
    [InlineData("C:\\user-0a1b2c3d.webp")]
    [InlineData("a.webp")]
    [InlineData("")]
    public void 置き場の外やアプリが付けない形の名前は使わない(string name)
    {
        Directory.CreateDirectory(_paths.BaseImagesDir);
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_paths.BaseImagesDir, "a.webp"), "x");

        Assert.Null(AvatarBaseImage.OwnFile(_paths, new AvatarBaseGroup { Name = "素体A", Image = name }));
    }

    /// <summary>付けた画像が先、無ければ素体の商品の1枚目（ここでは持っていない商品の控えの1枚）</summary>
    [Fact]
    public async Task 画像が無ければ素体の商品の1枚目を出す()
    {
        var itemImages = _paths.AvatarImagesDir("1234567");
        Directory.CreateDirectory(itemImages);
        var itemImage = Path.Combine(itemImages, "a.webp");
        File.WriteAllText(itemImage, "x");
        await SaveGroupsAsync(new AvatarBaseGroup { Name = "素体A", ItemId = "1234567" });

        Assert.Equal(itemImage, AvatarBaseImage.IconPath(_paths, Group("素体A")!, _ => null, showRemoved: false));

        await _service.SetBaseImageAsync("素体A", MakePng(10));
        Assert.Equal(AvatarBaseImage.OwnFile(_paths, Group("素体A")!), AvatarBaseImage.IconPath(_paths, Group("素体A")!, _ => null, showRemoved: false));

        Assert.Null(AvatarBaseImage.IconPath(_paths, new AvatarBaseGroup { Name = "素体B" }, _ => null, showRemoved: false));
    }

    /// <summary>候補・札は名前しか持たない。名前（大文字小文字は問わない）からグループを引き、消した素体と登録簿に無い名前は頭の字（null）</summary>
    [Fact]
    public async Task 名前から引くと消した素体と登録簿に無い名前は絵が無い()
    {
        await SaveGroupsAsync(
            new AvatarBaseGroup { Name = "Base", Rejected = true },
            new AvatarBaseGroup { Name = "素体A" });
        await _service.SetBaseImageAsync("素体A", MakePng(10));
        var own = AvatarBaseImage.OwnFile(_paths, Group("素体A")!);

        var registry = _store.Avatars.Load();
        Assert.Equal(own, AvatarBaseImage.IconPath(_paths, registry, "素体a", _ => null, showRemoved: false));
        Assert.Null(AvatarBaseImage.IconPath(_paths, registry, "Base", _ => null, showRemoved: false));
        Assert.Null(AvatarBaseImage.IconPath(_paths, registry, "記録だけの素体", _ => null, showRemoved: false));
    }
}
