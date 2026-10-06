using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 属性の統合は、始めた時の属性に固定する（外部の点検 2026-10-06）。
/// 件数を数える間に一覧で別の属性を選び直すと、前は待った後に選び直した属性を統合し、その評価値を失っていた
/// </summary>
public sealed class AttributeMergeSelectionTests
{
    private static ItemRecord Rated(string id, string attribute, int value)
    {
        var item = Make.Item(id, "作り物の衣装");
        return item with { Local = item.Local with { Attributes = new Dictionary<string, int> { [attribute] = value } } };
    }

    [Fact]
    public Task 件数を数える間に別の属性を選び直すと_統合せずにやめ_どちらの評価も残る() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "甲" }, new AttributeDefinition { Name = "乙" }, new AttributeDefinition { Name = "寄せ先" }],
        });
        await app.AddItemAsync(Rated("9900001", "甲", 30));
        await app.AddItemAsync(Rated("9900002", "乙", 80));
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        attributes.Selected = attributes.Rows.Single(row => row.Name == "甲");
        var counting = new TaskCompletionSource<AttributeMergePreview>();
        string? countedFor = null;
        attributes.PreviewMergeForTest = (from, _) => { countedFor = from; return counting.Task; };
        var dialogs = 0;
        AttributeManageViewModel.MergeDialogIntercept = _ => { dialogs++; return true; };
        try
        {
            var renaming = attributes.RenameAsync("寄せ先");

            // 数えている間に、一覧で別の属性を選び直す
            attributes.Selected = attributes.Rows.Single(row => row.Name == "乙");
            counting.SetResult(new AttributeMergePreview { ItemCount = 1, Conflicts = 0 });
            await renaming;
            await app.SettleAsync();

            Assert.Equal("甲", countedFor);
            Assert.Equal(0, dialogs);
            Assert.Equal(30, (await app.Store.Items.LoadAsync("9900001"))!.Local.Attributes["甲"]);
            Assert.Equal(80, (await app.Store.Items.LoadAsync("9900002"))!.Local.Attributes["乙"]);
            Assert.Contains(app.Store.Attributes.Load().Attributes, attribute => attribute.Name == "乙");
        }
        finally
        {
            AttributeManageViewModel.MergeDialogIntercept = null;
        }
    });

    [Fact]
    public Task 選び直さなければ_数えた属性をそのまま統合する() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "甲" }, new AttributeDefinition { Name = "寄せ先" }],
        });
        await app.AddItemAsync(Rated("9900001", "甲", 30));
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await app.SettleAsync();

        attributes.Selected = attributes.Rows.Single(row => row.Name == "甲");
        string? shownFor = null;
        AttributeManageViewModel.MergeDialogIntercept = model => { shownFor = model.From; return true; };
        app.Answer = request => request.Button == System.Windows.MessageBoxButton.YesNo ? System.Windows.MessageBoxResult.Yes : System.Windows.MessageBoxResult.OK;
        try
        {
            await attributes.RenameAsync("寄せ先");
            await app.SettleAsync();

            Assert.Equal("甲", shownFor);
            Assert.Equal(30, (await app.Store.Items.LoadAsync("9900001"))!.Local.Attributes["寄せ先"]);
            Assert.DoesNotContain(app.Store.Attributes.Load().Attributes, attribute => attribute.Name == "甲");
        }
        finally
        {
            AttributeManageViewModel.MergeDialogIntercept = null;
        }
    });
}
