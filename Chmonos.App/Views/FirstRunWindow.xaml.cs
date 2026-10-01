using System.IO;
using System.Windows;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Views;

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
        var resolved = StoreLocation.Resolve();
        _root = resolved.Path;
        RootText.Path = _root;
        _fromEnvironment = resolved.Source == StoreRootSource.Environment;

        // 環境変数で決まった保存先は選び直せない（選んでも環境変数が勝つ）。設定画面の「場所を変える」と同じく理由を出す
        if (_fromEnvironment)
        {
            PickButton.IsEnabled = false;
            Notice.Text = "環境変数CHMONOS_HOMEで保存先が指定されているため、ここからは変えられません。";
        }

        Services.DialogFit.Prepare(this);
    }

    /// <summary>
    /// 保存先が環境変数から来たか。**そのときは location.json を書かない。**
    /// 書くと、環境変数は確かめのための一時の逃げ道なのに、環境変数を外した普段の起動までその場所へ向いてしまう
    /// （撮影用の空の保存先で「はじめる」を押し、普段の起動が消した撮影用フォルダを開こうとした・2026-09-17）。
    /// </summary>
    private readonly bool _fromEnvironment;

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

    /// <summary>
    /// Esc でも閉じられるようにする（D1：窓はどれも Esc で抜けられる）。
    /// ×と同じ扱いで、まだ何も書いていないので失う物は無い（押し間違えても、次の起動でまたこの窓が出る）。
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            Close();
        }

        base.OnPreviewKeyDown(e);
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

        // 選んだ場所の中に「Chmonos」を作って使う（ライブラリがある場所ならそのまま）
        var picked = StoreLocation.RootFor(dialog.FolderName);

        // 既に別のライブラリが入っている場所を選んだら、そちらを開くことになる。
        // 黙って混ざるより、選び直す機会を出す
        if (StoreLocation.LooksLikeStore(picked))
        {
            var answer = Services.Notice.Show(
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
        RootText.Path = picked;

        // 作るのか、そのまま使うのかを押す前に言う
        Notice.Text = string.Equals(picked, Path.TrimEndingDirectorySeparator(dialog.FolderName), StringComparison.OrdinalIgnoreCase)
            ? "「はじめる」を押すとこの場所を使います。"
            : $"選んだ場所の中に「{StoreLocation.FolderName}」フォルダを作って、そこを使います。";
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_fromEnvironment && !string.Equals(
                    Path.TrimEndingDirectorySeparator(_root),
                    Path.TrimEndingDirectorySeparator(StoreLocation.DefaultRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                StoreLocation.Save(_root);
            }

            Directory.CreateDirectory(_root);

            // 既定値の上に、聞いた1つだけを乗せて書く。
            // ここでファイルができるので、次からこの画面は出ない。
            // **既にあるライブラリを選んだときは書かない。**丸ごと書き直すと、そのライブラリの取り込み元・監視・
            // 間隔などの設定が既定値に戻っていた（点検 2026-09-28）。そのライブラリの設定をそのまま使う
            var settingsFile = Path.Combine(_root, "settings.json");
            if (!File.Exists(settingsFile))
            {
                var settings = new AppSettings { SaveImages = SaveImagesCheck.IsChecked == true };
                JsonStore.Write(settingsFile, settings);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Services.Notice.Show(
                // ログはまだ書き先が決まっていない（保存先を決める窓なので）。見当と次の一手だけ出す
                $"「{_root}」に設定を書けませんでした。\n\n{Core.Services.FailureText.Cause(exception)}\n\n"
                + "書き込める場所を選び直してください。",
                "保存先に書けません",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
