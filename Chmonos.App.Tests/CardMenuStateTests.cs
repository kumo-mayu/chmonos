using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 右クリックのメニュー（CardMenu）は、項目をいつも全部出し、要る物（商品の JSON・手元のファイル）が無いときは押せなくして理由を言う
/// （ユーザ判断 2026-10-04・メモ20-①）。押せるかと吹き出しの文を、状態ごとに確かめる。
/// </summary>
public class CardMenuStateTests
{
    private static readonly string[] NeedFiles = ["Reveal", "Unpack", "SendToUnity", "SendToUnityWithRecord", "SelectInUnity"];

    private static readonly string[] NeedJson = ["Favorite", "OpenItem", "Edit", "AddToModification", "Hide", "OpenBooth", "OpenShop", "CopyLink"];

    private sealed class Row(ItemCardViewModel? card) : IHasItemCard
    {
        public ItemCardViewModel? Card { get; } = card;
    }

    [Fact]
    public Task ファイルがある商品は_全部の項目が押せる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithPackageZip(Make.Item("1000001", "作り物の衣装A")));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles.Concat(NeedJson).Concat(["Select"]))
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task ファイルが無い商品は_ファイルが要る項目だけ押せず_理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A").WithFiles());
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles)
        {
            Assert.False(CardMenuState.IsEnabled(key, card), key);
            Assert.Equal("手元にファイルがありません", CardMenuState.Tip(key, card));
        }

        // 使った記録・お気に入り・商品ページなどは、手元のファイルが無くても押せる
        foreach (var key in NeedJson)
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    private static readonly string[] NeedPackage = ["SendToUnity", "SendToUnityWithRecord", "SelectInUnity"];

    /// <summary>zip の中に unitypackage を持つ商品にする（中身の一覧 `contents` に載っている）。</summary>
    private static ItemRecord WithPackageZip(ItemRecord item)
        => item with { Local = item.Local with { LocalFiles = [.. item.Local.LocalFiles.Select(file => file with { Contents = ["Sample/Sample.unitypackage"] })] } };

    private static ItemRecord WithFolders(ItemRecord item, params LocalFolderRecord[] folders)
        => item with { Local = item.Local with { LocalFolders = folders } };

    [Fact]
    public Task フォルダだけの商品は_開くは押せ_展開は押せず理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithFolders(Make.Item("9900601", "作り物の衣装A").WithFiles(), new LocalFolderRecord { Path = @"C:\作り物\衣装A" }));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.True(CardMenuState.IsEnabled("Reveal", card));
        Assert.True(CardMenuState.IsEnabled("OpenParent", card));
        Assert.False(CardMenuState.IsEnabled("Unpack", card));
        Assert.Equal("手元にzipがありません", CardMenuState.Tip("Unpack", card));

        // 中に unitypackage が無いフォルダは、Unity の項目も押せない（メモ65-③）
        foreach (var key in NeedPackage)
        {
            Assert.False(CardMenuState.IsEnabled(key, card), key);
            Assert.Equal("手元にunitypackageがありません", CardMenuState.Tip(key, card));
        }

        Assert.False(CardMenuState.IsEnabled("UnityParent", card));
        Assert.Equal("手元にunitypackageがありません", CardMenuState.Tip("UnityParent", card));
    });

    [Fact]
    public Task フォルダの中にunitypackageがあれば_Unityの項目は押せ_展開は押せない() => TestApp.Run(async app =>
    {
        // メモ65-③：展開できないのはフォルダだからで、中の unitypackage は送れる
        await app.AddItemAsync(WithFolders(Make.Item("9900602", "作り物の衣装B").WithFiles(),
            new LocalFolderRecord { Path = @"C:\作り物\衣装B", UnityPackages = ["Unity/Outfit.unitypackage"] }));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedPackage.Append("UnityParent"))
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }

        Assert.False(CardMenuState.IsEnabled("Unpack", card));
        Assert.Equal("手元にzipがありません", CardMenuState.Tip("Unpack", card));
    });

    [Fact]
    public Task unitypackageの無いzipだけの商品は_Unityの項目を押せず理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900603", "作り物のテクスチャ"));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.True(CardMenuState.IsEnabled("Unpack", card));
        foreach (var key in NeedPackage.Append("UnityParent"))
        {
            Assert.False(CardMenuState.IsEnabled(key, card), key);
            Assert.Equal("手元にunitypackageがありません", CardMenuState.Tip(key, card));
        }
    });

    [Fact]
    public Task 持っている物が全部見つからない商品は_開くとUnityを親ごと押せず_見つからないと言う() => TestApp.Run(async app =>
    {
        // メモ65-①：zip は見つからなくなった日時付き、もう1つの zip は場所が空、フォルダも日時付き
        var gone = DateTimeOffset.Now;
        var item = WithPackageZip(Make.Item("9900604", "作り物の衣装C").WithFiles(
            Make.File(@"D:\files\c1.zip") with { MissingSince = gone },
            Make.File(@"D:\files\c2.zip") with { Paths = [] }));
        await app.AddItemAsync(WithFolders(item,
            new LocalFolderRecord { Path = @"C:\作り物\衣装C", UnityPackages = ["C.unitypackage"], MissingSince = gone }));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles.Concat(["OpenParent", "UnityParent"]))
        {
            Assert.False(CardMenuState.IsEnabled(key, card), key);
            Assert.Equal("手元のファイルが見つかりません", CardMenuState.Tip(key, card));
        }

        // 記録だけを使う項目は今のまま押せる
        foreach (var key in NeedJson)
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task zipが全部見つからずフォルダだけ在る商品は_展開の理由をzipが見つかりませんと言う() => TestApp.Run(async app =>
    {
        // ユーザ判断 2026-10-05：zip を持たない商品の「手元にzipがありません」と言い分ける
        var item = Make.Item("9900607", "作り物の衣装F").WithFiles(Make.File(@"D:\files\f.zip") with { MissingSince = DateTimeOffset.Now });
        await app.AddItemAsync(WithFolders(item, new LocalFolderRecord { Path = @"C:\作り物\衣装F" }));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.False(CardMenuState.IsEnabled("Unpack", card));
        Assert.Equal("zipが見つかりません", CardMenuState.Tip("Unpack", card));
    });

    [Fact]
    public Task 見つからない物があっても1つ在れば_今のまま押せる() => TestApp.Run(async app =>
    {
        // zip は見つからないが、フォルダ（中に unitypackage）は在る
        var item = WithPackageZip(Make.Item("9900605", "作り物の衣装D").WithFiles(Make.File(@"D:\files\d.zip") with { MissingSince = DateTimeOffset.Now }));
        await app.AddItemAsync(WithFolders(item, new LocalFolderRecord { Path = @"C:\作り物\衣装D", UnityPackages = ["D.unitypackage"] }));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.True(CardMenuState.IsEnabled("Reveal", card));
        Assert.True(CardMenuState.IsEnabled("OpenParent", card));
        foreach (var key in NeedPackage.Append("UnityParent"))
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task つながっていないドライブの上の物は_見つからないに数えず押せる() => TestApp.Run(async app =>
    {
        // 外付けを外しているだけの物には日時が付かない（無くなったのではない）。押せば「取り外しているドライブ」と言う
        await app.AddItemAsync(WithPackageZip(Make.Item("9900606", "作り物の衣装E").WithFiles(Make.File(@"Q:\外付け\e.zip"))));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles.Concat(["OpenParent", "UnityParent"]))
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task 商品が手元に無い行は_商品のJSONが要る項目も押せず_理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A"));
        var main = await app.StartAsync();
        var nameOnly = new Row(null);

        foreach (var key in NeedFiles.Concat(NeedJson))
        {
            Assert.False(CardMenuState.IsEnabled(key, nameOnly), key);
            Assert.Equal("商品の情報がまだありません", CardMenuState.Tip(key, nameOnly));
        }

        // 商品がある行は、カードと同じに押せる
        var withCard = new Row(main.Search.ListItems.Single());
        Assert.True(CardMenuState.IsEnabled("Favorite", withCard));
        Assert.False(CardMenuState.IsEnabled("Select", withCard));
        Assert.Equal("この一覧では選べません", CardMenuState.Tip("Select", withCard));
        Assert.Equal("お気に入りに追加", CardMenuState.FavoriteHeader(nameOnly));
    });

    [Fact]
    public Task BOOTHに無い商品は_BOOTHとリンクだけ押せず_その理由を言う() => TestApp.Run(async app =>
    {
        var local = Make.Item("1000001", "作り物の衣装A") with { Id = "local-5a3e0001" };
        await app.AddItemAsync(local);
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.False(CardMenuState.IsEnabled("OpenBooth", card));
        Assert.False(CardMenuState.IsEnabled("CopyLink", card));
        Assert.Equal(card.OpenBoothTip, CardMenuState.Tip("OpenBooth", card));
        Assert.True(CardMenuState.IsEnabled("Edit", card));
    });

    [Fact]
    public Task 子が全部押せない親は_親も押せず_子と同じ理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装A").WithFiles());
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var parent in new[] { "OpenParent", "UnityParent" })
        {
            Assert.False(CardMenuState.IsEnabled(parent, card), parent);
            Assert.Equal("手元にファイルがありません", CardMenuState.Tip(parent, card));
            // 商品が手元に無い行は、子の理由（商品の情報がまだありません）がそのまま親の理由になる
            Assert.False(CardMenuState.IsEnabled(parent, new Row(null)), parent);
            Assert.Equal("商品の情報がまだありません", CardMenuState.Tip(parent, new Row(null)));
        }
    });

    [Fact]
    public Task 子が1つでも押せる親は_押せて吹き出しは出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithPackageZip(Make.Item("9900001", "作り物の衣装A")));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var parent in new[] { "OpenParent", "UnityParent" })
        {
            Assert.True(CardMenuState.IsEnabled(parent, card), parent);
            Assert.Null(CardMenuState.Tip(parent, card));
        }
    });
}
