using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存先の中の決まったフォルダから消す物までの途中にリンクがあれば、消さない（点検29）。
/// 文字の上で中かを見るだけ（<see cref="StoreIds.IsInside"/>）だと、途中のフォルダがほかの場所へのリンクのとき、リンクの先の実体を消してしまう。
/// 作れない環境（FAT など）では、リンクを使う試験は飛ばす。
/// </summary>
public sealed class StoreLinkGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-linkguard-" + Guid.NewGuid().ToString("N"));

    public StoreLinkGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // ジャンクションは先へ降りずに消す（Directory.Delete は先の中身を消さない）
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Make(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void リンクの無い途中は_通さないと答える()
    {
        var images = Make("store", "images");
        var target = Make("store", "images", "1000001");

        Assert.False(StoreIds.PassesThroughLink(target, images));
    }

    [Fact]
    public void 中でない場所は_通るとみなす()
    {
        var images = Make("store", "images");
        var outside = Make("elsewhere", "1000001");

        Assert.True(StoreIds.PassesThroughLink(outside, images));
    }

    [Fact]
    public void 途中のフォルダがリンクなら_通ると答える()
    {
        var store = Make("store");
        var images = Make("store", "images");
        var assets = Make("my-assets");
        Make("my-assets", "1000001");
        File.WriteAllText(Path.Combine(assets, "1000001", "大事な物.txt"), "使う人のファイル");
        var link = Path.Combine(images, "_avatars");
        if (!TestJunction.TryCreate(link, assets))
        {
            return;
        }

        Assert.True(StoreIds.PassesThroughLink(Path.Combine(link, "1000001"), images));
        Assert.True(StoreIds.PassesThroughLink(Path.Combine(link, "1000001"), link));
        Assert.True(Directory.Exists(store));
    }

    /// <summary>
    /// 商品を消すとき、画像のフォルダの途中にリンクがあれば、画像には触らず記録だけを消す。リンクの先の実体は残る
    /// </summary>
    [Fact]
    public async Task 商品を消しても_リンクの先の物は消さない()
    {
        var paths = new AppPaths(Make("store"));
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var assets = Make("my-assets");
        Make("my-assets", "1000001");
        var precious = Path.Combine(assets, "1000001", "大事な物.txt");
        File.WriteAllText(precious, "使う人のファイル");

        // images の中の商品の画像の置き場を、使う人のフォルダへのリンクにした
        if (Directory.Exists(paths.ItemImagesDir("1000001")))
        {
            Directory.Delete(paths.ItemImagesDir("1000001"));
        }

        if (!TestJunction.TryCreate(paths.ItemImagesDir("1000001"), Path.Combine(assets, "1000001")))
        {
            return;
        }

        await store.Items.SaveAsync(new ItemRecord { Id = "1000001", Booth = new BoothBlock(), Local = new LocalBlock() });

        await store.Items.DeleteAsync("1000001");

        Assert.False(File.Exists(paths.ItemFile("1000001")));
        Assert.True(File.Exists(precious));
    }
}
