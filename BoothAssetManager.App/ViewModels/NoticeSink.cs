namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 操作の結果を言う先。どこに出すかは呼んだ側が決める（画面の1行か、知らせの窓か）。
///
/// <paramref name="failed"/> を添えるのは、**窓に出すときに失敗を情報の顔で出していた**ため
/// （D8：<c>FrontNotice</c> の既定が Information で、「送れませんでした」も ℹ の見た目になっていた）。
/// 既定値を持たせてあるので、うまくいったことを言う側は今まで通り文字だけ渡せる。
/// </summary>
/// <param name="text">言う中身。空文字は「言うことは無い（途中の表示を消す）」の意味。</param>
/// <param name="failed">できなかったことか。窓なら ⚠ で出す。</param>
internal delegate void NoticeSink(string text, bool failed = false);

/// <summary>結果の言い先を作る（ユーザ判断 2026-09-20・D6）。</summary>
internal static class Notices
{
    /// <summary>
    /// **うまくいったことは画面の1行に、できなかったことは窓に**出す先を作る。
    ///
    /// 行に出した失敗は、次の操作をすると押し出されて消えてしまい、何が起きたのか追えない。
    /// 行を持たない画面（カードの右クリック）は、うまくいったことも窓で言うしかない。
    /// **見れば分かること**（Unity の取り込み画面が出る・画像が消える・フォルダが一覧に出る）は、そもそも言わない（D7）。
    /// </summary>
    /// <param name="title">窓の題。何の操作の結果かが分かる名前。</param>
    /// <param name="setLine">その画面の1行に書く。</param>
    public static NoticeSink LineOrWindow(string title, Action<string> setLine)
        => (text, failed) =>
        {
            if (!failed)
            {
                setLine(text);
                return;
            }

            // 途中の表示（「調べています…」）を残したまま窓を出すと、何が今の状態か分からなくなる
            setLine(string.Empty);
            Services.FrontNotice.Show(
                text, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        };
}
