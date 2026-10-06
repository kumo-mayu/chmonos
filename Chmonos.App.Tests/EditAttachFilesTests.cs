using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面でも新しいファイルをこの商品に紐付ける（ユーザ判断 2026-10-06）：左のローカルファイルの欄の「追加…」と、
/// バリエーション分けの「バリエーションごと」の行の「追加…」（紐付けたうえで、その種類のファイルとしても結ぶ）。
/// 足したファイルは、開き直さずに右の一覧と選ぶ欄に出る
/// </summary>
public class EditAttachFilesTests
{
    private const string ItemId = "9900831";

    /// <summary>種類が2つあって両方買い、ファイルを1つ持つ商品（「本体」に結んである）。</summary>
    private static ItemRecord TwoVariations(bool buyBoth = true)
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        return item with
        {
            Booth = item.Booth! with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                    new BoothVariation { Id = 2, Name = "テクスチャ", Price = 500, Type = "downloadable" },
                ],
            },
            Local = item.Local with
            {
                Purchases = buyBoth
                    ?
                    [
                        new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                        new Purchase { VariationId = 2, NameSnapshot = "テクスチャ", Price = 500 },
                    ]
                    : [new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 }],
                LocalFiles = [Make.File(@"D:\files\body.zip") with { VariationId = 1 }],
            },
        };
    }

    private static async Task<EditViewModel> OpenEditAsync(TestApp app, ItemRecord item)
    {
        await app.AddItemAsync(item);
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        Assert.NotNull(edit.ItemPage);
        return edit;
    }

    private static async Task SettleAttachAsync(TestApp app, EditViewModel edit)
    {
        await app.SettleAsync();
        await UiThread.Until(() => !edit.ItemPage!.FilesNotice.Text.EndsWith('…'), "読み終える");
        await app.SettleAsync();
    }

    private static OrderedVariationInput Row(EditViewModel edit, string name) => edit.PurchasedVariationRows.Single(row => row.Name == name);

    private static async Task<LocalFileRecord?> StoredFileAsync(TestApp app, string path)
        => (await app.Store.Items.LoadAsync(ItemId))!.Local.LocalFiles.SingleOrDefault(file => file.Paths.Contains(path));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    [Fact]
    public Task 編集画面のローカルファイルの欄にも追加が出る() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app, TwoVariations());

        // 欄は商品ページと同じ部品。前は「使う」操作と同じく編集画面では隠していた
        var panel = new ItemFilesPanel { DataContext = edit.ItemPage };
        var host = new Grid();
        host.Children.Add(panel);
        host.Measure(new Size(900, 2000));
        host.Arrange(new Rect(0, 0, 900, 2000));
        host.UpdateLayout();

        var add = Descendants(host).OfType<Button>()
            .Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "ItemAttachFiles");
        Assert.Equal(Visibility.Visible, add.Visibility);
        Assert.Equal("追加…", add.Content);
    });

    [Fact]
    public Task 左の追加で紐付くと_ファイルの行と右の一覧と選ぶ欄に開き直さずに出る() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_追加分.zip");
        var edit = await OpenEditAsync(app, TwoVariations());
        var page = edit.ItemPage!;
        app.PickAttachFiles = () => [zip];

        page.AttachFilesCommand.Execute(null);
        await SettleAttachAsync(app, edit);

        Assert.Same(page, edit.ItemPage);
        Assert.Contains(page.LocalFiles, row => row.FileName == "作り物_追加分.zip");
        Assert.Contains(edit.FileSortRows, row => row.Name == "作り物_追加分.zip" && row.IsUnassigned);
        Assert.Contains(Row(edit, "テクスチャ").FileChoices, choice => choice.Name == "作り物_追加分.zip");
        Assert.Contains(Row(edit, "本体").FileChoices, choice => choice.Name == "作り物_追加分.zip");

        // 左から足しただけでは、どの種類にも結ばない
        Assert.Null((await StoredFileAsync(app, zip))!.VariationId);
    });

    [Fact]
    public Task バリエーションの行から足すと_紐付いてその種類に結ばれ_保存で書かれる() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_テクスチャ.zip");
        var edit = await OpenEditAsync(app, TwoVariations());
        app.PickAttachFiles = () => [zip];
        var texture = Row(edit, "テクスチャ");
        Assert.True(texture.CanAddFiles);

        texture.AddFilesCommand!.Execute(null);
        await SettleAttachAsync(app, edit);

        // 紐付けはその場で書く。種類は画面の上で結び、選ぶ欄で選んだときと同じく「保存して次へ」で書く
        Assert.NotNull(await StoredFileAsync(app, zip));
        Assert.Contains(texture.LinkedFiles, file => file.Name == "作り物_テクスチャ.zip");
        Assert.DoesNotContain(Row(edit, "本体").LinkedFiles, file => file.Name == "作り物_テクスチャ.zip");
        Assert.DoesNotContain(edit.FileSortRows, row => row.Name == "作り物_テクスチャ.zip" && row.IsUnassigned);
        Assert.Null((await StoredFileAsync(app, zip))!.VariationId);

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(2, (await StoredFileAsync(app, zip))!.VariationId);
    });

    [Fact]
    public Task バリエーションの行から足しても_除外の窓でやめれば種類にも結ばない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_除外.zip");
        await app.Store.Excluded.SaveAsync(
            [new ExcludedEntry { Hash = await Core.Scanning.FileHasher.ComputeSha256Async(zip), Paths = [zip], ExcludedAt = DateTimeOffset.Now }]);
        var edit = await OpenEditAsync(app, TwoVariations());
        app.PickAttachFiles = () => [zip];
        var texture = Row(edit, "テクスチャ");

        texture.AddFilesCommand!.Execute(null);
        await SettleAttachAsync(app, edit);

        Assert.Single(app.Notices);
        Assert.Null(await StoredFileAsync(app, zip));
        Assert.Empty(texture.LinkedFiles);

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        Assert.Null(await StoredFileAsync(app, zip));
    });

    [Fact]
    public Task もう紐付いているファイルをバリエーションの行から選んでも_種類は変えない() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_本体2.zip");
        var edit = await OpenEditAsync(app, TwoVariations());
        app.PickAttachFiles = () => [zip];
        edit.ItemPage!.AttachFilesCommand.Execute(null);
        await SettleAttachAsync(app, edit);

        Row(edit, "テクスチャ").AddFilesCommand!.Execute(null);
        await SettleAttachAsync(app, edit);

        Assert.Equal("「作り物_本体2.zip」は、もうこの商品に紐付いています。", edit.ItemPage!.FilesNotice.Text);
        Assert.DoesNotContain(Row(edit, "テクスチャ").LinkedFiles, file => file.Name == "作り物_本体2.zip");
    });

    [Fact]
    public Task 買った種類が1つなら_行から足しても種類は書かずに自動で見せる() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("作り物_本体2.zip");
        var edit = await OpenEditAsync(app, TwoVariations(buyBoth: false));
        app.PickAttachFiles = () => [zip];
        var body = Row(edit, "本体");

        body.AddFilesCommand!.Execute(null);
        await SettleAttachAsync(app, edit);
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        Assert.Contains(body.LinkedFiles, file => file.Name == "作り物_本体2.zip");
        Assert.Null((await StoredFileAsync(app, zip))!.VariationId);
    });

    [Fact]
    public Task 選ぶ欄の空欄の文字と読み上げの名前は_ファイルを選択() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app, TwoVariations());
        var texture = Row(edit, "テクスチャ");

        Assert.Equal("ファイルを選択…", texture.FileChoiceHint);
        Assert.Equal("テクスチャのファイルを選択", texture.FileChoiceAutomationName);
        Assert.Equal("テクスチャにファイルを追加", texture.AddFilesAutomationName);
    });
}
