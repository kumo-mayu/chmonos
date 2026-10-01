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
/// 自分で足す画像。
///
/// BOOTHから取れる画像だけでは足りない——**BOOTHに無い商品は画像が1枚も無く**、
/// 普通の商品でも自分で撮った着用例を並べたいことがある。
///
/// **観測と入力を区別して持つ。**混ぜると、自分で足したのに
/// 「BOOTHから消えた」と表示される。
/// </summary>
public class UserImageTests : IDisposable
{
    private const string ItemId = "5927710";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public UserImageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-uimg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new Unreachable()), settings, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, _paths, settings), settings);
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

    /// <summary>画像を足すのに通信は要らない。行ったら失敗させる。</summary>
    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("画像を足すのにBOOTHへは行かないはず");
    }

    /// <summary>色だけ違う小さなPNGを作る。中身が違えば別の画像として扱われる。</summary>
    private static byte[] MakePng(byte red)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(red, 100, 100));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private async Task SaveItemAsync(LocalBlock? local = null)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "鳥" },
            Local = local ?? new LocalBlock(),
        });

    private string ImagePath(string fileName) => Path.Combine(_paths.ItemImagesDir(ItemId), fileName);

    // ---- 足す ----

    [Fact]
    public async Task AddsAnImageAndRecordsIt()
    {
        await SaveItemAsync();

        var fileName = await _service.AddUserImageAsync(ItemId, MakePng(200));

        Assert.NotNull(fileName);
        Assert.StartsWith("user-", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".webp", fileName, StringComparison.Ordinal);
        Assert.True(File.Exists(ImagePath(fileName!)));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.Equal(fileName, Assert.Single(item!.Local.UserImages).FileName);
    }

    /// <summary>
    /// **BOOTHと同じ圧縮を通す。**別の設定にすると、同じギャラリーの中で
    /// 画質と容量の基準が2つになる。webpとして保存されていることで確かめる。
    /// </summary>
    [Fact]
    public async Task SavesAsWebpLikeBoothImages()
    {
        await SaveItemAsync();
        var fileName = await _service.AddUserImageAsync(ItemId, MakePng(200));

        using var saved = await Image.LoadAsync(ImagePath(fileName!));

        Assert.Equal("Webp", saved.Metadata.DecodedImageFormat!.Name);
    }

    /// <summary>同じ絵を2回足しても1枚。保存名が中身のハッシュなので自然にまとまる。</summary>
    [Fact]
    public async Task KeepsOneCopyOfTheSamePicture()
    {
        await SaveItemAsync();
        var bytes = MakePng(200);

        var first = await _service.AddUserImageAsync(ItemId, bytes);
        var second = await _service.AddUserImageAsync(ItemId, bytes);

        Assert.Equal(first, second);
        Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.UserImages);
        Assert.Single(Directory.GetFiles(_paths.ItemImagesDir(ItemId), "user-*"));
    }

    /// <summary>違う絵は別の1枚として並ぶ。</summary>
    [Fact]
    public async Task KeepsDifferentPicturesApart()
    {
        await SaveItemAsync();

        await _service.AddUserImageAsync(ItemId, MakePng(10));
        await _service.AddUserImageAsync(ItemId, MakePng(250));

        Assert.Equal(2, (await _store.Items.LoadAsync(ItemId))!.Local.UserImages.Count);
    }

    /// <summary>画像として読めないものは受け取らない。落としたのが書類でも壊れない。</summary>
    [Fact]
    public async Task RefusesSomethingThatIsNotAnImage()
    {
        await SaveItemAsync();

        Assert.Null(await _service.AddUserImageAsync(ItemId, "これは画像ではない"u8.ToArray()));
        Assert.Empty((await _store.Items.LoadAsync(ItemId))!.Local.UserImages);
    }

    // ---- 並べ替え ----

    [Fact]
    public async Task MovesAnImageWithinTheUserBlock()
    {
        await SaveItemAsync();
        var first = await _service.AddUserImageAsync(ItemId, MakePng(10));
        var second = await _service.AddUserImageAsync(ItemId, MakePng(250));

        Assert.True(await _service.MoveUserImageAsync(ItemId, second!, -1));

        var images = (await _store.Items.LoadAsync(ItemId))!.Local.UserImages;
        Assert.Equal(second, images[0].FileName);
        Assert.Equal(first, images[1].FileName);
    }

    /// <summary>端では動かない。押しても何も起きないより、押せない方がよい。</summary>
    [Fact]
    public async Task RefusesToMoveBeyondTheEnds()
    {
        await SaveItemAsync();
        var only = await _service.AddUserImageAsync(ItemId, MakePng(10));

        Assert.False(await _service.MoveUserImageAsync(ItemId, only!, -1));
        Assert.False(await _service.MoveUserImageAsync(ItemId, only!, 1));
    }

    // ---- 消す ----

    [Fact]
    public async Task RemovesTheRecordAndTheFile()
    {
        await SaveItemAsync();
        var fileName = await _service.AddUserImageAsync(ItemId, MakePng(10));

        Assert.True(await _service.RemoveUserImageAsync(ItemId, fileName!));

        Assert.Empty((await _store.Items.LoadAsync(ItemId))!.Local.UserImages);
        Assert.False(File.Exists(ImagePath(fileName!)));
    }

    /// <summary>
    /// **BOOTHの画像はここでは消さない。**取り直せば戻るものなので、
    /// 消せると「消したのに戻る」になる。
    /// </summary>
    [Fact]
    public async Task NeverDeletesABoothImage()
    {
        await SaveItemAsync();
        var boothImage = ImagePath("a1b2c3d4.webp");
        Directory.CreateDirectory(_paths.ItemImagesDir(ItemId));
        await File.WriteAllBytesAsync(boothImage, MakePng(10));

        await _service.RemoveUserImageAsync(ItemId, "a1b2c3d4.webp");

        Assert.True(File.Exists(boothImage));
    }

    // ---- サムネイルの指名 ----

    /// <summary>**BOOTHの画像も指名できる。**2枚目の方が分かりやすい商品は普通にある。</summary>
    [Fact]
    public async Task PinsAnyImageAsTheThumbnail()
    {
        await SaveItemAsync();

        Assert.True(await _service.PinThumbnailAsync(ItemId, "a1b2c3d4.webp"));
        Assert.Equal("a1b2c3d4.webp", (await _store.Items.LoadAsync(ItemId))!.Local.ThumbnailImage);

        Assert.True(await _service.PinThumbnailAsync(ItemId, null));
        Assert.Null((await _store.Items.LoadAsync(ItemId))!.Local.ThumbnailImage);
    }

    /// <summary>
    /// 指名した自分の画像を消したら、指名も外す。
    ///
    /// BOOTHの画像が消えたときは指名を残す（取り直せば戻る）が、
    /// **自分で消したものは戻らない**ので、残すと永久に空振りする。
    /// </summary>
    [Fact]
    public async Task ClearsThePinWhenThePinnedUserImageIsRemoved()
    {
        await SaveItemAsync();
        var fileName = await _service.AddUserImageAsync(ItemId, MakePng(10));
        await _service.PinThumbnailAsync(ItemId, fileName);

        await _service.RemoveUserImageAsync(ItemId, fileName!);

        Assert.Null((await _store.Items.LoadAsync(ItemId))!.Local.ThumbnailImage);
    }

    /// <summary>別の画像を指名しているときは、消しても指名は動かない。</summary>
    [Fact]
    public async Task KeepsAPinThatPointsElsewhere()
    {
        await SaveItemAsync();
        var first = await _service.AddUserImageAsync(ItemId, MakePng(10));
        var second = await _service.AddUserImageAsync(ItemId, MakePng(250));
        await _service.PinThumbnailAsync(ItemId, first);

        await _service.RemoveUserImageAsync(ItemId, second!);

        Assert.Equal(first, (await _store.Items.LoadAsync(ItemId))!.Local.ThumbnailImage);
    }

    // ---- 並び ----

    /// <summary>
    /// **これが要になる決まり。**並びは「観測 → 自分の分 → 消えたもの」。
    /// 自分で足した画像がBOOTHの一覧に無いからといって
    /// 「削除済」と出してはいけない。
    /// </summary>
    [Fact]
    public void OrdersBoothThenUserThenOrphaned()
    {
        var directory = @"C:\images\5927710";
        var booth = new[] { new BoothImage { OriginalUrl = "https://booth.pximg.net/x/i/1/a.jpg" } };
        var boothFile = Path.Combine(directory, ImagePipeline.FileNameFor(booth[0].OriginalUrl));
        var userFile = Path.Combine(directory, "user-3f9c1b7e.webp");
        var goneFile = Path.Combine(directory, "deadbeef.webp");

        var ordered = ItemImageOrder.Arrange(
            directory,
            booth,
            [goneFile, userFile, boothFile],
            [new UserImage { FileName = "user-3f9c1b7e.webp" }]);

        Assert.Equal(ImageOrigin.Booth, ordered[0].Origin);
        Assert.Equal(ImageOrigin.UserAdded, ordered[1].Origin);
        Assert.Equal(ImageOrigin.Orphaned, ordered[2].Origin);
    }

    /// <summary>記録が失われても、名前で自分の分と分かる。「削除済」と誤って出さない。</summary>
    [Fact]
    public void FallsBackToTheNameWhenTheRecordIsGone()
    {
        var directory = @"C:\images\5927710";
        var userFile = Path.Combine(directory, "user-3f9c1b7e.webp");

        var ordered = ItemImageOrder.Arrange(directory, [], [userFile], userImages: null);

        Assert.Equal(ImageOrigin.UserAdded, Assert.Single(ordered).Origin);
    }

    /// <summary>
    /// 新しく足した絵は自分の画像の末尾に付く（記録の順・ユーザ判断 2026-09-12）。
    /// 名前（中身のハッシュ）の順に並べると、今足した絵が古い絵より前に入ることがある。
    /// </summary>
    [Fact]
    public void PutsNewlyAddedImagesLast()
    {
        var directory = @"C:\images\5927710";
        var first = Path.Combine(directory, "user-ffff0000.webp");
        var added = Path.Combine(directory, "user-00000000.webp");

        var ordered = ItemImageOrder.Arrange(
            directory,
            [],
            [added, first],
            [new UserImage { FileName = "user-ffff0000.webp" }, new UserImage { FileName = "user-00000000.webp" }]);

        Assert.Equal(new[] { first, added }, ordered.Select(entry => entry.Path));
        Assert.All(ordered, entry => Assert.Equal(ImageOrigin.UserAdded, entry.Origin));
    }

    /// <summary>指名があればそれをサムネイルに。無ければ並びの1枚目。</summary>
    [Fact]
    public void PicksThePinnedThumbnail()
    {
        var ordered = new[]
        {
            new OrderedImage(@"C:\images\5927710\a1b2c3d4.webp", ImageOrigin.Booth),
            new OrderedImage(@"C:\images\5927710\user-3f9c1b7e.webp", ImageOrigin.UserAdded),
        };

        Assert.EndsWith("user-3f9c1b7e.webp", ItemImageOrder.Thumbnail(ordered, "user-3f9c1b7e.webp"), StringComparison.Ordinal);
        Assert.EndsWith("a1b2c3d4.webp", ItemImageOrder.Thumbnail(ordered, null), StringComparison.Ordinal);
    }

    /// <summary>指名した先が無ければ黙って1枚目に戻す。取り直せば戻るので指名は消さない。</summary>
    [Fact]
    public void FallsBackWhenThePinnedImageIsMissing()
    {
        var ordered = new[] { new OrderedImage(@"C:\images\5927710\a1b2c3d4.webp", ImageOrigin.Booth) };

        Assert.EndsWith("a1b2c3d4.webp", ItemImageOrder.Thumbnail(ordered, "user-nothere.webp"), StringComparison.Ordinal);
    }
}
