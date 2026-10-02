using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chmonos.App.Tests.Support;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 名前の「_」が画面で消えないこと（ユーザ指摘 2026-10-02 メモ1：「名前のアンダーバーは作り手の側で保証できるはず」）。
///
/// WPF の標準の型は、中身が文字のとき最初の「_」をアクセスキーの印として食べて、「tag_name_01」を「tagname_01」と描く。
/// アプリの型は ContentPresenter の <c>RecognizesAccessKey</c> を外してあるが、型を差し替えていない部品や、後から付けた型で
/// 戻ると、見た目では気付きにくい。部品の種類ごとに名前を渡して組み、描かれた文字に「_」が残るかを機械で見る。
/// 消えるときは、描く木の中に <see cref="AccessText"/> ができる（食った文字を下線付きで描く部品）。
/// </summary>
public class NameUnderscoreTests
{
    private const string Name = "tag_name_01";

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
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
    /// 部品を組んで並べ、描く木を作る。ポップアップの中ではなく、閉じたままの見た目を見る。
    /// 入れ物（Grid）に載せる：1つだけで並べると、一覧・選ぶ欄・メニューは型が組まれず、木が空のまま「消えていない」と答えてしまう
    /// </summary>
    private static List<DependencyObject> Layout(FrameworkElement part)
    {
        var host = new Grid();
        host.Children.Add(part);
        host.Measure(new Size(600, 400));
        host.Arrange(new Rect(0, 0, 600, 400));
        host.UpdateLayout();
        return [.. Descendants(host)];
    }

    private static void AssertKeepsUnderscore(string kind, FrameworkElement part, bool mustShowName = true)
    {
        var tree = Layout(part);

        // 先に食べたかを見る：食べると文字の部品が見つからず、原因の違う失敗に見えてしまう
        Assert.False(
            tree.OfType<AccessText>().Any(),
            $"{kind}：型が「_」をアクセスキーの印として食べています（RecognizesAccessKey）");
        if (mustShowName)
        {
            Assert.True(
                tree.OfType<TextBlock>().Any(text => text.Text == Name),
                $"{kind}：描かれた文字に「{Name}」が見つかりません（文字が描かれていません）");
        }
    }

    [Fact]
    public Task 中身が文字の部品は_型を通しても_が消えない() => UiThread.Run(() =>
    {
        AssertKeepsUnderscore("文字（見本）", new TextBlock { Text = Name });
        AssertKeepsUnderscore("ボタン", new Button { Content = Name });
        AssertKeepsUnderscore("チェック", new CheckBox { Content = Name });
        AssertKeepsUnderscore("ラジオ", new RadioButton { Content = Name });
        AssertKeepsUnderscore("切り替えのボタン", new ToggleButton { Content = Name });
        AssertKeepsUnderscore("ラベル", new Label { Content = Name });
        AssertKeepsUnderscore("畳む欄の見出し", new Expander { Header = Name });
        AssertKeepsUnderscore("チェック（中に TextBlock）", new CheckBox { Content = new TextBlock { Text = Name } });
    });

    [Fact]
    public Task 管理の画面の三角の畳む欄でも_が消えない() => UiThread.Run(() =>
    {
        var manage = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Chmonos;component/Views/ManageResources.xaml"),
        };

        AssertKeepsUnderscore("三角の畳む欄", new Expander { Header = Name, Style = (Style)manage["TriangleExpander"] });
    });

    [Fact]
    public Task メニューの項目と一覧の列の見出しと選ぶ欄でも_が消えない() => UiThread.Run(() =>
    {
        var menu = new Menu();
        menu.Items.Add(new MenuItem { Header = Name });
        AssertKeepsUnderscore("メニューの項目", menu);

        var list = new ListView { Width = 300, Height = 60 };
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = Name, Width = 200 });
        list.View = view;
        AssertKeepsUnderscore("リストの列の見出し", list);

        var combo = new ComboBox { Width = 200 };
        combo.Items.Add(Name);
        combo.SelectedIndex = 0;
        AssertKeepsUnderscore("選ぶ欄", combo);

        var box = new ListBox();
        box.Items.Add(new ListBoxItem { Content = Name });
        AssertKeepsUnderscore("一覧の行", box);
    });

    [Fact]
    public Task アプリの資源にある_ボタンや切り替えの型は_どれも_が消えない() => UiThread.Run(() =>
    {
        // 名前を付けた型（SmallButton・PrimaryButton・FolderGlyph など）は画面ごとに付け替えて使うので、置いてある物を全部通す
        var styles = Styles(Application.Current.Resources).Distinct().ToList();
        Assert.NotEmpty(styles);

        var checkedCount = 0;
        foreach (var style in styles)
        {
            var target = style.TargetType;
            // 吹き出しと窓は入れ物に載せられない（載せると例外）。吹き出しの中身は部品の型ではなく、載せた文字の部品
            if (!typeof(ContentControl).IsAssignableFrom(target) || target.IsAbstract || target.GetConstructor(Type.EmptyTypes) is null
                || typeof(ToolTip).IsAssignableFrom(target) || typeof(Window).IsAssignableFrom(target))
            {
                continue;
            }

            var part = (FrameworkElement)Activator.CreateInstance(target)!;
            if (part is HeaderedContentControl headered)
            {
                headered.Header = Name;
            }
            else if (part is ContentControl content)
            {
                content.Content = Name;
            }

            part.Style = style;
            // 矢印の部品（RepeatButton など）は中身の文字を描かない型なので、描かれているかは問わず、食べていないことだけ見る
            AssertKeepsUnderscore($"型（{target.Name}）", part, mustShowName: false);
            checkedCount++;
        }

        Assert.True(checkedCount > 0, "通した型が1つもありません。資源の読み込みが変わっていないか確かめてください");
    });

    private static IEnumerable<Style> Styles(ResourceDictionary dictionary)
    {
        foreach (var value in dictionary.Values)
        {
            if (value is Style style)
            {
                yield return style;
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var style in Styles(merged))
            {
                yield return style;
            }
        }
    }
}
