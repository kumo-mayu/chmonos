using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace ViewShot;

/// <summary>
/// アバター・タグ・属性の管理で、右の欄から開いた商品ページを右の欄に重ねた所（open.md 95 行の残り・2026-11-19）。
/// 頭の「← 一覧に戻る」と、右の欄が狭い時の商品ページの形を見る（狭い形は --size で窓を縮めて撮る。フォルダの folder-right-item と同じ）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> RightPaneItemScenes =>
    [
        new Scene("avatars-right-item", "アバターの管理：右の「商品ページを開く」で、商品ページを右の欄に重ねた所", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            avatars.Selected = avatars.Rows.First(row => row.ItemId == OwnedAvatarId);
            await SceneContext.UntilAsync(() => avatars.CanOpenItem, "商品ページを開ける");
            avatars.OpenItemCommand.Execute(null);
            return await RightItemShownAsync(context, root);
        }),
        new Scene("tag-manage-right-item", "タグの管理：中の商品のカードを押して、商品ページを右の欄に重ねた所", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: true);
            var tags = context.Screen<TagManageViewModel>();
            tags.Lines.OfType<ManageItemLine>().First().Items[0].OpenCommand!.Execute(null);
            return await RightItemShownAsync(context, root);
        }),
        new Scene("attribute-manage-right-item", "属性の管理：中の商品の行を押して、商品ページを右の欄に重ねた所", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            var attributes = context.Screen<AttributeManageViewModel>();
            attributes.Items[0].OpenCommand!.Execute(null);
            return await RightItemShownAsync(context, root);
        }),
    ];

    private static async Task<Shot> RightItemShownAsync(SceneContext context, FrameworkElement root)
    {
        await SceneContext.UntilAsync(() => Look.View<ItemView>(root) is not null, "右に商品ページが出る");
        await context.SettleAsync();
        return new Shot(root);
    }
}
