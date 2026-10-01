using System.Collections.Specialized;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の「バリエーションごと」の欄で、付けたファイルを ✕ で外すとき（点検 2026-10-01）。
/// 欄の行（買ったバリエーション）を作り直すと、行の部品ごと作り直されて、キーボードで外した後に隣の ✕ へ止まり直せず、フォーカスが窓へ落ちた。
/// 並ぶ行が変わらないなら作り直さないことと、外したファイルが行から消えることを確かめる
/// </summary>
public class EditFileLinkTests
{
    private static ItemRecord TwoVariations()
    {
        var item = Make.Item("1000001", "作り物の衣装");
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
                Purchases =
                [
                    new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                    new Purchase { VariationId = 2, NameSnapshot = "テクスチャ", Price = 500 },
                ],
                LocalFiles =
                [
                    Make.File(@"D:\files\body.zip") with { VariationId = 1 },
                    Make.File(@"D:\files\body_extra.zip") with { VariationId = 1 },
                    Make.File(@"D:\files\texture.zip") with { VariationId = 2 },
                ],
            },
        };
    }

    [Fact]
    public Task 付けたファイルを外しても_買ったバリエーションの行は作り直さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(TwoVariations());
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);

        var body = edit.PurchasedVariationRows.Single(row => row.Name == "本体");
        Assert.Equal(2, body.LinkedFiles.Count);
        var resets = 0;
        ((INotifyCollectionChanged)edit.PurchasedVariationRows).CollectionChanged += (_, _) => resets++;

        body.LinkedFiles.Single(file => file.Name == "body.zip").UnlinkCommand!.Execute(null);
        await app.SettleAsync();

        Assert.Equal(0, resets);
        Assert.Same(body, edit.PurchasedVariationRows.Single(row => row.Name == "本体"));
        Assert.Equal("body_extra.zip", Assert.Single(body.LinkedFiles).Name);
    });
}
