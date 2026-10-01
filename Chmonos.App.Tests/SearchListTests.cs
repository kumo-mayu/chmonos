using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の画面：条件「壊れたzip」の絞り込みと、ほかの画面からの知らせで一覧の写しが更新されること。
///
/// 検索は全商品の写しを持ち回り、ほかの画面が保存した1件を知らせで差し替える。知らせが効かないと
/// 「保存したのに検索に出ない」「ナビの数が減らない」になり、前は画面を行き来して数を読んで確かめていた。
/// </summary>
public class SearchListTests
{
    private static IEnumerable<string> ShownIds(SearchViewModel search) => search.ListItems.Select(card => card.Item.Id);

    // ---- 条件「壊れたzip」----

    [Fact]
    public Task 壊れたzipだけを出すと_壊れたzipを持つ商品だけが並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "壊れたzipの商品").WithFiles(
            Make.File(@"D:\files\broken.zip", archiveBroken: true)));
        await app.AddItemAsync(Make.Item("1000002", "開けるzipの商品").WithFiles(Make.File(@"D:\files\good.zip")));
        await app.AddItemAsync(Make.Item("1000003", "ファイルの無い商品").WithFiles());
        var search = (await app.StartAsync()).Search;

        search.ShowOnlyBrokenZip();

        Assert.Equal(["1000001"], ShownIds(search));
        Assert.Equal("1 件", search.ResultSummary);
        Assert.True(search.HasActiveFilters);
    });

    [Fact]
    public Task 外したファイルの壊れた印は_絞り込みに数えない() => TestApp.Run(async app =>
    {
        // 外したファイルはこの商品の持ち物ではない。絞り込みで出ると「ダウンロードし直す」相手がこの商品に見える
        await app.AddItemAsync(Make.Item("1000001", "外した壊れたzipの商品").WithFiles(
            Make.File(@"D:\files\kept.zip"),
            Make.File(@"D:\files\broken.zip", archiveBroken: true, detached: true)));
        var search = (await app.StartAsync()).Search;

        search.ShowOnlyBrokenZip();

        Assert.Empty(ShownIds(search));
        Assert.True(search.IsEmpty);
        Assert.Equal("「条件をクリア」で全件に戻ります", search.EmptyHint);
    });

    [Fact]
    public Task 壊れたzipだけを出すと_前の条件と検索の文字は外れる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "壊れたzipの商品").WithFiles(
            Make.File(@"D:\files\broken.zip", archiveBroken: true)));
        var search = (await app.StartAsync()).Search;
        search.QueryText = "どこにも無い語";
        Assert.Empty(ShownIds(search));

        // 前の条件が残っていると「壊れたzipがある商品」に見えない
        search.ShowOnlyBrokenZip();

        Assert.Equal(string.Empty, search.QueryText);
        Assert.Equal(["1000001"], ShownIds(search));
    });

    [Fact]
    public Task 取り込みの結果のボタンは_検索を壊れたzipの条件で開く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "壊れたzipの商品").WithFiles(
            Make.File(@"D:\files\broken.zip", archiveBroken: true)));
        await app.AddItemAsync(Make.Item("1000002", "開けるzipの商品").WithFiles(Make.File(@"D:\files\good.zip")));
        var main = await app.StartAsync();
        main.ShowImportCommand.Execute(null);

        main.Import.ShowBrokenZipCommand.Execute(null);

        await UiThread.Until(() => main.CurrentViewModel is SearchViewModel, "検索の画面へ移る");
        await app.SettleAsync();
        Assert.Equal(["1000001"], ShownIds(main.Search));
    });

    // ---- ほかの画面からの知らせ ----

    [Fact]
    public Task 保存した1件を知らせると_一覧ごと読み直さずに写しが替わる() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "前の名前");
        await app.AddItemAsync(item);
        await app.AddItemAsync(Make.Item("1000002", "もう1つの商品"));
        var main = await app.StartAsync();
        var search = main.Search;
        var otherCard = search.ListItems.Single(card => card.Item.Id == "1000002");

        var saved = item with { Local = item.Local with { DisplayName = "付け直した名前" } };
        search.NoteItemChanged(saved);

        Assert.Equal("付け直した名前", search.FindItem("1000001")!.DisplayName);
        Assert.Equal("付け直した名前", search.ListItems.Single(card => card.Item.Id == "1000001").Item.DisplayName);
        Assert.Contains(search.SnapshotItems(), entry => entry.DisplayName == "付け直した名前");

        // 知らせた1件だけを作り直す。ほかのカードは同じ物のまま（見えている絵やなぞりの途中が残る）
        Assert.Same(otherCard, search.ListItems.Single(card => card.Item.Id == "1000002"));
    });

    [Fact]
    public Task 保存した1件は_新しい名前で検索に当たる() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "前の名前");
        await app.AddItemAsync(item);
        var search = (await app.StartAsync()).Search;

        search.NoteItemChanged(item with { Local = item.Local with { DisplayName = "みずいろのワンピース" } });
        search.QueryText = "みずいろ";

        // 検索用の文字列も差し替わっている（写しだけ替えると、新しい名前で当たらない）
        Assert.Equal(["1000001"], ShownIds(search));
    });

    [Fact]
    public Task タグを付けて保存したと知らせると_ナビの未編集の数が減る() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        var main = await app.StartAsync();
        Assert.Equal(2, main.NeedsEditCount);

        main.Search.NoteItemChanged(item with
        {
            Local = item.Local with { UserTags = [new UserTagAssignment { Top = "衣装" }] },
        });

        Assert.Equal(1, main.Search.NeedsEditCount);
        Assert.Equal(1, main.NeedsEditCount);
    });

    [Fact]
    public Task 写しに無い商品を変えたと知らせても_一覧には足さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var search = (await app.StartAsync()).Search;

        // 差し替えの知らせ。足すのは NoteItemSaved の方
        search.NoteItemChanged(Make.Item("1000009", "まだ読んでいない商品"));

        Assert.Equal(1, search.TotalCount);
        Assert.Null(search.FindItem("1000009"));
    });

    [Fact]
    public Task 未確定で登録した商品を知らせると_一覧に1件足され_件数が増える() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        var search = main.Search;
        Assert.Equal("商品 1 件・1 ショップ", main.LibrarySummary);

        search.NoteItemSaved(Make.Item("1000002", "登録したばかりの商品", shop: "another-shop"));

        Assert.Equal(2, search.TotalCount);
        Assert.Equal(["1000001", "1000002"], ShownIds(search).Order());
        Assert.NotNull(search.FindItem("1000002"));
        Assert.Equal("2 件", search.ResultSummary);

        // ナビの「商品 n 件・m ショップ」も、読み直しを待たずに合う
        Assert.Equal("商品 2 件・2 ショップ", main.LibrarySummary);
    });

    [Fact]
    public Task 既にある商品を登録したと知らせると_増やさずに差し替える() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        var search = (await app.StartAsync()).Search;

        // 既にある商品へファイルを足しただけのとき
        search.NoteItemSaved(item.WithFiles(Make.File(@"D:\files\added.zip")));

        Assert.Equal(1, search.TotalCount);
        Assert.Equal(@"D:\files\added.zip", Assert.Single(search.FindItem("1000001")!.Local.LocalFiles).Paths[0]);
    });

    [Fact]
    public Task 足した1件は_今の絞り込みに当たらなければ並ばない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "壊れたzipの商品").WithFiles(
            Make.File(@"D:\files\broken.zip", archiveBroken: true)));
        var search = (await app.StartAsync()).Search;
        search.ShowOnlyBrokenZip();

        search.NoteItemSaved(Make.Item("1000002", "開けるzipの商品").WithFiles(Make.File(@"D:\files\good.zip")));

        // 写しには入るが、条件に当たらないので並ばない
        Assert.Equal(2, search.TotalCount);
        Assert.Equal(["1000001"], ShownIds(search));
    });

    [Fact]
    public Task 読み直しの最中に知らせた1件は_読み直しの後も新しい中身で残る() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "前の名前");
        await app.AddItemAsync(item);
        var search = (await app.StartAsync()).Search;

        // 読み直しがファイルを読んだ後・写しを差し替える前に、編集画面が保存を知らせた形。
        // ディスクは古いまま（読み直しが読むのは「前の名前」）にして、知らせた中身が勝つことを見る
        var reload = search.ReloadAsync();
        search.NoteItemChanged(item with { Local = item.Local with { DisplayName = "保存した名前" } });
        await reload;

        Assert.Equal("保存した名前", search.FindItem("1000001")!.DisplayName);
    });

    [Fact]
    public Task 星を変えたと知らせると_写しとカードの星が替わる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var search = (await app.StartAsync()).Search;

        search.NoteFavoriteChanged("1000001", isFavorite: true);

        Assert.True(search.FindItem("1000001")!.Local.IsFavorite);
        Assert.True(search.ListItems.Single().IsFavorite);
    });

    // ---- 並べる行 ----

    [Fact]
    public Task 一覧の幅が変わると_カードを列の数で行に切り直す() => TestApp.Run(async app =>
    {
        foreach (var number in Enumerable.Range(1, 5))
        {
            await app.AddItemAsync(Make.Item($"100000{number}", $"作り物の商品{number}"));
        }

        var search = (await app.StartAsync()).Search;

        // 画面が無いので、幅は試験が知らせる（アプリでは View が一覧の幅を知らせる）
        search.SetViewportWidth(100_000);
        Assert.Single(search.Rows);

        search.SetViewportWidth(1);
        Assert.Equal(5, search.Rows.Count);
        Assert.All(search.Rows, row => Assert.Single(row.Cards));
    });
}
