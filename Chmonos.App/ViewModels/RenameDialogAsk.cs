namespace Chmonos.App.ViewModels;

/// <summary>
/// 名前を変える窓（<see cref="Views.RenameTagDialog"/>）を出す口。タグの管理（大分類・小分類）と属性の管理が使う。
/// 試験は <see cref="Intercept"/> で答える（窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）。
/// 通知の「もう一度操作する」（open 145）で、前の値が欄に入って窓が開くことを試験で見るために置いた
/// </summary>
internal static class RenameDialogAsk
{
    /// <summary>窓を出す代わりに答える口。**アプリでは null のまま**。</summary>
    internal static Func<RenameTagDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓を出す。「変更」を押したら true。</summary>
    public static bool Ask(RenameTagDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.RenameTagDialog(model).ShowDialog() == true;
}
