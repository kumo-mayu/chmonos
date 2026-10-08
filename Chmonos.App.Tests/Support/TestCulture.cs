using System.Globalization;
using System.Runtime.CompilerServices;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 試験の一式を、いつも日本語の設定（ja-JP）で走らせる。
///
/// 並べ替え・比べ方の一部は、使う人の言語の並べ方（<see cref="StringComparer.CurrentCulture"/>）に従う作りで、使う人は日本語の環境にいる。
/// 試験を走らせる PC の言語に結果が左右されると、GitHub の英語の環境でだけ候補の並ぶ順が変わって落ちた（2026-10-08。
/// CLAUDE.md「実マシンの状態に結果が左右される試験を書かない」）。画面のスレッドも含め、後から作るスレッドにも効かせる
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseJapanese()
    {
        var japanese = CultureInfo.GetCultureInfo("ja-JP");
        CultureInfo.DefaultThreadCurrentCulture = japanese;
        CultureInfo.DefaultThreadCurrentUICulture = japanese;
        CultureInfo.CurrentCulture = japanese;
        CultureInfo.CurrentUICulture = japanese;
    }
}
