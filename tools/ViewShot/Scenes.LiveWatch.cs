using System.IO;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>開いている間の監視フォルダの見張り（plan 49・2026-11-13）：下の帯と設定の2行。</summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> LiveWatchScenes =>
    [
        new("band-live-watch", "下の帯：開いている間に監視フォルダで新しいファイルを3件見つけた（取り込む・×）", async context =>
        {
            await SeedLibraryAsync(context, count: 3);

            // 設定を入にしておく（明暗を切り替えると設定を保存し、切のままだと帯を下げる）。見張るのは空の一時フォルダ
            var watched = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "chmonos-viewshot-watch")).FullName;
            var main = await context.StartAsync(settings => settings with { WatchFoldersWhileOpen = true, WatchedFolders = [watched] });
            var root = context.MainWindow();
            await context.PresentAsync(root);

            main.ShowLiveNotice([@"D:\作り物\監視\衣装.zip", @"D:\作り物\監視\髪.zip", @"D:\作り物\監視\靴.unitypackage"]);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, main.LiveWatchNoticeText)), FocusMargin = 24 };
        }),
        SettingsLiveWatch("settings-live-watch-on", "設定の「取り込みと取得」：開いている間も探すが入（子の「そのまま取り込む」は押せる）", parent: true),
        SettingsLiveWatch("settings-live-watch-off", "設定の「取り込みと取得」：開いている間も探すが切（子は押せず薄い）", parent: false),
    ];

    private static Scene SettingsLiveWatch(string name, string title, bool parent)
        => new(name, title, async context =>
        {
            var main = await context.StartAsync(settings => settings with { WatchFoldersWhileOpen = parent, AutoImportWatchedFiles = false });
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            settings.SectionImportFetchOpen = true;
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "取り込みと取得")) };
        });
}
