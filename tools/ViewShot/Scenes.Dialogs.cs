using System.Windows;
using System.Windows.Controls;

namespace ViewShot;

internal static partial class Scenes
{
    // 小窓は中身の大きさで描く（窓の題の帯と枠は Windows が描くので、この台では出ない）
    private static IEnumerable<Scene> Dialogs =>
    [
        Notice(
            "notice-okcancel-long",
            "確認の窓：OK／キャンセル・長い本文（折り返しと、既定がキャンセルのときのボタン）",
            "統合しますか",
            "「かわいい」を「可愛い」に統合します。「かわいい」が付いている 128 件の商品は、すべて「可愛い」に付け替わります。\n\n"
            + "小分類の「ふわふわ」「もこもこ」「ゆるい」は、同じ名前の小分類があればそこへ、無ければ「可愛い」の下へ移ります。\n\n"
            + "統合すると元に戻せません。",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel),

        Notice(
            "notice-yesno-warning",
            "確認の窓：はい／いいえ・警告の印・空白の無い長いパス",
            "保存先が見つかりません",
            "データの保存先が見つかりません。\n\n"
            + @"E:\VeryLongFolderNameWithoutAnySpaces\AnotherVeryLongFolderNameWithoutAnySpaces\Chmonos\data" + "\n\n"
            + "外付けドライブを外している場合は、つないでからもう一度開いてください。\n\n"
            + "［はい］既定の場所（%LOCALAPPDATA%）で開きます。保存先の設定はそちらに変わります。\n"
            + "［いいえ］何もせずに終了します。つなぎ直してから開き直せます。",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No),

        Notice(
            "notice-error-short",
            "知らせの窓：OK だけ・エラーの印・短い本文（最小の幅）",
            "Chmonos",
            "送るのを中止しました。",
            MessageBoxButton.OK,
            MessageBoxImage.Error),

        // どこを探すかの窓（見つからない・移動の点検 11-A）。監視フォルダ2つと、この回だけ足した長いパス1つ（1つは外してある）
        new Scene("missing-search-scope-dialog", "見つからないファイルを探す窓：監視フォルダ2つ・今回だけ足した長いパス・1つはチェックを外した", context =>
        {
            var model = new Chmonos.App.ViewModels.MissingSearchScopeViewModel([@"D:\Booth\downloads", @"E:\Assets"]);
            model.AddFolders([@"F:\VeryLongFolderNameWithoutAnySpaces\AnotherVeryLongFolderName\moved-assets"]);
            model.Rows[1].IsChecked = false;
            var window = new Chmonos.App.Views.MissingSearchScopeDialog(model);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("missing-search-scope-empty", "見つからないファイルを探す窓：監視フォルダが無い（空の文・「探す」が押せない理由）", context =>
        {
            var model = new Chmonos.App.ViewModels.MissingSearchScopeViewModel([]);
            var window = new Chmonos.App.Views.MissingSearchScopeDialog(model);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },
    ];

    private static Scene Notice(
        string name, string title, string caption, string text, MessageBoxButton button, MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None)
        => new(name, title, context =>
        {
            var window = Backdoor.NewNotice(text, caption, button, icon, defaultResult);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        };

    private static IEnumerable<Scene> Parts =>
    [
        new Scene("calendar", "カレンダー（検索の日付の絞り込み）：月の表示と年の表示", context =>
        {
            // 日付は決め打ち。今日を使うと、日が変わるたびに前後の比べで差が出る
            var day = new DateTime(2026, 9, 17);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var mode in new[] { CalendarMode.Month, CalendarMode.Year })
            {
                row.Children.Add(new Calendar
                {
                    DisplayMode = mode,
                    DisplayDate = day,
                    SelectedDate = day.AddDays(3),
                    Margin = new Thickness(0, 0, 16, 0),
                });
            }

            return Task.FromResult(new Shot(SceneContext.OnSurface(row)));
        })
        {
            Width = null,
            Height = null,
        },

        // 名前の最初の「_」が画面から消えるかを、絵で確かめる場面（2026-09-30）。
        // 型の ContentPresenter が RecognizesAccessKey="True" を持つ部品は、中身が文字のとき、最初の「_」をアクセスキーの印として食べる。
        // 左に部品の種類、右に同じ文字「tag_name_01」を中身に渡した部品。右の字が「tagname_01」になっていれば消えている。
        // アプリの型からはこの指定を全部外したので、今はどの部品でも消えないのが正しい（同日に直した。型に付け直すとここで分かる）
        new Scene("underscore-in-names", "名前の「_」：中身が文字の部品に「tag_name_01」を渡したとき、どの部品で最初の「_」が消えるか", context =>
        {
            const string Name = "tag_name_01";
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            void Row(string kind, FrameworkElement part)
            {
                var index = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = kind, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
                part.HorizontalAlignment = HorizontalAlignment.Left;
                part.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(label, index);
                Grid.SetRow(part, index);
                Grid.SetColumn(part, 1);
                grid.Children.Add(label);
                grid.Children.Add(part);
            }

            Row("文字（TextBlock）＝見本", new TextBlock { Text = Name });
            Row("ボタン", new Button { Content = Name });
            Row("チェック", new CheckBox { Content = Name });
            Row("ラジオ", new RadioButton { Content = Name });
            Row("切り替えのボタン（ToggleButton）", new System.Windows.Controls.Primitives.ToggleButton { Content = Name });
            Row("畳む欄の見出し（Expander）", new Expander { Header = Name });
            // 三角の畳む欄の見た目は、管理の画面の資源に置いてある
            var manage = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Chmonos;component/Views/ManageResources.xaml"),
            };
            Row("畳む欄の見出し（三角の Expander）", new Expander { Header = Name, Style = (Style)manage["TriangleExpander"] });

            var menu = new Menu { Background = System.Windows.Media.Brushes.Transparent };
            menu.Items.Add(new MenuItem { Header = Name });
            Row("メニューの項目", menu);

            var list = new ListView { Width = 240, Height = 34, BorderThickness = new Thickness(0) };
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = Name, Width = 200 });
            list.View = view;
            Row("リストの列の見出し", list);

            var combo = new ComboBox { Width = 200 };
            combo.Items.Add(Name);
            combo.SelectedIndex = 0;
            Row("選ぶ欄（ComboBox）", combo);

            var box = new ListBox { BorderThickness = new Thickness(0) };
            box.Items.Add(new ListBoxItem { Content = Name });
            Row("一覧の行（ListBoxItem）", box);

            // チェックの中に文字の部品を入れる形（このアプリの大半のチェックはこの形）
            Row("チェック（中に TextBlock）", new CheckBox { Content = new TextBlock { Text = Name } });

            return Task.FromResult(new Shot(SceneContext.OnSurface(grid)));
        })
        {
            Width = null,
            Height = null,
        },

        // 実際の窓（属性の統合・小分類の移動）は Scenes.Catalog.cs で描く（先に出さない主の窓を作っておく。PrepareDialogOwner）。
        // この2つの窓のラジオは、上の「ラジオ」と同じ型に、名前入りの文字を中身として渡している
    ];
}
