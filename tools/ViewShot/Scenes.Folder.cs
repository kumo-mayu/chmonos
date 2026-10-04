using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace ViewShot;

/// <summary>
/// フォルダビューの左の木（2026-10-01 に、ファイル名と商品名の切り替えを足したときに作った）。
/// 同じ置き方で切り替えの切・入を撮り、行の高さ・並び・絵が変わらず、名前の所だけが替わることを見る。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> FolderTree =>
    [
        new Scene("folder-tree-filenames", "フォルダビュー：木をファイル名で出した所（既定）。同じzipを2つの商品が持つ行と未確定を含む", context
            => FolderTreeAsync(context, itemNames: false)),
        new Scene("folder-tree-itemnames", "フォルダビュー：木を商品名で出した所。ファイル名は2行目、未確定はファイル名のまま", context
            => FolderTreeAsync(context, itemNames: true)),

        // 監視をやめたときの知らせ（2026-10-04 担当NA）。出る前と出た後で、下の未確定の枠が動かないことを見る
        new Scene("folder-notice-off", "フォルダビュー：監視の行の下の知らせが出ていない右の欄", context => FolderNoticeAsync(context, show: false)),
        new Scene("folder-notice-on", "フォルダビュー：監視をやめた知らせが出た右の欄", context => FolderNoticeAsync(context, show: true)),
    ];

    private static async Task<Shot> FolderNoticeAsync(SceneContext context, bool show)
    {
        var shot = await FolderTreeAsync(context, itemNames: false);
        var folders = context.Screen<FolderViewModel>();
        if (show && folders.Detail is FolderViewDetail detail)
        {
            detail.WatchNote.Notice("監視をやめました。取り込んだものはそのまま残ります。");
            await context.SettleAsync();
        }

        return shot;
    }

    private static async Task<Shot> FolderTreeAsync(SceneContext context, bool itemNames)
    {
        // 2つ目の商品にも1つ目の zip を持たせる（同じファイルが2つの商品に結び付く行）
        var shared = Fake.Zip(@"ライブラリ\item0.zip");
        await SeedLibraryAsync(context, count: 8, change: (index, record) => index != 1 ? record : record with
        {
            Local = record.Local with { LocalFiles = [.. record.Local.LocalFiles, Fake.FileRecord(shared)] },
        });
        await context.Seed.Unresolved.SaveAsync([Fake.Unresolved(Fake.Zip(@"ライブラリ\どれのか分からない.zip"))]);

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

        main.ShowFolders();
        var folders = context.Screen<FolderViewModel>();
        await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.Entry is not null) || folders.Rows.Any(row => row.CanExpand && !row.IsExpanded), "木が並ぶ");
        while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
        {
            folders.ToggleCommand.Execute(closed);
        }

        (itemNames ? folders.ShowItemNamesCommand : folders.ShowFileNamesCommand).Execute(null);

        // 右は商品のファイルのすぐ上のフォルダ（商品ページを組み込むと、右の読み込みで絵が落ち着かない）
        var file = folders.Rows.First(row => row.Entry?.Item is not null);
        folders.Selected = folders.Rows.Take(folders.Rows.IndexOf(file)).Last(row => row.IsFolderLike);
        await SceneContext.UntilAsync(() => Look.View<FolderBrowserView>(root) is { } view && Look.All<Chmonos.App.Controls.ItemCardBorder>(view).Any(), "右にカードが並ぶ");
        await context.SettleAsync();
        return new Shot(root);
    }
}
