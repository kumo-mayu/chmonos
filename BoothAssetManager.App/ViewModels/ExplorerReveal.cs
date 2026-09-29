using System.IO;
using BoothAssetManager.App.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 「エクスプローラで開く」で、開く先が無ければ「見つかりません」と言う。
///
/// 外付けを外すと、記録の場所も親フォルダも無く、押しても黙って何も起きなかった。
/// 商品ページのファイルの行は 9ae1344 で直したが、未確定・フォルダビュー・取り込み・改変の2画面に残っていた（洗い出し 14）。
/// 窓の言い方は商品ページと揃える（同じ場面で画面ごとに言い方が違うと、別のことが起きたように読める）
/// </summary>
internal static class ExplorerReveal
{
    /// <summary>開く。無ければ窓で知らせる。道が空（選んでいない）なら押せていないのと同じなので黙る。</summary>
    public static async Task RevealAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (await Shell.TryRevealAsync(path))
        {
            return;
        }

        // ドライブの根（E:\）は名前が空になるので、道のまま言う
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        Notice.Show(
            $"「{(name.Length > 0 ? name : path)}」が、記録にある場所に見つかりません。\n\n"
            + "外付けのドライブなら、つないでからもう一度お試しください。",
            "エクスプローラで開く",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}
