namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定の「除外の解除…」の窓（open.md 132・ユーザ判断 2026-11-19）。除外の一覧の共通部品（<see cref="ExclusionListViewModel"/>）を窓に出す。
/// 前の「外した n 件を戻す」は直前に外した物しか戻せず、前に外した物は設定の奥まで行かないと戻せなかった。
/// </summary>
public static class ExclusionDialog
{
    /// <summary>
    /// 窓を出す代わりに一覧を受ける口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<ExclusionListViewModel, Task>? Intercept { get; set; }

    /// <summary>窓を開き、閉じるまで待つ。解除は窓の中で済む（閉じた後の読み直しは呼び手が行う）。</summary>
    public static async Task ShowAsync(ExclusionListViewModel model)
    {
        model.IsActive = true;
        try
        {
            if (Intercept is { } intercept)
            {
                await intercept(model);
            }
            else
            {
                new Views.ExclusionListDialog(model).ShowDialog();
            }
        }
        finally
        {
            model.IsActive = false;
        }
    }
}
