using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// アバターの管理の「変換」の左の欄が多い時（ユーザ判断 2026-11-20・①：探す欄より下を1つの流れにする。プロファイルの商品は数百件あり得る）。
/// プロファイルの商品300件（4つの群に散らす）と追加した組40個。開く・作った行の数・流す・絞って並べ直す重さを測って出し、
/// 探す欄より下で流せる物が1つだけか・組の行の上のホイールが全体を流すかを確かめる。名前・ID は作り物
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ConversionLeftManyScenes =>
    [
        new Scene("avatars-conversion-left-many", "アバターの管理：変換の左の欄に、プロファイルの商品300件と追加した組40個（探す欄より下が1つの流れ。開く・流す・絞る重さを測って出す）", context => ConversionLeftManyAsync(context, LeftManyPairs)),
        new Scene("avatars-conversion-left-many-pairs", "アバターの管理：変換の左の欄に、プロファイルの商品300件と追加した組400個（組の行も見える分だけ作るか。開く・流す・絞る重さを測って出す）", context => ConversionLeftManyAsync(context, LeftManyPairsLarge)),
    ];

    private const int LeftManyItems = 300;
    private const int LeftManyAvatars = 10;
    private const int LeftManyPairs = 40;

    /// <summary>組の行を見える分だけ作るかを見る数（組は手で足す物だが、数百でも開く重さが組の数に比例しないかを測る。ユーザ判断 2026-11-20「組は大きくなる箇所」）。</summary>
    private const int LeftManyPairsLarge = 400;

    private static async Task<Shot> ConversionLeftManyAsync(SceneContext context, int pairCount)
    {
        // 組が同じ元と先で重ならないよう、組が多い時はアバターを増やす（元 i、先 i+1〜i+n-1 の組は n×(n-1) 個。21体で420個）
        var avatarCount = pairCount > LeftManyAvatars * (LeftManyAvatars - 1) ? 21 : LeftManyAvatars;
        var avatarIds = Enumerable.Range(0, avatarCount).Select(index => $"98005{index:D2}").ToArray();
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [.. avatarIds.Select((id, index) => new AvatarRegistryEntry
            {
                ItemId = id, BoothName = $"作り物のアバター「アバター{index:D2}」", DisplayName = $"アバター{index:D2}", Category = "3Dキャラクター",
            })],
        });
        await context.Seed.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [.. Enumerable.Range(0, pairCount).Select(index => new ConversionPair
            {
                From = ConversionEnd.Avatar(avatarIds[index % avatarCount]),
                To = ConversionEnd.Avatar(avatarIds[(index / avatarCount + index + 1) % avatarCount]),
                Memo = index % 3 == 0 ? $"作り物のメモ{index}" : null,
            })],
        });

        var paths = new Chmonos.Core.Storage.UnityPackagePathStore(Chmonos.Core.Storage.AppPaths.Default);
        for (var index = 0; index < LeftManyItems; index++)
        {
            var id = $"99010{index:D2}";
            var hash = $"{index + 1:X64}";
            var slug = $"fakeleft{index:D3}";
            paths.Add(hash, "Profile.unitypackage",
                [new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000001", $"Assets/OutfitRetargetingSystem/Editor/config_template2{slug}.json")]);
            // 4つの群（決めていない・途中・決めた・扱わない）に散らす
            ConversionSetting? conversion = (index % 4) switch
            {
                0 => null,
                1 => new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = avatarIds[index % LeftManyAvatars], Forward = true, Slug = slug }] },
                2 => new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = avatarIds[index % LeftManyAvatars], Forward = true, Slug = slug, Confirmed = true }] },
                _ => new ConversionSetting { Mode = ConversionMode.NotProfile },
            };
            await context.Fake.ItemAsync(id, $"作り物のプロファイル{index:D3}", record => record with
            {
                Local = record.Local with
                {
                    LocalFiles =
                    [
                        new LocalFileRecord
                        {
                            Hash = hash, Paths = [$@"D:\fake\{id}.zip"], SizeBytes = 1, Contents = ["Profile.unitypackage"],
                            UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                        },
                    ],
                    Conversion = conversion,
                },
            }, images: 1);
        }

        var main = await context.StartAsync();
        await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == LeftManyItems, "商品を読み終える");
        main.ShowAvatarsCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var avatars = context.Screen<AvatarsViewModel>();
        await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        await context.SettleAsync();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        avatars.ShowConversionModeCommand.Execute(null);
        root.UpdateLayout();
        var open = watch.Elapsed.TotalMilliseconds;
        await context.SettleAsync();

        var list = Descendants(root).OfType<ListBox>().First(box => AutomationIdOf(box) == "AvatarsConversionList");
        var left = Ancestors(list).OfType<DockPanel>().First();
        int RealizedItems() => Descendants(left).OfType<ListBoxItem>().Count(item => item.DataContext is ConversionItemRowViewModel);
        int RealizedPairs() => Descendants(left).OfType<ListBoxItem>().Count(item => item.DataContext is ConversionPairRow);
        var realized = RealizedItems();
        var realizedPairs = RealizedPairs();
        var scrollers = Descendants(left).OfType<ScrollViewer>().Where(viewer => viewer.IsVisible && viewer.ScrollableHeight > 0).ToList();

        // 流す：左の一覧の流れを、ホイールの1目盛りずつ端まで送る（行単位でも画素単位でも同じ1目盛り）。
        // 流れは一覧の物から取る（組が多いと、最初の画面に商品の行が無い）
        var scroller = Descendants(list).OfType<ScrollViewer>().First();
        var steps = new List<double>();
        var realizedPairsMax = realizedPairs;
        while (scroller.VerticalOffset < scroller.ScrollableHeight && steps.Count < 3000)
        {
            watch.Restart();
            scroller.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            root.UpdateLayout();
            steps.Add(watch.Elapsed.TotalMilliseconds);

            // 時間の外で数える（流す途中で一度に持った組の行の数。全部作るなら組の数になる）
            realizedPairsMax = Math.Max(realizedPairsMax, RealizedPairs());
        }

        var realizedMax = RealizedItems();
        scroller.ScrollToHome();
        root.UpdateLayout();

        // 組の行の上でホイールを回すと、全体の流れが動くか（中の一覧で止まらないか）
        var pairRow = Descendants(left).OfType<ListBoxItem>().FirstOrDefault(item => item.DataContext is ConversionPairRow);
        var wheelMoves = "組の行が見えない";
        if (pairRow is not null)
        {
            var before = scroller.VerticalOffset;
            var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            pairRow.RaiseEvent(preview);
            if (!preview.Handled)
            {
                pairRow.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            }

            root.UpdateLayout();
            wheelMoves = scroller.VerticalOffset > before ? "全体が流れた" : "全体が流れない";
            scroller.ScrollToHome();
            root.UpdateLayout();
        }

        // 絞って並べ直す・絞りを消す（並べ直すたびに一覧を作り直すので、作る行の数がそのまま効く）
        watch.Restart();
        avatars.ConversionPairs.Query = "アバター03";
        root.UpdateLayout();
        var narrow = watch.Elapsed.TotalMilliseconds;
        await context.SettleAsync();
        watch.Restart();
        avatars.ConversionPairs.Query = "";
        root.UpdateLayout();
        var widen = watch.Elapsed.TotalMilliseconds;
        await context.SettleAsync();

        Console.WriteLine($"  変換の左：商品 {LeftManyItems}・組 {pairCount}：開く {open:F0}ms・作った商品の行 {realized}（流し切った後 {realizedMax}）・組の行 {realizedPairs}（流す途中の最多 {realizedPairsMax}）・"
            + $"流せる物 {scrollers.Count}・流す {steps.Count} 目盛り 合計 {steps.Sum():F0}ms 最長 {(steps.Count > 0 ? steps.Max() : 0):F0}ms・"
            + $"絞る {narrow:F0}ms・絞りを消す {widen:F0}ms・組の行の上のホイール：{wheelMoves}");
        return new Shot(root);
    }

    private static string AutomationIdOf(DependencyObject element) => System.Windows.Automation.AutomationProperties.GetAutomationId(element);
}
