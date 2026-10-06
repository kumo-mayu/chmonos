using System.IO;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

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

        new Scene("resolve-unpacked-folder", "未確定：元zipの無い展開物（長いフォルダ名）を選んだ右の欄（上の帯・見出しの横のボタン・その他のフォルダのまま登録）", async context =>
        {
            // 展開物の根は、中身がそのフォルダしか無い親まで遡る。取り込み元に別の物も置き、根を展開したフォルダで止める
            const string folder = @"展開物だけ\とても長い名前の衣装セット_v2.1_フルパッケージ版_PSD付き";
            var package = Fake.PlainFile(folder + @"\outfit\outfit.unitypackage");
            var texture = Fake.PlainFile(folder + @"\outfit\texture\outfit_body_main_texture_4k.png");
            Fake.PlainFile(@"展開物だけ\ほかの物\readme.txt");
            var importFolder = Fake.Folder("展開物だけ");
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(package, size: 8_100_000),
                Fake.Unresolved(texture, size: 31_000_000),
            ]);

            var main = await context.StartAsync(settings => settings with { ImportFolders = [importFolder] });
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 2, "未確定の一覧が並ぶ");
            screen.Selected = screen.Files.First(row => row.IsArchiveContent);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            // 右の欄を下の「その他」まで1枚に収める
            Height = 1900,
        },

        new Scene("resolve-checked-local", "未確定：「このフォルダを選択」で選んだときの右の欄（まとめ操作の3つのボタン・その他の「選択した n 件をこの名前で登録する」）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_set\ribbon_body.psd"), size: 120_000_000),
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_set\ribbon_extra.psd"), size: 40_000_000),
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_set\ribbon_icon.png"), size: 900_000),
                Fake.Unresolved(Fake.Zip(@"選んで登録\hair_ribbon_v1.0.zip"), contents: ["readme.txt"]),
            ]);

            var main = await context.StartAsync();
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 4, "未確定の一覧が並ぶ");
            screen.SelectFolderCommand.Execute(screen.Files.First(row => row.FileName == "ribbon_body.psd").GroupKey);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1900,
        },

        new Scene("resolve-many-contents", "未確定：中身7万件の zip の行を選んだ右の欄（中身の一覧は見える行だけ作る。選ぶ・離れる・戻るの時間と作った行の数を書き出す）", async context =>
        {
            // 大容量の確かめ（2026-09-30）で、中身7万件の zip の行を選ぶと約49秒止まり、メモリが約1GBまで上がって戻らなかった。
            // 一覧を仮想化して直した（00f242f）。アプリを起動せずに同じ数で効いているかを確かめられるよう、時間と作った行を書き出す
            const int count = 70_000;
            var contents = Enumerable.Range(0, count)
                .Select(i => $"Assets/Fake/Folder{i / 100:000}/texture_{i:00000}.png")
                .ToArray();
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\中身の多い作り物.zip"), contents: contents),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip"), contents: ["readme.txt"]),
            ]);

            var main = await context.StartAsync();
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 2, "未確定の一覧が並ぶ");
            var big = screen.Files.First(row => row.File.Contents.Count == count);
            var small = screen.Files.First(row => row.File.Contents.Count == 1);

            screen.Selected = small;
            await context.SettleAsync();
            var before = Memory();

            // 選ぶ・離れる・戻る。止まりは画面のスレッドで組み終わるまで（UpdateLayout）の時間で見る
            var choose = Lap(big);
            var made = Look.All<Chmonos.App.Controls.ContentItemsControl>(root).Sum(list => Look.All<TextBlock>(list).Count());
            var leave = Lap(small);
            var back = Lap(big);
            await context.SettleAsync();
            var after = Memory();

            Console.WriteLine(
                $"  中身 {count:N0} 件：選ぶ {choose} ms・離れる {leave} ms・戻る {back} ms・作った行 {made}・"
                + $"GC のヒープ {before.Heap} → {after.Heap} MB・作業セット {before.WorkingSet} → {after.WorkingSet} MB");

            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };

            long Lap(UnresolvedRow row)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                screen.Selected = row;
                root.UpdateLayout();
                return clock.ElapsedMilliseconds;
            }

            static (long Heap, long WorkingSet) Memory()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                return (GC.GetTotalMemory(forceFullCollection: true) / 1_048_576, process.WorkingSet64 / 1_048_576);
            }
        }),

        // 右の欄の組み立ての案（メモ22）を、対象の4つの状態で見比べるための場面。案の枝だけに置く
        new Scene("resolve-target-single", "未確定：対象が1件（zip 1つ）のときの右の欄", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage", "readme.txt"),
                    contents: ["hair_ribbon.unitypackage", "readme.txt"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\accessory_pack.zip", "ring.unitypackage"), contents: ["ring.unitypackage"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 2);
            screen.Selected = screen.Files.First(row => row.FileName == "hair_ribbon_v1.0.zip");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-registering-eta", "未確定：登録している最中の帯に、BOOTHへの問い合わせの残りの件数と目安の時間（メモ34）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage", "readme.txt"),
                    contents: ["hair_ribbon.unitypackage", "readme.txt"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\accessory_pack.zip", "ring.unitypackage"), contents: ["ring.unitypackage"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 2);
            screen.Selected = screen.Files.First(row => row.FileName == "hair_ribbon_v1.0.zip");
            await context.SettleAsync();
            Backdoor.ShowRegisteringInDecision(screen, done: 0, total: 1, left: 14);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-queue", "未確定：登録の順番待ち（メモ60）。行の札（登録中・登録待ち・登録に失敗・検索中）と、待っている行を選んだ右の欄", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage"), contents: ["hair_ribbon.unitypackage"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\accessory_pack.zip", "ring.unitypackage"), contents: ["ring.unitypackage"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\winter_coat_full.zip", "coat.unitypackage"), contents: ["coat.unitypackage"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\shoes_set.zip", "shoes.unitypackage"), contents: ["shoes.unitypackage"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 4);
            string HashOf(string name) => screen.Files.First(row => row.FileName == name).File.Hash;

            Backdoor.ShowRegistrationQueue(
                screen,
                context.Main.Registrations,
                [
                    (HashOf("hair_ribbon_v1.0.zip"), "9900801", "作り物のリボン", 40),
                    (HashOf("accessory_pack.zip"), "9900802", "作り物のアクセサリー", 12),
                ],
                requestsLeft: 31,
                failed: (HashOf("shoes_set.zip"), "商品ID 9900804 には確定できませんでした。"));

            // 検索中の札（別の行の検索が走っている）
            context.Main.ResolveSearch.Begin(@"C:\ダウンロード\winter_coat_full.zip", [HashOf("winter_coat_full.zip")]);

            screen.Selected = screen.Files.First(row => row.FileName == "accessory_pack.zip");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1100,
        },

        new Scene("resolve-header-notice", "未確定：上の帯の右に結果の知らせが出たとき、左の画面の説明と重ならず、説明の方が「…」で切れる（2026-10-06 の手触りの確認）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage"), contents: ["hair_ribbon.unitypackage"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 1);

            // 知らせは行ごと消える操作の結果だけが書く（private set）。台では文だけを入れて見た目を見る
            typeof(Chmonos.App.ViewModels.ResolveViewModel).GetProperty(nameof(Chmonos.App.ViewModels.ResolveViewModel.ListNoticeText))!
                .SetValue(screen, "「作り物のとても長い名前の衣装セット」を登録しました。");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Width = 900,
            Height = 300,
        },

        new Scene("resolve-target-images","未確定：BOOTHに無い商品に画像を2枚添えたときのギャラリー（枠と＋の枠）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\hair_ribbon_v1.0.zip", "hair_ribbon.unitypackage"), contents: ["hair_ribbon.unitypackage"]),
            ]);
            var pictures = Path.Combine(Isolation.FilesRoot, "画像");
            Fake.Image(pictures, "front.png", "front");
            Fake.Image(pictures, "back.png", "back");
            var (root, screen) = await OpenResolveAsync(context, 1);
            screen.Selected = screen.Files[0];
            screen.AddLocalImages([Path.Combine(pictures, "front.png"), Path.Combine(pictures, "back.png")]);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-not-on-booth", "未確定：取り込みが BOOTH から「無い」と言われた行を選んだとき（左の札「BOOTHで非公開」・初めから欄にID・説明・名前・画像の枠・このIDで登録。ユーザ 2026-10-06）", async context =>
        {
            // 取り込みが残した答え（notOnBooth）を持つ行。選ぶだけで形が出る（BOOTHへは聞かない）
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\winter_coat_2025.zip", "coat.unitypackage"), contents: ["coat.unitypackage"],
                    candidates: ["9900901"], notOnBooth: "9900901"),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\accessory_pack.zip", "ring.unitypackage"), contents: ["ring.unitypackage"]),
            ]);
            var pictures = Path.Combine(Isolation.FilesRoot, "画像");
            Fake.Image(pictures, "front.png", "front");
            var (root, screen) = await OpenResolveAsync(context, 2);
            screen.Selected = screen.Files.First(row => row.FileName == "winter_coat_2025.zip");
            await context.SettleAsync();
            screen.AddLocalImages([Path.Combine(pictures, "front.png")]);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-not-on-booth-overturned", "未確定：非公開と思って画像を添えた行を「情報を確認」で聞き直したら公開されていた後（ふつうの登録の形に画像の枠が残り、外せる。ユーザ 2026-10-06）", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\winter_coat_2025.zip", "coat.unitypackage"), contents: ["coat.unitypackage"],
                    candidates: ["9900901"], notOnBooth: "9900901"),
            ]);
            var pictures = Path.Combine(Isolation.FilesRoot, "画像");
            Fake.Image(pictures, "front.png", "front");
            Fake.Image(pictures, "back.png", "back");
            var (root, screen) = await OpenResolveAsync(context, 1);
            screen.Selected = screen.Files.First(row => row.FileName == "winter_coat_2025.zip");
            await context.SettleAsync();
            screen.AddLocalImages([Path.Combine(pictures, "front.png"), Path.Combine(pictures, "back.png")]);
            await context.SettleAsync();
            Backdoor.ShowOverturnedPreview(screen, "9900901", "作り物の再公開された冬のコート");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-target-bundle","未確定：元zipを展開した中身（束 3 件・元のzipも一覧にある）の1行を選んだ右の欄", async context =>
        {
            var zip = Fake.Zip(@"ダウンロード\costume_set_v2.zip", "costume.unitypackage", "costume_4k.psd", "readme.txt");
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(zip, contents: ["costume.unitypackage", "costume_4k.psd", "readme.txt"]),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\costume.unitypackage"), originZip: zip),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\costume_4k.psd"), size: 310_000_000, originZip: zip),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\readme.txt"), size: 2_400, originZip: zip),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 4);
            screen.Selected = screen.Files.First(row => row.FileName == "costume.unitypackage");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-target-origin-unlisted", "未確定：元zipはディスクにあるが未確定の一覧に無い中身（束 3 件）。左の見出し・1行にも帯にも「元zipとして扱う」を出さず、帯は文だけ（ふつうの色）", async context =>
        {
            var zip = Fake.Zip(@"ダウンロード\costume_set_v2.zip", "costume.unitypackage", "costume_4k.psd", "readme.txt");
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\costume.unitypackage"), originZip: zip),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\costume_4k.psd"), size: 310_000_000, originZip: zip),
                Fake.Unresolved(Fake.PlainFile(@"展開\costume_set_v2\readme.txt"), size: 2_400, originZip: zip),
                Fake.Unresolved(Fake.PlainFile(@"展開\single\one.unitypackage"), originZip: Fake.Zip(@"ダウンロード\single.zip", "one.unitypackage")),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 4);
            screen.Selected = screen.Files.First(row => row.FileName == "costume.unitypackage");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-target-checked","未確定：一覧で3件にチェックを入れたときの右の欄", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_body.psd"), size: 120_000_000),
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_extra.psd"), size: 40_000_000),
                Fake.Unresolved(Fake.PlainFile(@"選んで登録\ribbon_icon.png"), size: 900_000),
                Fake.Unresolved(Fake.Zip(@"選んで登録\hair_ribbon_v1.0.zip"), contents: ["readme.txt"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 4);
            foreach (var row in screen.Files.Where(row => row.FileName.StartsWith("ribbon_", StringComparison.Ordinal)))
            {
                row.IsSelected = true;
            }

            screen.Selected = screen.Files.First(row => row.FileName == "ribbon_body.psd");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },

        new Scene("resolve-target-folder", "未確定：zipが無い展開物のフォルダ（2件）の1行を選んだ右の欄", async context =>
        {
            const string folder = @"展開物だけ\衣装セット_v2.1_PSD付き";
            var package = Fake.PlainFile(folder + @"\outfit\outfit.unitypackage");
            var texture = Fake.PlainFile(folder + @"\outfit\texture\outfit_body_main_texture_4k.png");
            Fake.PlainFile(@"展開物だけ\ほかの物\readme.txt");
            var importFolder = Fake.Folder("展開物だけ");
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(package, size: 8_100_000),
                Fake.Unresolved(texture, size: 31_000_000),
            ]);

            var main = await context.StartAsync(settings => settings with { ImportFolders = [importFolder] });
            main.ShowResolveCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            var screen = context.Screen<ResolveViewModel>();
            await SceneContext.UntilAsync(() => screen.Files.Count == 2, "未確定の一覧が並ぶ");
            screen.Selected = screen.Files.First(row => row.IsArchiveContent);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ResolveView>(root) };
        })
        {
            Height = 1500,
        },
    ];

    private static async Task<(System.Windows.FrameworkElement Root, ResolveViewModel Screen)> OpenResolveAsync(SceneContext context, int count)
    {
        var main = await context.StartAsync();
        main.ShowResolveCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var screen = context.Screen<ResolveViewModel>();
        await SceneContext.UntilAsync(() => screen.Files.Count == count, "未確定の一覧が並ぶ");
        return (root, screen);
    }
}
