using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 152（ユーザ判断 2026-11-20・案A）の確かめ：共通素体に付けた画像を、一覧の行の頭と素体の詳細の左に出し、
/// 詳細の絵の下に「画像を変更…」「画像を外す」を置いた所。画像の無い素体は頭の字。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AvatarBaseImageScenes =>
    [
        new Scene("avatars-base-image", "アバターの管理：共通素体に画像を付けた所（一覧の行の頭・詳細の左の絵と、その下の「画像を変更…」「画像を外す」）", async context =>
        {
            var (avatars, root) = await OpenBaseImageAsync(context);
            // 付けると置き場に写すので、元の一時ファイルは要らない（撮るたびに一時フォルダへ溜めない）
            var picked = WriteGradientPng();
            try
            {
                await avatars.SetBaseImageFromFileAsync("作り物の素体", picked);
            }
            finally
            {
                File.Delete(picked);
            }

            await context.SettleAsync();
            await SceneContext.UntilAsync(() => avatars.SelectedBase?.HasOwnImage == true, "付けた画像が行に出る");
            await context.SettleAsync();

            var remove = Look.All<Button>(root).FirstOrDefault(button =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "AvatarBaseRemoveImage");
            if (remove is not { IsVisible: true })
            {
                throw new InvalidOperationException("画像を付けたのに「画像を外す」が出ていない。");
            }

            return new Shot(root);
        }),

        new Scene("avatars-base-image-none", "アバターの管理：画像も素体の商品も無い共通素体（頭の字・「画像を設定…」だけ）", async context =>
        {
            var (avatars, root) = await OpenBaseImageAsync(context);
            if (avatars.SelectedBase?.HasOwnImage != false)
            {
                throw new InvalidOperationException("画像の無い素体を選べていない。");
            }

            return new Shot(root);
        }),

        // open.md 73 行の続き（ユーザ指示 2026-11-18）：素体を選ぶ・探す欄の候補にも素体の絵を出す。
        // 候補の入れ物はポップアップで撮れないので、本物の画面の候補・群・絵の引き方で中身だけを並べる（SuggestPanel）
        new Scene("suggest-base-icons", "素体の候補の絵：商品ページの対応アバターを追加／変換のつなぎ先を選ぶ欄／アバターの詳細の共通素体（画像を付けた素体は絵、無い素体は頭の字）", async context =>
        {
            const string hash = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD";
            await context.Fake.ItemAsync("9800401", "作り物のアバター「アオイ」", images: 1);
            await context.Seed.Avatars.SaveAsync(new AvatarRegistry
            {
                Entries =
                [
                    new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター", BaseName = "作り物の素体", IsOwnedManually = true },
                    new AvatarRegistryEntry { ItemId = "9800402", BoothName = "作り物のアバター「モモ」", DisplayName = "モモ", Category = "3Dキャラクター" },
                ],
                BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体", IsManual = true }, new AvatarBaseGroup { Name = "画像の無い素体", IsManual = true }],
            });
            new Chmonos.Core.Storage.UnityPackagePathStore(Chmonos.Core.Storage.AppPaths.Default).Add(hash, "Profile.unitypackage",
            [
                new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2fakeaoi.json"),
            ]);
            var item = await context.Fake.ItemAsync("9900251", "作り物のプロファイル", record => record with
            {
                Local = record.Local with
                {
                    LocalFiles =
                    [
                        new LocalFileRecord
                        {
                            Hash = hash,
                            Paths = [@"D:\fake\profile.zip"],
                            SizeBytes = 1,
                            Contents = ["Profile.unitypackage"],
                            UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                        },
                    ],
                },
            });

            var main = await context.StartAsync();
            await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 2, "商品を読み終える");
            var picked = WriteGradientPng();
            try
            {
                await context.Services.Avatars.SetBaseImageAsync("作り物の素体", await File.ReadAllBytesAsync(picked));
            }
            finally
            {
                File.Delete(picked);
            }

            var page = new ItemViewModel(item, context.Services, main, main.Thumbnails);
            main.ShowAvatarsCommand.Execute(null);
            var avatars = context.Screen<AvatarsViewModel>();
            await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.BaseNames.Count == 2, "素体の候補が並ぶ");
            if (!page.Conversion.ShowsConversion)
            {
                throw new InvalidOperationException("プロファイルの商品なのに変換扱いの欄が出ていない。");
            }

            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(SuggestPanel("商品ページの「対応アバターを追加」", page.SupportSuggestions, string.Empty,
                page.SupportSuggestInfoSelector, page.SupportGroupHeadings, width: 240, icons: page.AvatarIconSelector));
            host.Children.Add(SuggestPanel("変換のつなぎ先を選ぶ欄", page.Conversion.TargetSuggestions, string.Empty,
                page.Conversion.TargetInfoSelector, page.Conversion.TargetGroupHeadings, width: 220, icons: page.Conversion.TargetIconSelector));
            host.Children.Add(SuggestPanel("アバターの詳細の「共通素体」", [.. avatars.BaseNames], string.Empty,
                _ => null, null, width: 240, icons: avatars.BaseIconSelector));
            return new Shot(SceneContext.OnSurface(host, 20));
        })
        {
            Width = null,
            Height = null,
        },
    ];

    private static async Task<(AvatarsViewModel Avatars, FrameworkElement Root)> OpenBaseImageAsync(SceneContext context)
    {
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9900701", BoothName = "作り物のアバター「アオイ」", Category = "3Dキャラクター", BaseName = "作り物の素体" },
                new AvatarRegistryEntry { ItemId = "9900702", BoothName = "作り物のアバター「ツムギ」", Category = "3Dキャラクター", BaseName = "作り物の素体" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "MARUBODY" }, new AvatarBaseGroup { Name = "作り物の素体", IsManual = true }],
        });

        var main = await context.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var avatars = context.Screen<AvatarsViewModel>();
        await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.Bases.Count == 2, "共通素体が並ぶ");
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.SelectedBase = avatars.Bases.First(row => row.Name == "作り物の素体");
        await context.SettleAsync();
        return (avatars, root);
    }

    /// <summary>斜めの色の帯の作り物の画像（購入した絵を使わない）。置き場は一時フォルダ</summary>
    private static string WriteGradientPng()
    {
        const int Size = 256;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(
                new LinearGradientBrush(Color.FromRgb(0x7c, 0xb3, 0xe8), Color.FromRgb(0xe8, 0x9c, 0xc4), 45),
                null,
                new Rect(0, 0, Size, Size));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xc0, 0xff, 0xff, 0xff)), null, new Point(Size / 2, Size * 0.42), 56, 56);
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var path = Path.Combine(Path.GetTempPath(), $"viewshot-base-{Guid.NewGuid():N}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
