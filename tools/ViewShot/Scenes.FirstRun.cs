using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Views;

namespace ViewShot;

internal static partial class Scenes
{
    /// <summary>
    /// 初回の窓（公開前の点検 2026-10-01：選んだ長いパスの末尾が切れ、最後のフォルダ名が見えなかった）。
    /// 窓の幅は 620 で固定（窓の枠の分を引いた中身の幅で描く）。保存先の欄と下の理由の文を見る
    /// </summary>
    private const double FirstRunClientWidth = 604;

    private const string FirstRunLongPath =
        @"D:\作り物のフォルダ\とても長い名前のフォルダ（入れ子1）\さらに長い名前のフォルダ（入れ子2）\VRChat用の素材置き場\Chmonos";

    private static IEnumerable<Scene> FirstRun =>
    [
        FirstRunScene("first-run-long-path", "初回の窓：深い場所を選んだ（長いパス。最後のフォルダ名が見えるか）",
            FirstRunLongPath, "選んだ場所の中に「Chmonos」フォルダを作って、そこを使います。", canPick: true),
        FirstRunScene("first-run-short-path", "初回の窓：既定の場所（短いパス。省かずに出る）",
            @"C:\Users\user\AppData\Local\Chmonos", string.Empty, canPick: true),
        FirstRunScene("first-run-environment", "初回の窓：環境変数で保存先が決まっている（理由の文の折り返し）",
            FirstRunLongPath, null, canPick: false),
    ];

    /// <param name="notice">下の理由の文。null は窓が自分で書いた文のまま（環境変数のとき）。</param>
    private static Scene FirstRunScene(string name, string title, string path, string? notice, bool canPick)
        => new(name, title, context =>
        {
            var constructor = typeof(FirstRunWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
                ?? throw new InvalidOperationException("FirstRunWindow のコンストラクタが見つかりません。");
            var window = (FirstRunWindow)constructor.Invoke(null);

            var rootText = (FrameworkElement)window.FindName("RootText");
            if (rootText.GetType().GetProperty("Path") is { } pathProperty)
            {
                pathProperty.SetValue(rootText, path);
            }
            else
            {
                ((TextBlock)rootText).Text = path;
            }

            ((Button)window.FindName("PickButton")).IsEnabled = canPick;
            var noticeText = (TextBlock)window.FindName("Notice");
            if (notice is not null)
            {
                noticeText.Text = notice;
            }
            else if (noticeText.Text.Length == 0)
            {
                // 台が環境変数を付けずに走っているときも、同じ文で描く
                noticeText.Text = "環境変数CHMONOS_HOMEで保存先が指定されているため、ここからは変えられません。";
            }

            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = FirstRunClientWidth,
            Height = null,
        };
}
