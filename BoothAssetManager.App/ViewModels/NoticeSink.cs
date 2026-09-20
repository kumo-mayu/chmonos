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
