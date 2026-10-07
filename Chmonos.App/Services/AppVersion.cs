using System.Reflection;

namespace Chmonos.App.Services;

/// <summary>
/// アプリの版（<c>Directory.Build.props</c> の <c>Version</c>）。
/// 画面のどこにも版が無く、不具合を知らせてもらうときにどの版かを聞けなかった（ユーザ判断 2026-10-07：設定の「このアプリについて」に出す）。
/// </summary>
internal static class AppVersion
{
    public static string Text { get; } = Read(typeof(AppVersion).Assembly);

    private static string Read(Assembly assembly)
        => Strip(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version);

    /// <summary>
    /// 情報用の版には、組むときに「+コミットの印」が後ろに付くので切る（使う人には意味が無い）。
    /// 情報用の版が無ければ、アセンブリの版の上3つ
    /// </summary>
    internal static string Strip(string? informational, Version? fallback)
    {
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return fallback?.ToString(3) ?? string.Empty;
    }
}
