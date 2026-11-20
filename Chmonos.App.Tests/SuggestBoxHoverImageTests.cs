using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 候補の欄（<see cref="SuggestBox"/>）の頭の絵に乗せると、大きな絵を出す（ユーザ判断 2026-11-20・①「アバターの管理の画面の絵は全部、乗せると大きな絵を出す」）。
/// 付けた欄（<see cref="SuggestBox.HoverIconSelector"/>）の、絵のある既存の行だけ。大きな絵は吹き出しが開いた時に初めて読む。
/// 部品はアプリ全体で使うので、付けない欄は絵を余分に読まない。名前は作り物
/// </summary>
public sealed class SuggestBoxHoverImageTests
{
    private static readonly BitmapSource Pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);

    [Fact]
    public void 付けていない欄の行は_吹き出しを出さず_頭の絵も余分に読まない()
    {
        var iconCalls = 0;
        var row = new Suggestion
        {
            Value = "作り物のアバター",
            Display = "作り物のアバター",
            IconFactory = _ => { iconCalls++; return Pixel; },
        };

        Assert.False(row.HasHoverImage);
        Assert.Null(row.HoverImage);
        Assert.Equal(0, iconCalls);
    }

    [Fact]
    public void 新規の行と_絵の無い行は_吹き出しを出さず_大きな絵を読まない()
    {
        var hoverCalls = 0;
        Func<string, Action, ImageSource?> hover = (_, _) => { hoverCalls++; return Pixel; };
        var fresh = new Suggestion { Value = "新しい語", Display = "新しい語", IsNew = true, IconFactory = _ => Pixel, HoverIconFactory = hover };
        var plain = new Suggestion { Value = "絵の無い素体", Display = "絵の無い素体", IconFactory = _ => null, HoverIconFactory = hover };

        Assert.False(fresh.HasHoverImage);
        Assert.Null(fresh.HoverImage);
        Assert.False(plain.HasHoverImage);
        Assert.Equal(0, hoverCalls);
    }

    [Fact]
    public void 行を作って頭の絵を出しても大きな絵は読まず_乗せて引いた時に読み_届いたら知らせる()
    {
        var asked = new List<string>();
        Action? loaded = null;
        var row = new Suggestion
        {
            Value = "作り物のアバター",
            Display = "作り物のアバター",
            IconFactory = _ => Pixel,
            HoverIconFactory = (value, onLoaded) => { asked.Add(value); loaded = onLoaded; return null; },
        };
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // 行が描く時に引く物（頭の絵・出し分け）
        _ = row.Icon;
        _ = row.IconVisibility;
        Assert.True(row.HasHoverImage);
        Assert.Empty(asked);

        // 吹き出しが開いて引く。手元に無ければ null で、裏で読み終えたら知らせる
        Assert.Null(row.HoverImage);
        Assert.Equal(["作り物のアバター"], asked);
        loaded!();
        Assert.Equal([nameof(Suggestion.HoverImage)], changed);
    }

    /// <summary>
    /// 候補の行は Popup の中にあり、吹き出しの型は親の画面が取り込む ManageResources.xaml にある。
    /// Popup 越しに型が届き、付けた欄の絵のある行だけ吹き出しが効き、付けない欄の行は止まっている事
    /// </summary>
    [Fact]
    public Task 候補の窓の行に_親の画面の吹き出しの型が届き_付けた欄だけで効く() => UiThread.Run(async () =>
    {
        var hovered = await RowTipOf(hover: (_, _) => Pixel);
        Assert.IsType<ToolTip>(hovered.Tip);
        Assert.True(hovered.Enabled);

        var plain = await RowTipOf(hover: null);
        Assert.False(plain.Enabled);
    });

    private static async Task<(object? Tip, bool Enabled)> RowTipOf(Func<string, Action, ImageSource?>? hover)
    {
        var box = new SuggestBox
        {
            Source = new List<string> { "作り物のアバター" }, AllowNew = false, Width = 280,
            IconSelector = _ => Pixel, HoverIconSelector = hover,
        };
        var host = new Grid { Children = { box } };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Chmonos;component/Views/ManageResources.xaml"),
        });
        var window = new Window
        {
            Content = host, Width = 400, Height = 300, ShowActivated = true,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = 0,
        };
        window.Show();
        window.Activate();
        try
        {
            box.FocusInput();
            await UiThread.Settle();
            var drop = (Popup)box.FindName("DropDown");
            Assert.True(drop.IsOpen, "候補の窓が開く");
            await UiThread.Settle();

            var candidates = (ListBox)box.FindName("Candidates");
            var icon = Descendants(candidates).OfType<Border>().Single(border => border.Width == 24 && border.Height == 24);
            return (icon.ToolTip, ToolTipService.GetIsEnabled(icon));
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>
    /// 付けた欄：アバターの管理の絵付きの候補の欄（変換の組の元・先、素体の探す欄、アバターの詳細の共通素体、素体のアバターを足す欄）と、
    /// 変換扱いのつなぎ先を選ぶ欄（商品ページにも出る）。ほかの画面の候補の欄には付けない
    /// </summary>
    [Fact]
    public void 付けたのは_アバターの管理の絵付きの候補の欄と_変換扱いのつなぎ先の欄だけ()
    {
        Assert.Equal(5, Regex.Matches(Read("AvatarsView.xaml"), @"HoverIconSelector=""\{Binding \w*HoverIconSelector\}""").Count);
        Assert.Single(Regex.Matches(Read("ItemConversionPanel.xaml"), "HoverIconSelector="));

        foreach (var other in new[] { "SearchView.xaml", "EditView.xaml", "ItemAvatarsPanel.xaml", "PickModificationDialog.xaml" })
        {
            Assert.DoesNotContain("HoverIconSelector", Read(other));
        }
    }

    private static string Read(string name, [CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views", name));
}
