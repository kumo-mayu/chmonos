using System.Windows;
using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// Unity へ送る前の確認と、送った結果の言い方を決める1か所（ユーザ判断 2026-11-03。2026-11-19 に設定を3つに分けた・open.md 167）。
///
/// 設定の分岐を送る道ごとに撒かないため、ここに集める。設定で変えられるのは、読まずに進んでも困らない物だけ：
/// 「送ります」の確認（<see cref="Core.Models.AppSettings.ConfirmsUnitySend"/>）と、送れた・止めたの結果
/// （知らせるか <see cref="Core.Models.AppSettings.NotifiesUnitySendResult"/>・知らせ方 <see cref="Core.Models.AppSettings.UnitySendResultByToast"/>）。
/// **送れなかったことは窓で言う**（理由を読まないと次の手が決まらない）。
/// 別のプロジェクトへ送る・飛ばす物があるときの確認は、呼ぶ側がこの口を通さずに今までどおり聞く。
/// </summary>
internal static class UnitySendNotice
{
    /// <summary>「送ります」の確認。確認しない設定なら聞かずに進める。</summary>
    /// <returns>送ってよいか。</returns>
    public static bool Confirm(
        AppServiceContainer services, string text, string title, MessageBoxResult defaultResult = MessageBoxResult.OK)
        => !services.Settings.ConfirmsUnitySend
           || Notice.Show(text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, defaultResult) == MessageBoxResult.OK;

    /// <summary>
    /// 送れた・止めたの結果。知らせない設定なら何も出さない（主の窓も Unity の手前へ戻さない）。
    /// Windows の通知の設定なら通知で言い、出せなかったら窓で言う（知らせると決めた結果を黙って捨てない）。
    /// </summary>
    public static void Result(AppServiceContainer services, string text, string title)
    {
        var settings = services.Settings;
        if (!settings.NotifiesUnitySendResult)
        {
            return;
        }

        if (settings.UnitySendResultByToast && WindowsToast.Show(title, text))
        {
            return;
        }

        FrontNotice.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
