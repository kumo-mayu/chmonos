using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 選んだ商品の間でファイルを割り振るサブ画面（plan 30）。窓は作らず、ViewModel と主の窓のつながりを見る（窓は ViewShot で撮る）。
/// 商品・ファイルの名前と番号は作り物。
/// </summary>
public class FilePoolWindowTests
{
    private const string SetId = "9900801";
    private const string PartAId = "9900802";
    private const string PartBId = "9900803";

    private static readonly LocalFileRecord Top = Make.File(@"D:\files\SampleShop_Top.zip") with
    {
        Contents = ["SampleShop_Top.unitypackage", "readme.txt"],
    };

    private static readonly LocalFileRecord Skirt = Make.File(@"D:\files\SampleShop_Skirt.zip") with
    {
        Contents = ["Skirt/SampleShop_Skirt.unitypackage"],
    };

    private static readonly LocalFileRecord Other = Make.File(@"D:\files\Other.zip");

    /// <summary>セット（3つ持つ）・単品A（上着だけをセットと共有）・単品B（何も無い）。</summary>
    private static async Task<MainViewModel> StartWithSetAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item(SetId, "作り物のセット").WithFiles(Top, Skirt, Other));
        await app.AddItemAsync(Make.Item(PartAId, "作り物の上着").WithFiles(Top));
        await app.AddItemAsync(Make.Item(PartBId, "作り物のスカート").WithFiles());
        return await app.StartAsync();
    }

    private static async Task<FilePoolViewModel> OpenAsync(TestApp app, MainViewModel main, params string[] ids)
    {
        main.ShowFilePool(ids.Length > 0 ? ids : [SetId, PartAId, PartBId]);
        await app.SettleAsync();
        return main.FilePool!;
    }

    private static IEnumerable<FilePoolFileRow> Shown(FilePoolViewModel pool) => pool.Groups.SelectMany(group => group.Files);

    private static FilePoolItemRow Row(FilePoolViewModel pool, string id) => pool.Items.Single(row => row.Id == id);

    private static async Task<LocalBlock> LocalOfAsync(TestApp app, string id) => (await app.Store.Items.LoadAsync(id))!.Local;

    // ---- 入口 ----

    [Fact]
    public Task 検索で2件以上選ぶと帯から開け_1件では押せない() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var cards = main.Search.ListItems.ToList();

        cards[0].IsSelected = true;
        Assert.False(main.Search.CanOpenFilePool);

        cards[1].IsSelected = true;
        Assert.True(main.Search.CanOpenFilePool);
        main.Search.OpenFilePoolCommand.Execute(null);
        await app.SettleAsync();

        var window = Assert.Single(app.SubWindows);
        Assert.Equal(
            new[] { cards[0].Item.Id, cards[1].Item.Id }.Order(),
            window.Model.Items.Select(row => row.Id).Order());

        // 選択は外さない（割り振りながら一覧も使う）
        Assert.True(main.Search.HasSelection);
    });

    // ---- 溜まり・重複回数・絞り ----

    [Fact]
    public Task 溜まりは選んだ商品の中で重複回数を数え_既定は1以下を表示する() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);

        Assert.Equal("選んだ商品（3）", pool.ItemsHeading);
        Assert.Equal(["ファイル 3 個", "ファイル 1 個", "ファイルなし"], pool.Items.Select(row => row.FileCountText));
        Assert.Equal(1, pool.Threshold);
        Assert.Equal(HolderCountFilter.AtMost, pool.Filter);
        Assert.Equal([0, 1, 2, 3], pool.ThresholdChoices);

        // 上着はセットと単品Aの2件に付いているので、1以下では隠れる
        Assert.Equal(["Other.zip", "SampleShop_Skirt.zip"], Shown(pool).Select(file => file.FileName));
        Assert.Equal("3 個のうち 2 個を表示", pool.PoolCountText);
        Assert.All(Shown(pool), file => Assert.Equal("1 件に付いています", file.HolderText));

        pool.Filter = HolderCountFilter.AtLeast;
        pool.Threshold = 2;
        var top = Assert.Single(Shown(pool));
        Assert.Equal("SampleShop_Top.zip", top.FileName);
        Assert.Equal("2 件に付いています", top.HolderText);
        Assert.Equal("作り物のセット\n作り物の上着", top.HoldersTip);

        pool.Filter = HolderCountFilter.Exactly;
        pool.Threshold = 3;
        Assert.Empty(pool.Groups);
        Assert.Equal("条件に合うファイルはありません。付いている商品の数を変えてください。", pool.EmptyText);
    });

    [Fact]
    public Task 名前の頭が同じファイルは組にして畳み_見出しに中のunitypackageを添える() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        pool.Threshold = 3;

        Assert.Equal(["Other", "SampleShop"], pool.Groups.Select(group => group.Name));
        var single = pool.Groups[0];
        Assert.True(single.IsSingle);

        var group = pool.Groups[1];
        Assert.False(group.IsSingle);
        Assert.False(group.IsExpanded);
        Assert.Equal("SampleShop（2）", group.HeaderText);
        Assert.Equal("unitypackage：SampleShop_Skirt、SampleShop_Top", group.PackagesText);

        // 組のチェックで中を全部選び、1つ外すと中間になる
        group.IsChecked = true;
        Assert.All(group.Files, file => Assert.True(file.IsChecked));
        group.Files[0].IsChecked = false;
        Assert.Null(group.IsChecked);

        // 開いた組とチェックは、絞りを変えて組み直しても残る
        group.IsExpanded = true;
        pool.Threshold = 2;
        pool.Threshold = 3;
        Assert.True(pool.Groups[1].IsExpanded);
        Assert.Equal([false, true], pool.Groups[1].Files.Select(file => file.IsChecked));
    });

    [Fact]
    public Task 中のunitypackageが4つ以上なら3つまで書き残りは数で言う()
    {
        Assert.Equal("unitypackage：A、B、C ほか 2 個", FilePoolGroupRow.PackagesTextOf(["A", "B", "C", "D", "E"]));
        Assert.Equal(string.Empty, FilePoolGroupRow.PackagesTextOf([]));
        return Task.CompletedTask;
    }

    // ---- 左の商品の開閉 ----

    [Fact]
    public Task 左の商品は最初すべて広げ_1件ずつとすべてで開閉でき_付ける先とは連動しない() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);

        Assert.All(pool.Items, row => Assert.True(row.IsExpanded));
        Assert.Equal("作り物のセットのファイルを折りたたむ", Row(pool, SetId).ExpandName);

        // 1件だけ畳む
        Row(pool, SetId).IsExpanded = false;
        Assert.Equal([false, true, true], pool.Items.Select(row => row.IsExpanded));
        Assert.Equal("作り物のセットのファイルを広げる", Row(pool, SetId).ExpandName);

        // 付ける先に選んでも開閉は変わらず、畳んだ行を選んでも広がらない
        await pool.OpenItemAsync(Row(pool, SetId));
        Assert.Equal(SetId, pool.SelectedItem?.Id);
        Assert.False(Row(pool, SetId).IsExpanded);
        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal([false, true, true], pool.Items.Select(row => row.IsExpanded));

        pool.CollapseAllItemsCommand.Execute(null);
        Assert.All(pool.Items, row => Assert.False(row.IsExpanded));
        Assert.Equal(PartAId, pool.SelectedItem?.Id);

        // 読み直しでも開閉と付ける先は残る
        Row(pool, PartBId).IsExpanded = true;
        await pool.ReloadAsync();
        Assert.Equal([false, false, true], pool.Items.Select(row => row.IsExpanded));
        Assert.Equal(PartAId, pool.SelectedItem?.Id);

        pool.ExpandAllItemsCommand.Execute(null);
        Assert.All(pool.Items, row => Assert.True(row.IsExpanded));
        Assert.Equal(PartAId, pool.SelectedItem?.Id);

        // 右の組の「すべて広げる」は左に響かない
        pool.CollapseAllCommand.Execute(null);
        Assert.All(pool.Items, row => Assert.True(row.IsExpanded));
    });

    // ---- メインの画面で開く位置 ----

    private static ItemViewModel Page(MainViewModel main) => Assert.IsType<ItemViewModel>(main.CurrentViewModel);

    [Fact]
    public Task 左から開く商品ページは_商品ごとに最後に離れた位置で開き_初めては先頭から() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);

        // a を初めて開く：先頭から（戻す位置は無い）
        await pool.OpenItemAsync(Row(pool, SetId));
        Assert.False(Page(main).ArrivedByHistory);
        Page(main).ScrollReader = () => 800;

        // b を初めて開く：先頭から（a の位置を引き継がない）
        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal(PartAId, Page(main).Item.Id);
        Assert.False(Page(main).ArrivedByHistory);
        Assert.Null(Page(main).TakeRestoreScrollOffset());
        Page(main).ScrollReader = () => 300;

        // a に戻る：a を離れたときの位置
        await pool.OpenItemAsync(Row(pool, SetId));
        Assert.Equal(SetId, Page(main).Item.Id);
        Assert.Equal(800, Page(main).TakeRestoreScrollOffset());
        Page(main).ScrollReader = () => 950;

        // b へ：b の位置
        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal(300, Page(main).TakeRestoreScrollOffset());
        Page(main).ScrollReader = () => 320;

        // 今出ている商品を押し直したときは、今の位置のまま
        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal(320, Page(main).TakeRestoreScrollOffset());
        Page(main).ScrollReader = () => 320;

        // 戻るは今までどおり履歴の1件ごとの位置（a を2回目に離れた 950）
        main.GoBack();
        await UiThread.Until(() => main.CurrentViewModel is ItemViewModel page && page.Item.Id == PartAId && page.ArrivedByHistory, "戻るで1つ前が開く");
        await app.SettleAsync();
        Assert.Equal(320, Page(main).TakeRestoreScrollOffset());
        Page(main).ScrollReader = () => 320;
        main.GoBack();
        await UiThread.Until(() => main.CurrentViewModel is ItemViewModel page && page.Item.Id == SetId, "戻るで a が開く");
        await app.SettleAsync();
        Assert.Equal(950, Page(main).TakeRestoreScrollOffset());

        // 検索など、サブ画面の外から進むときは今までどおり先頭から
        Page(main).ScrollReader = () => 400;
        main.ShowItem((await app.Store.Items.LoadAsync(PartAId))!);
        Assert.Null(Page(main).TakeRestoreScrollOffset());

        // サブ画面の外から開いて流した後も、サブ画面から開くと覚えている
        Page(main).ScrollReader = () => 640;
        await pool.OpenItemAsync(Row(pool, SetId));
        Assert.Equal(400, Page(main).TakeRestoreScrollOffset());
        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal(640, Page(main).TakeRestoreScrollOffset());
    });

    // ---- 左の商品の絵 ----

    [Fact]
    public Task 左の商品の絵は読まれるまで決めず_絵の無い商品は頭文字で_読み直しでも決め直さない() => TestApp.Run(async app =>
    {
        // 絵を1枚持つ商品（作り物の 4×4 の絵）
        var item = Make.Item("9900804", "作り物の髪飾り");
        item = item with { Booth = item.Booth with { Images = [new BoothImage { OriginalUrl = "https://sample.invalid/9900804/a.png" }] } };
        var directory = app.Services.Paths.ItemImagesDir(item.Id);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, ImagePipeline.FileNameFor(item.Booth.Images[0].OriginalUrl));
        using (var image = new Image<Rgba32>(4, 4))
        {
            image.SaveAsWebp(file);
        }

        await app.AddItemAsync(item);
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main, SetId, "9900804");

        var dressed = Row(pool, "9900804").Picture!;
        var bare = Row(pool, SetId).Picture!;
        Assert.Equal("作", dressed.Initial);

        // 画面が行を作って絵を読むまでは、商品の記録を読みに行かない
        Assert.Null(dressed.Path);

        _ = dressed.Image;
        _ = bare.Image;
        await UiThread.Until(() => dressed.Path is not null, "絵の場所が決まる");
        await app.SettleAsync();
        Assert.Equal(file, dressed.Path);
        Assert.Null(bare.Path);
        Assert.Null(bare.Image);

        // 読み直しても行と絵は使い回す
        await pool.ReloadAsync();
        Assert.Same(dressed, Row(pool, "9900804").Picture);
    });

    // ---- 探す欄 ----

    [Fact]
    public Task 左の探す欄は商品名で絞り_かなの種類と全角半角を区別せず_右と付ける先は変えない() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, PartBId));
        var poolBefore = Shown(pool).Select(file => file.Hash).ToList();

        // 「スカート」をひらがな・半角カナで打っても当たる
        pool.ItemFilterText = "すかーと";
        Assert.Equal([PartBId], pool.ShownItems.Select(row => row.Id));
        pool.ItemFilterText = "ｽｶｰﾄ";
        Assert.Equal([PartBId], pool.ShownItems.Select(row => row.Id));

        // 語は全部当たる物だけ
        pool.ItemFilterText = "作り物　上着";
        Assert.Equal([PartAId], pool.ShownItems.Select(row => row.Id));
        Assert.False(pool.HasItemsEmptyText);

        // 付ける先が隠れても外さない。見出しの数・n の選択肢・右の溜まりは選んだ商品の全部のまま
        Assert.Equal(PartBId, pool.SelectedItem?.Id);
        Assert.Equal("選んだ商品（3）", pool.ItemsHeading);
        Assert.Equal([0, 1, 2, 3], pool.ThresholdChoices);
        Assert.Equal(poolBefore, Shown(pool).Select(file => file.Hash));

        // 「すべて畳む」は見えている商品だけ（右の組のボタンと同じ）
        pool.CollapseAllItemsCommand.Execute(null);
        Assert.Equal([true, false, true], pool.Items.Select(row => row.IsExpanded));

        pool.ItemFilterText = "無い名前";
        Assert.Empty(pool.ShownItems);
        Assert.Equal("当たる商品はありません。探す文字を変えてください。", pool.ItemsEmptyText);

        // 読み直しでも絞りは残る
        await pool.ReloadAsync();
        Assert.Empty(pool.ShownItems);

        pool.ClearItemFilterCommand.Execute(null);
        Assert.Equal([SetId, PartAId, PartBId], pool.ShownItems.Select(row => row.Id));
        Assert.False(pool.HasItemsEmptyText);
    });

    [Fact]
    public Task 右の探す欄はファイル名と組の名前で絞り_付いている商品の数の絞りと重なる() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        pool.Threshold = 3;

        // ファイル名：大文字小文字・全角半角を区別しない。組の中で1つだけ残った組は見出しの無い1行になる
        pool.FileFilterText = "ｓｋｉｒｔ";
        var skirt = Assert.Single(Shown(pool));
        Assert.Equal("SampleShop_Skirt.zip", skirt.FileName);
        Assert.True(Assert.Single(pool.Groups).IsSingle);
        Assert.Equal("3 個のうち 1 個を表示", pool.PoolCountText);

        // 組の名前で当たれば組の全部が残る
        pool.FileFilterText = "sampleshop";
        Assert.Equal(["SampleShop_Skirt.zip", "SampleShop_Top.zip"], Shown(pool).Select(file => file.FileName));
        Assert.Equal("SampleShop", Assert.Single(pool.Groups).Name);

        // 数の絞りと重なる：上着はセットと単品Aの2件に付いているので「1 以下」では隠れる
        pool.Threshold = 1;
        Assert.Equal(["SampleShop_Skirt.zip"], Shown(pool).Select(file => file.FileName));
        Assert.Equal("3 個のうち 1 個を表示", pool.PoolCountText);

        // 文字で全部隠れたときは、変えるのは文字だと言う
        pool.FileFilterText = "無いファイル";
        Assert.Empty(pool.Groups);
        Assert.Equal("当たるファイルはありません。探す文字を変えてください。", pool.EmptyText);

        // 数で全部隠れたときは、今までどおり数を変えるように言う
        pool.Filter = HolderCountFilter.Exactly;
        pool.Threshold = 3;
        Assert.Equal("条件に合うファイルはありません。付いている商品の数を変えてください。", pool.EmptyText);

        pool.ClearFileFilterCommand.Execute(null);
        pool.Filter = HolderCountFilter.AtMost;
        Assert.Equal(3, Shown(pool).Count());
        Assert.Equal("3 個", pool.PoolCountText);
    });

    [Fact]
    public Task 右の探す欄で隠れたチェックは控えに残り_付けるのは見えている行だけ() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, PartBId));

        Shown(pool).Single(file => file.FileName == "Other.zip").IsChecked = true;
        pool.FileFilterText = "skirt";
        Shown(pool).Single(file => file.FileName == "SampleShop_Skirt.zip").IsChecked = true;
        Assert.Equal(1, pool.CheckedCount);

        await pool.AttachAsync();
        Assert.Equal([Skirt.Hash], (await LocalOfAsync(app, PartBId)).OwnedFiles.Select(file => file.Hash));

        // 隠れていたチェックは、文字を消すと戻る
        pool.FileFilterText = string.Empty;
        Assert.True(Shown(pool).Single(file => file.FileName == "Other.zip").IsChecked);
    });

    // ---- 付ける ----

    [Fact]
    public Task 付けると選んだ商品に付き_元の商品からは外さず_主の窓の写しにも映る() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);

        Assert.False(pool.CanAttach);
        Assert.Equal("左の一覧で、付ける先の商品を選んでください。", pool.AttachHint);

        await pool.OpenItemAsync(Row(pool, PartBId));
        Assert.Equal("付けるファイルにチェックを入れてください。", pool.AttachHint);

        Shown(pool).Single(file => file.FileName == "SampleShop_Skirt.zip").IsChecked = true;
        Assert.True(pool.CanAttach);
        Assert.Equal("1 個を付ける", pool.AttachText);
        Assert.Equal("付ける先：作り物のスカート", pool.AttachHint);

        await pool.AttachAsync();
        await app.SettleAsync();

        Assert.Equal("1 個のファイルを「作り物のスカート」に付けました。", pool.ResultText);
        Assert.Equal([Skirt.Hash], (await LocalOfAsync(app, PartBId)).OwnedFiles.Select(file => file.Hash));
        Assert.Contains((await LocalOfAsync(app, SetId)).OwnedFiles, file => file.Hash == Skirt.Hash);

        // 2件に付いたので、1以下からは消える。付けた先の下に並ぶ
        Assert.DoesNotContain(Shown(pool), file => file.FileName == "SampleShop_Skirt.zip");
        Assert.Equal(["SampleShop_Skirt.zip"], Row(pool, PartBId).Files.Select(file => file.FileName));
        Assert.Equal(0, pool.CheckedCount);

        // 主の窓の検索の写しにも、開いている商品ページにも映る
        await UiThread.Until(
            () => main.Search.FindItem(PartBId)!.Local.OwnedFiles.Any(file => file.Hash == Skirt.Hash),
            "検索の写しに付けたファイルが映る");
        await UiThread.Until(
            () => main.CurrentItemPage is { } page && page.LocalFiles.Any(file => file.Hash == Skirt.Hash),
            "開いている商品ページに付けたファイルが映る");
    });

    [Fact]
    public Task 付ける先がもう持つファイルはチェックできない() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        pool.Threshold = 3;

        await pool.OpenItemAsync(Row(pool, PartAId));

        var top = Shown(pool).Single(file => file.FileName == "SampleShop_Top.zip");
        Assert.False(top.CanCheck);
        Assert.Equal("この商品に付いています", top.HolderText);
        top.IsChecked = true;
        Assert.False(top.IsChecked);
    });

    [Fact]
    public Task 除外したファイルは解かずに付けずに_そう言う() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        await app.Store.Excluded.SaveAsync([new ExcludedEntry { Hash = Other.Hash, ExcludedAt = DateTimeOffset.Now }]);
        var pool = await OpenAsync(app, main);

        await pool.OpenItemAsync(Row(pool, PartBId));
        Shown(pool).Single(file => file.FileName == "Other.zip").IsChecked = true;
        await pool.AttachAsync();

        Assert.Equal("「Other.zip」は管理対象から除外しているので、付けませんでした。", pool.ResultText);
        Assert.Empty((await LocalOfAsync(app, PartBId)).LocalFiles);
    });

    // ---- 外す ----

    [Fact]
    public Task ほかの商品にも付いていれば聞かずに外し_重複回数が減る() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, PartAId));

        await pool.DetachAsync(Assert.Single(Row(pool, PartAId).Files));

        Assert.Empty(app.Notices);
        Assert.Equal("「SampleShop_Top.zip」を「作り物の上着」から外しました。", pool.ResultText);
        Assert.Empty(Row(pool, PartAId).Files);

        // 商品は消さない。上着はセットだけになったので、1以下に出る
        Assert.NotNull(await app.Store.Items.LoadAsync(PartAId));
        Assert.Contains(Shown(pool), file => file.FileName == "SampleShop_Top.zip");
    });

    /// <summary>
    /// どの商品にも付かなくなるときだけ聞く（未確定に表示される）。外した物は 0 件で溜まりに残り、別の商品へ付け直せる。
    /// 付け直すと未確定から消える。
    /// </summary>
    [Fact]
    public Task 最後の1件から外すときだけ聞き_外した物は0件で残って付け直せる() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, SetId));
        var skirt = Row(pool, SetId).Files.Single(file => file.FileName == "SampleShop_Skirt.zip");

        // 既定の答えはキャンセル：何も変わらない
        await pool.DetachAsync(skirt);
        var asked = Assert.Single(app.Notices);
        Assert.Equal(
            "「SampleShop_Skirt.zip」を「作り物のセット」から外しますか？\nどの商品にも付いていない状態になります。ファイルが手元にあれば未確定に表示されます。この画面でほかの商品に付け直せます。",
            asked.Text);
        Assert.Contains((await LocalOfAsync(app, SetId)).OwnedFiles, file => file.Hash == Skirt.Hash);

        app.Answer = _ => MessageBoxResult.OK;
        await pool.DetachAsync(skirt);

        var zero = Shown(pool).Single(file => file.FileName == "SampleShop_Skirt.zip");
        Assert.Equal(0, zero.HolderCount);
        Assert.Equal("どの商品にも付いていません", zero.HolderText);

        // 作り物の場所はディスクに無いので未確定には出ない。付け直せることだけを見る
        await pool.OpenItemAsync(Row(pool, PartBId));
        Shown(pool).Single(file => file.FileName == "SampleShop_Skirt.zip").IsChecked = true;
        await pool.AttachAsync();

        Assert.Equal([Skirt.Hash], (await LocalOfAsync(app, PartBId)).OwnedFiles.Select(file => file.Hash));
    });

    // ---- ほかの画面の変更を映す・消えた商品 ----

    [Fact]
    public Task 開いている間にほかの画面や取り込みが書き換えると読み直す() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);

        // 商品ページと同じ命令で外す（サブ画面を通らない道）
        await app.Services.Commands.ExecuteAsync(new UiCommand.DetachFile(PartAId, Top.Hash));
        await app.SettleAsync();
        Assert.Equal("ファイルなし", Row(pool, PartAId).FileCountText);

        // 取り込みのように置き場へ直に書く
        var added = Make.File(@"D:\files\SampleShop_Hat.zip");
        await app.Store.Items.ChangeLocalAsync(
            PartBId, local => local with { LocalFiles = [.. local.LocalFiles, added] }, [LocalField.LocalFiles]);
        await app.SettleAsync();
        Assert.Contains(Shown(pool), file => file.FileName == "SampleShop_Hat.zip");
        Assert.Equal("ファイル 1 個", Row(pool, PartBId).FileCountText);
    });

    [Fact]
    public Task 選んだ商品が削除されたら一覧から外し_そう言う() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, PartAId));

        await app.Store.Items.DeleteAsync(PartAId);
        await app.SettleAsync();

        Assert.Equal([SetId, PartBId], pool.Items.Select(row => row.Id));
        Assert.Equal("削除された商品 1 件を一覧から外しました。", pool.RemovedText);
        Assert.Null(pool.SelectedItem);
        Assert.Equal([0, 1, 2], pool.ThresholdChoices);
    });

    // ---- 主の窓 ----

    [Fact]
    public Task 商品を押すと主の窓がその商品ページを開き_画面の履歴に積む() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        Assert.True(main.IsSearchActive);
        Assert.False(main.CanGoBack);

        await pool.OpenItemAsync(Row(pool, SetId));
        Assert.Equal(SetId, main.CurrentItemPage!.Item.Id);
        Assert.Same(Row(pool, SetId), pool.SelectedItem);
        Assert.True(main.CanGoBack);

        await pool.OpenItemAsync(Row(pool, PartAId));
        Assert.Equal(PartAId, main.CurrentItemPage!.Item.Id);

        main.GoBack();
        Assert.Equal(SetId, main.CurrentItemPage!.Item.Id);
        main.GoBack();
        Assert.True(main.IsSearchActive);
    });

    /// <summary>主の窓が閉じる・隠れるとき（App.OnMainWindowClosing が呼ぶ）に閉じる。持ち主任せにしない（隠すだけの窓ではサブ画面が残る）。</summary>
    [Fact]
    public Task 主の窓が閉じるときはサブ画面も閉じ_開いていなければ何もしない() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        main.CloseSubWindows();

        await OpenAsync(app, main);
        var window = Assert.Single(app.SubWindows);

        main.CloseSubWindows();

        Assert.True(window.IsClosed);
        Assert.Null(main.FilePool);
    });

    [Fact]
    public Task 同じ商品を順だけ変えて開き直しても_チェックは残す() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        Shown(pool).First().IsChecked = true;

        main.ShowFilePool([PartBId, SetId, PartAId]);
        await app.SettleAsync();

        Assert.Equal(1, pool.CheckedCount);
        Assert.Equal(1, Assert.Single(app.SubWindows).BroughtToFront);
    });

    [Fact]
    public Task 開くのは1つだけで_2回目は前に出し_違う選び方なら入れ替える() => TestApp.Run(async app =>
    {
        var main = await StartWithSetAsync(app);
        var pool = await OpenAsync(app, main);
        await pool.OpenItemAsync(Row(pool, PartBId));

        // 同じ選び方：入れ替えずに前に出すだけ（付ける先を残す）
        main.ShowFilePool([SetId, PartAId, PartBId]);
        await app.SettleAsync();
        var window = Assert.Single(app.SubWindows);
        Assert.Equal(1, window.BroughtToFront);
        Assert.Same(pool, main.FilePool);
        Assert.Equal(PartBId, pool.SelectedItem?.Id);

        // 違う選び方：同じ窓のまま中身を入れ替える
        main.ShowFilePool([SetId, PartAId]);
        await app.SettleAsync();
        Assert.Single(app.SubWindows);
        Assert.Equal(2, window.BroughtToFront);
        Assert.Equal([SetId, PartAId], pool.Items.Select(row => row.Id));
        Assert.Null(pool.SelectedItem);

        // 閉じたら控えを外し、置き場の知らせも聞かない。次はまた新しく開く
        window.Close();
        Assert.Null(main.FilePool);
        await app.Store.Items.DeleteAsync(PartAId);
        await app.SettleAsync();
        Assert.Equal(2, pool.Items.Count);

        main.ShowFilePool([SetId, PartBId]);
        await app.SettleAsync();
        Assert.Equal(2, app.SubWindows.Count);
        Assert.NotSame(pool, main.FilePool);
    });
}
