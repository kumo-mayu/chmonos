using System.IO;
using System.Windows;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 初回だけ出す画面。
///
/// 聞くのは<b>後から変えると高くつく2つ</b>だけ——保存先と、画像を取るかどうか。
/// 3つ以上並べると、答えより「早く始めたい」が勝って読まずに次へ押される。
/// 読まれない質問は無いのと同じなので、残りは既定のまま設定画面に置いてある。
///
/// 本体のウィンドウではなく別の窓にしているのは、ここで保存先が変わり得るから。
/// サービス一式は保存先を決めてから組み立てる必要があり、
/// 同じウィンドウの中で差し替えると「組み立て直す」経路を作ることになる。
/// </summary>
public partial class FirstRunWindow : Window
{
    private string _root;

    private FirstRunWindow()
    {
        InitializeComponent();
        _root = StoreLocation.Resolve().Path;
        RootText.Text = _root;
    }

    /// <summary>
    /// 一度も起動していないか。
    ///
    /// 印を別に持たず、<c>settings.json</c> の有無をそのまま使う。
    /// 設定ファイルが無いことが「まだ何も決めていない」を意味するので、
    /// 新しく覚えることが増えない。既に使っている人には二度と出ない。
    /// </summary>
    public static bool IsNeeded()
    {
        var root = StoreLocation.Resolve();
        return !File.Exists(Path.Combine(root.Path, "settings.json"));
    }

    /// <summary>
    /// 初回の設定を聞いて書く。閉じられた（はじめないと言われた）なら false。
    /// </summary>
    public static bool Run()
    {
        var window = new FirstRunWindow();
        return window.ShowDialog() == true;
    }

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "データの保存先を選ぶ",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var picked = dialog.FolderName;

        // 既に別のライブラリが入っている場所を選んだら、そちらを開くことになる。
        // 黙って混ざるより、選び直す機会を出す
        if (StoreLocation.LooksLikeStore(picked))
        {
            var answer = MessageBox.Show(
                $"選んだ場所には既にライブラリがあります。\n\n{picked}\n\n"
                + "そのまま開くと、そのライブラリの続きから始まります。",
                "既にライブラリがあります",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.OK);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        _root = picked;
        RootText.Text = picked;
        Notice.Text = "「はじめる」を押すとこの場所を使います。";
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(_root),
                    Path.TrimEndingDirectorySeparator(StoreLocation.DefaultRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                StoreLocation.Save(_root);
            }

            Directory.CreateDirectory(_root);

            // 既定値の上に、聞いた1つだけを乗せて書く。
            // ここでファイルができるので、次からこの画面は出ない
            var settings = new AppSettings { SaveImages = SaveImagesCheck.IsChecked == true };
            JsonStore.Write(Path.Combine(_root, "settings.json"), settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"設定を書けませんでした。\n\n{exception.Message}\n\n"
                + "書き込める場所を選び直してください。",
                "保存先に書けません",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
