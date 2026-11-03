using System.Windows;
using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// Unity へ送る前の確認と、送った結果の言い方を決める1か所（設定 <see cref="Core.Models.AppSettings.QuietUnitySend"/>・ユーザ判断 2026-11-03）。
///
/// 設定の分岐を送る道ごとに撒かないため、ここに集める。オンのときに静かにするのは、読まずに進んでも困らない物だけ：
/// 「送ります」の確認と、送れた・止めたの結果。**送れなかったことは窓で言う**（理由を読まないと次の手が決まらない）。
/// 別のプロジェクトへ送る・飛ばす物があるときの確認は、呼ぶ側がこの口を通さずに今までどおり聞く。
/// </summary>
internal static class UnitySendNotice
{
    /// <summary>「送ります」の確認。静かにする設定なら聞かずに進める。</summary>
    /// <returns>送ってよいか。</returns>
    public static bool Confirm(
        AppServiceContainer services, string text, string title, MessageBoxResult defaultResult = MessageBoxResult.OK)
        => services.Settings.QuietUnitySend
           || Notice.Show(text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, defaultResult) == MessageBoxResult.OK;

    /// <summary>
    /// 送れた・止めたの結果。静かにする設定なら Windows の通知で言い、主の窓を Unity の手前へ戻さない。
    /// 通知を出せなかったら窓で言う（結果を黙って捨てない）。
    /// </summary>
    public static void Result(AppServiceContainer services, string text, string title)
    {
        if (services.Settings.QuietUnitySend && WindowsToast.Show(title, text))
        {
            return;
        }

        FrontNotice.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
