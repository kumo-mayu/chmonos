using System.IO;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 右クリックにしか無かった操作を画面にも置いた所（ユーザ判断 2026-10-06・メモ73）。
/// 取り込み画面の3つの一覧の「エクスプローラで開く」と、商品の画像の「この画像を削除」。
/// 幅900で行の右端のボタンが溢れないか・赤と普通の色の並びを見る
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> RowButtons =>
    [
        new Scene("import-row-reveal", "取り込み画面：3つの一覧の各行（赤いボタンは1語で同じ幅。見つからない・つながっていない行は「エクスプローラで開く」が薄い）", async context =>
        {
            // 在る場所（試験の置き場）・見つからない場所・つながっていないドライブの上の3通り（メモ75：後ろの2つは押せなくして理由を言う）
            var existing = Path.GetTempPath().TrimEnd('\\');
            var used = DriveInfo.GetDrives().Select(drive => drive.Name[0]).ToHashSet();
            var unusedDrive = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !used.Contains(c));
            string[] folders =
            [
                @"D:\作り物のフォルダ\とても長い名前のフォルダ（入れ子1）\さらに長い名前のフォルダ（入れ子2）\もっと深い所にあるダウンロードの置き場\2026年9月に買った分",
                existing,
                $@"{unusedDrive}:\外付け\作り物",
            ];
            var main = await context.StartAsync(settings => settings with { WatchedFolders = folders });
            main.ShowImportCommand.Execute(null);
            var import = context.Screen<ImportViewModel>();
            foreach (var folder in folders)
            {
                import.Folders.Add(folder);
                import.History.Add(folder);
            }

            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => import.Watched.Count == folders.Length, "監視フォルダが届く");
            await import.PlaceCheckTask;
            await context.SettleAsync();

            return new Shot(root);
        })
        {
            Height = 1500,
        },

        new Scene("item-page-gallery-remove-booth", "商品ページ：BOOTHの画像を選んでいる所（「この画像を削除」は出したまま押せない）", async context =>
        {
            var item = await context.Fake.ItemAsync("9900421", "作り物の衣装", images: 3);
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        }),

        new Scene("item-page-gallery-remove-own", "商品ページ：自分で足した画像を選んでいる所（「この画像を削除」が押せる）", async context =>
        {
            var item = await context.Fake.ItemAsync("9900422", "作り物の衣装", record => record with
            {
                Local = record.Local with { UserImages = [new UserImage { FileName = "user-0a1b2c3d.webp" }] },
            }, images: 2);
            Fake.Image(context.Seed.Paths.ItemImagesDir(item.Id), "user-0a1b2c3d.webp", seed: "own");
            var root = await OpenItemAsync(context, item);
            var page = context.Screen<ItemViewModel>();
            page.SelectImageCommand.Execute(page.Images.First(image => image.IsUserAdded));
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];
}
