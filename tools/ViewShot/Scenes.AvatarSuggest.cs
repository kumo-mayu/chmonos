using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// メモ48・メモ58（2026-10-05）の確かめ：アバターの候補の3群の見出し・呼び方で当たったときの札・改変を選ぶ窓の今ある改変の行の絵。
/// </summary>
internal static partial class Scenes
{
    private static BitmapSource Swatch(Color color)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = 255;
        }

        var bitmap = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static string SwatchFile(string name, Color color)
    {
        var directory = Path.Combine(Path.GetTempPath(), "chmonos-shots", "av7-swatches");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Swatch(color)));
        encoder.Save(stream);
        return path;
    }

    /// <summary>候補の入れ物はポップアップ（別の窓）で撮れないので、ポップアップの中身だけを外して返す。並びは本物の Arrange の答え。</summary>
    private static FrameworkElement SuggestPanel(
        string caption, IReadOnlyList<string> all, string text, Func<string, SuggestInfo?> info, IReadOnlyList<string>? headings, double width = 320)
    {
        var box = new SuggestBox { Width = width };
        var popup = (System.Windows.Controls.Primitives.Popup)box.FindName("DropDown");
        var list = (ListBox)box.FindName("Candidates");
        Func<string, ImageSource?> icon = value => value.StartsWith('作') && value.Contains("絵") ? Swatch(Color.FromRgb(120, 170, 210)) : null;
        list.ItemsSource = SuggestBox.Arrange(all, text, 0, info)
            .Select(row => new Suggestion
            {
                Value = row.Entry,
                Display = row.Entry,
                IconFactory = icon,
                HasDividerAbove = row.DividerAbove,
                Heading = row.GroupStart && headings is not null ? headings[row.Group] : string.Empty,
                Note = row.Hit?.Label ?? string.Empty,
            })
            .ToList();
        list.SelectedIndex = 0;
        var panel = (FrameworkElement)popup.Child;
        popup.Child = null;
        panel.Width = width;
        panel.VerticalAlignment = VerticalAlignment.Top;

        var label = new TextBlock { Text = caption, Margin = new Thickness(0, 0, 0, 6), FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = width };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var stack = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
        stack.Children.Add(label);
        stack.Children.Add(new TextBox { Text = text, Height = 30, Width = width, Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = true });
        stack.Children.Add(panel);
        return stack;
    }

    private static IEnumerable<Scene> AvatarSuggestScenes =>
    [
        new Scene("suggest-avatar-groups", "アバターの候補：3群の見出し（検索・商品ページ）と、改変を選ぶ窓の2群。呼び方で当たった行は名前の横に札が出る（左から：空の入力／「mzh」で絞る／改変を選ぶ窓）", context =>
        {
            string[] all =
            [
                "作り物のミズホ（9900001）", "作り物のカナタ 絵あり（9900002）", "作り物の素体（共通素体）", "別の作り物の素体（共通素体）",
                "作り物のアオイ（9900003）", "作り物のシズク（9900004）", "作り物のヒカリ（9900005）",
            ];
            var infos = new Dictionary<string, SuggestInfo>
            {
                ["作り物のミズホ（9900001）"] = new(0, [new SuggestHint("Mzh", "呼び方「Mzh」")]),
                ["作り物のカナタ 絵あり（9900002）"] = new(0, []),
                ["作り物の素体（共通素体）"] = new(1, []),
                ["別の作り物の素体（共通素体）"] = new(1, []),
                ["作り物のアオイ（9900003）"] = new(2, [new SuggestHint("Mzh-sister", "呼び方「Mzh-sister」")]),
                ["作り物のシズク（9900004）"] = new(2, []),
                ["作り物のヒカリ（9900005）"] = new(2, []),
            };
            var withoutBase = all.Where(entry => !entry.EndsWith("（共通素体）")).ToList();
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(SuggestPanel("検索の対応アバター／商品ページの対応アバターを足す欄（空の入力）", all, string.Empty, text => infos.GetValueOrDefault(text), ["所持アバター", "共通素体", "未所持アバター"]));
            host.Children.Add(SuggestPanel("「mzh」と打つ（呼び方で当たる。名前で当たった行には札が付かない）", all, "mzh", text => infos.GetValueOrDefault(text), ["所持アバター", "共通素体", "未所持アバター"]));
            host.Children.Add(SuggestPanel("改変を選ぶ窓の「どのアバターの改変か」（共通素体は選べないので2群）", withoutBase, string.Empty, text => infos.GetValueOrDefault(text), ["所持アバター", "共通素体", "未所持アバター"]));
            return Task.FromResult(new Shot(SceneContext.OnSurface(host, 20)));
        })
        {
            Width = null,
            Height = null,
        },

        // 検索の「改変」の候補（「アバター名：改変名」）。アバター名の呼び方でも当たり、名前で当たった行には札が付かない（2026-10-05 判断⑥）
        new Scene("suggest-modification-alias", "検索の改変の候補：空の入力／「mzh」（呼び方で当たる）／「普段着」（名前で当たる。札なし）", context =>
        {
            string[] all =
            [
                "作り物のミズホ（このアバターの改変すべて）", "作り物のミズホ：普段着", "作り物のミズホ：水着",
                "作り物のカナタ（このアバターの改変すべて）", "作り物のカナタ：普段着",
            ];
            var mizuho = new SuggestInfo(0, [new SuggestHint("Mzh", "呼び方「Mzh」")]);
            var infos = all.ToDictionary(text => text, text => text.StartsWith("作り物のミズホ") ? mizuho : new SuggestInfo(0, []));
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(SuggestPanel("空の入力", all, string.Empty, text => infos.GetValueOrDefault(text), null));
            host.Children.Add(SuggestPanel("「mzh」と打つ", all, "mzh", text => infos.GetValueOrDefault(text), null));
            host.Children.Add(SuggestPanel("「普段着」と打つ", all, "普段着", text => infos.GetValueOrDefault(text), null));
            return Task.FromResult(new Shot(SceneContext.OnSurface(host, 20)));
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("pick-modification-icons-dialog", "改変に追加の窓：今ある改変の行の絵（1行目は改変の写真・2行目は写真が無くアバターの絵・3行目はどちらも無く頭文字）", context =>
        {
            var photo = SwatchFile("photo", Color.FromRgb(232, 150, 120));
            var avatarPicture = SwatchFile("avatar", Color.FromRgb(110, 170, 150));
            var loader = new ThumbnailLoader();

            PickModificationRowViewModel Row(string id, string name, string? icon, int members) => new()
            {
                Record = new ModificationRecord
                {
                    Id = id,
                    AvatarItemId = "9900001",
                    Name = name,
                    Members = [.. Enumerable.Range(0, members).Select(index => new ModificationMember { ItemId = $"99001{index:00}" })],
                },
                AvatarText = "作り物のアバター",
                IconPath = icon,
                Thumbnails = () => loader,
            };

            var model = new PickModificationDialogViewModel(
                "改変に追加",
                "「作り物の衣装」を改変に追加します。",
                string.Empty,
                [Row("mod-1", "普段着", photo, 3), Row("mod-2", "制服", avatarPicture, 1), Row("mod-3", "水着", null, 0)],
                [],
                _ => null)
            {
                ExistingLabel = "今ある改変に追加",
                CommitLabel = "追加",
                EmptyText = "改変がまだありません。",
            };
            var window = new PickModificationDialog(model);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },
    ];
}
