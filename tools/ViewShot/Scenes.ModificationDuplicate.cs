using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace ViewShot;

/// <summary>
/// メモ44（2026-10-05）の確かめ：改変の複製。詳細の上の帯の「複製」（削除の左）と、左の一覧の改変の行の右クリック「複製」。
/// 台ではメニューを開けないので、宛先だけ手で渡して項目・命令・吹き出しを調べる。複製の中身・名前・開いてフォーカスの印は App の試験で見る
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ModificationDuplicateScenes =>
    [
        new Scene("modification-duplicate", "改変の画面：詳細の上の帯に「複製」が削除の左に並ぶ。左の一覧の改変の行の右クリックにも「複製」", async context =>
        {
            await SeedModificationsAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(ModificationHubLevel.Modification);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Lines.OfType<HubModLine>().Any(), "一覧が並ぶ");
            var record = hub.Lines.OfType<HubModLine>().First().Row.Record;
            hub.ShowModificationCommand.Execute(record);
            await SceneContext.UntilAsync(() => hub.Detail is ModificationViewModel, "右に詳細が開く");
            await context.SettleAsync();

            var detail = (ModificationViewModel)hub.Detail!;
            var button = Look.All<Button>(root).FirstOrDefault(entry =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(entry) == "ModificationDuplicate")
                ?? throw new InvalidOperationException("帯の「複製」のボタンがありません。");
            if (!ReferenceEquals(button.Command, detail.DuplicateCommand) || (string?)button.Content != "複製")
            {
                throw new InvalidOperationException("帯の「複製」の命令が違います。");
            }

            var border = Look.All<Border>(root).FirstOrDefault(entry => entry.ContextMenu is not null && entry.DataContext is HubModificationRow)
                ?? throw new InvalidOperationException("改変の行に右クリックのメニューがありません。");
            var menu = border.ContextMenu!;
            menu.PlacementTarget = border;
            var items = menu.Items.OfType<MenuItem>().ToList();
            if (items.Count != 1 || (string)items[0].Header != "複製" || !items[0].IsEnabled
                || !ReferenceEquals(items[0].Command, hub.DuplicateModificationCommand))
            {
                throw new InvalidOperationException("行の右クリックの項目が違います。");
            }

            Console.WriteLine("  行の右クリック：複製（押せる）／帯のボタン：複製");
            return new Shot(root);
        })
        {
            Width = 1280,
            Height = 700,
        },
    ];
}
