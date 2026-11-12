namespace Chmonos.App.ViewModels;

/// <summary>
/// ほかのソフトの項目を押した後の下の帯（plan 44 の②）。押すのは右クリック・帯のメニューで、知らせる行を持たないので、
/// 非表示の帯と同じく主画面の下に1本だけ出す。中身は係（<see cref="Services.PluginMenuService"/>）が持つ
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>ほかのソフトの項目の係（カードの右クリックが主の窓から引く・下の帯が結ぶ）。</summary>
    public Services.PluginMenuService PluginMenus => _services.PluginMenus;
}
