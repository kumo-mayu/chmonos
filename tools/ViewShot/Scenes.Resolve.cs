using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;

namespace ViewShot;

internal static partial class Scenes
{
    private static IEnumerable<Scene> Resolve =>
    [
        new Scene("resolve-broken-zip", "未確定：壊れたzipの札のある行・長い名前・選んだときの右の欄", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.BrokenZip(@"ダウンロード\途中で切れた衣装セット_v2.1_フルパッケージ版_対応アバター12体同梱_テクスチャ4K_PSD付き.zip"), broken: true),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage", "readme.txt"),
                    contents: ["hair_ribbon.unitypackage", "readme.txt"]),
                Fake.Unresolved(Fake.BrokenZip(@"ダウンロード\short.zip"), size: 1_200, broken: true),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\まとめ買い\accessory_pack.zip", "ring.unitypackage", "necklace.unitypackage"),
                    contents: ["ring.unitypackage", "necklace.unitypackage"]),
            ]);

            var main = await context.StartAsync();
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 4, "未確定の一覧が並ぶ");

            // 壊れた zip の長い名前の行を選ぶ（右の欄に、その行の中身と決める欄が出る）
            screen.Selected = screen.Files.First(row => row.IsBrokenArchive && row.File.SizeBytes > 10_000);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        }),

        new Scene("resolve-row-badges", "未確定：一覧の行だけ（壊れたzipの札・幅の狭い一覧での折り返し）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.BrokenZip(@"ダウンロード\途中で切れた衣装セット_v2.1_フルパッケージ版_対応アバター12体同梱_テクスチャ4K_PSD付き.zip"), broken: true),
                Fake.Unresolved(Fake.BrokenZip(@"ダウンロード\short.zip"), size: 1_200, broken: true),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip"), contents: ["readme.txt"]),
            ]);

            var main = await context.StartAsync();
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 3, "未確定の一覧が並ぶ");

            return new Shot(root) { Focus = () => Look.Named<ListBox>(root, "FilesList") };
        })
        {
            // 行は3つ。一覧の下の空きを長々と撮らない
            Height = 330,
        },

        new Scene("resolve-bundles", "未確定：展開元のzipでまとまった束が2つと、束でない行（束の見出し・間の線・字下げ）", async context =>
        {
            var first = Fake.MissingPath(@"ダウンロード\costume_set_v2.zip");
            var second = Fake.MissingPath(@"ダウンロード\accessory_pack_とても長い名前のまとめ買いセット_2026年夏.zip");
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\costume.unitypackage"), originZip: first),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\texture\costume_4k.psd"), size: 310_000_000, originZip: first),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\readme.txt"), size: 2_400, originZip: first),
                Fake.Unresolved(Fake.PlainFile(@"展開\accessory_pack\ring.unitypackage"), size: 8_100_000, originZip: second),
                Fake.Unresolved(Fake.PlainFile(@"展開\accessory_pack\necklace.unitypackage"), size: 6_300_000, originZip: second),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip"), contents: ["readme.txt"]),
            ]);

            var main = await context.StartAsync();
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 6, "未確定の一覧が並ぶ");
            await context.SettleAsync();

            // 束は畳んで始まる。開いた束と畳んだ束の両方が見えるように、上の1つだけ開く。
            // 開いた・畳んだは View が持つ（ViewModel に無い）ので、三角を押す代わりに部品の値を替える
            var list = Look.Named<ListBox>(root, "FilesList")!;
            Look.All<Expander>(list).First().IsExpanded = true;
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Named<ListBox>(root, "FilesList") };
        })
        {
            Height = 560,
        },
    ];
}
