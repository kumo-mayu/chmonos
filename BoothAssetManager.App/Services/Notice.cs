using BoothAssetManager.Core.Services;
using System.Windows;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 画面から出す知らせ・確認の窓を通す1か所（<see cref="MessageBox"/> の代わり）。
///
/// **なぜ1か所に集めるか：**出した文言と押されたボタンを足跡に残すため（<see cref="UiTrace"/>・ユーザ指示 2026-09-20）。
/// Win32 の MessageBox は中身が UI Automation に出ない物があり、確かめのたびに撮って読んでいた。
/// 出す形そのものは変えていない（持ち主を足すと出る位置が変わるので、手前に出したいときは <see cref="FrontNotice"/> を使う）
/// </summary>
internal static class Notice
{
    public static MessageBoxResult Show(
        string text,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var answer = defaultResult == MessageBoxResult.None
            ? MessageBox.Show(text, caption, button, icon)
            : MessageBox.Show(text, caption, button, icon, defaultResult);
        Trace(caption, text, button, answer);
        return answer;
    }

    /// <summary>持ち主付き（<see cref="FrontNotice"/> から。主の窓の真ん中に出て、後ろに隠れない）。</summary>
    public static MessageBoxResult Show(
        Window owner,
        string text,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var answer = defaultResult == MessageBoxResult.None
            ? MessageBox.Show(owner, text, caption, button, icon)
            : MessageBox.Show(owner, text, caption, button, icon, defaultResult);
        Trace(caption, text, button, answer);
        return answer;
    }

    private static void Trace(string caption, string text, MessageBoxButton button, MessageBoxResult answer)
    {
        if (UiTrace.IsOn)
        {
            UiTrace.Write("知らせ", $"「{caption}」{text}｜{button} → {answer}");
        }
    }
}
