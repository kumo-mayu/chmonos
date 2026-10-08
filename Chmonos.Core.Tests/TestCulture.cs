using System.Globalization;
using System.Runtime.CompilerServices;

namespace Chmonos.Core.Tests;

/// <summary>
/// 試験の一式を、いつも日本語の設定（ja-JP）で走らせる（App の試験の <c>TestCulture</c> と同じ理由）。
/// 試験を走らせる PC の言語に、並べ替えや比べ方の結果が左右されないようにする（GitHub の環境は英語）
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
