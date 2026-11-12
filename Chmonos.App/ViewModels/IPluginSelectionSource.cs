namespace Chmonos.App.ViewModels;

/// <summary>
/// 複数選択の帯を持つ画面（検索・フォルダの右側・ショップ）。帯の「ほかのソフト ▾」が、押した時点の選択を読む（plan 44 の②）。
/// </summary>
public interface IPluginSelectionSource
{
    /// <summary>選んでいる商品の ID（表示の並びの順。選択の出来事と同じ並べ方）。</summary>
    IReadOnlyList<string> SelectedItemIdsForPlugins();

    /// <summary>ほかのソフトの項目の係（帯のボタンを出すか・押したときの組み立て）。</summary>
    Services.PluginMenuService PluginMenus { get; }
}
