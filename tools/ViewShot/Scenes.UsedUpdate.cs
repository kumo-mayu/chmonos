using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>使っている商品の更新の促し（docs/spec/used-updates.md）。名前・ID は作り物。</summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> UsedUpdateScenes =>
    [
        new Scene("item-page-used-update", "商品ページの上の帯：改変2件・Unityプロジェクト0件で使っている衣装に新しい版（更新履歴を見る・確認した）", UsedUpdateItemAsync),
    ];

    private static async Task<Shot> UsedUpdateItemAsync(SceneContext context)
    {
        var item = await context.Fake.ItemAsync(
            "9907501",
            "作り物の衣装",
            record => record with
            {
                Booth = record.Booth with
                {
                    H2Sections =
                    [
                        new H2Section { Heading = "商品説明", Text = "作り物の衣装です。" },
                        new H2Section { Heading = "更新履歴", Text = "2026/10/01 v1.0 公開\n2026/11/01 v1.1 スカートの破綻を修正" },
                    ],
                },
                Local = record.Local with
                {
                    ContentUpdate = new ContentUpdate
                    {
                        FoundAt = DateTimeOffset.Now.AddDays(-1),
                        FilesBefore = record.Local.LocalFiles.Select(file => file.Hash).ToList(),
                    },
                },
            },
            images: 1);

        foreach (var (id, name) in new[] { ("mod-0000aaa1", "作り物の改変（夏）"), ("mod-0000aaa2", "作り物の改変（冬）") })
        {
            await context.Seed.Modifications.SaveAsync(new ModificationRecord
            {
                Id = id,
                AvatarItemId = "9907502",
                Name = name,
                Members = [new ModificationMember { ItemId = item.Id }],
            });
        }

        var main = await context.StartAsync();
        await SceneContext.UntilAsync(() => !main.Search.IsLoading, "商品を読み終える");
        await main.UsedItems.RequestAsync();
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);

        return new Shot(root)
        {
            Focus = () => Look.All<System.Windows.Controls.TextBlock>(root)
                .FirstOrDefault(text => System.Windows.Automation.AutomationProperties.GetAutomationId(text) == "ItemUsedUpdateText") is { } text
                    ? Look.Ancestor<System.Windows.Controls.Border>(Look.Ancestor<System.Windows.Controls.Border>(text)!)
                    : null,
            FocusMargin = 8,
        };
    }
}
