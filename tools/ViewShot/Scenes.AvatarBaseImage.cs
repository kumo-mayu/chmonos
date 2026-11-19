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
